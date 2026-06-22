using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Trader.Application.Abstractions.Persistence;
using Trader.Application.Configuration;
using Trader.Application.Wallet;
using Trader.Domain.Entities;


namespace Trader.Application.Broker;

public sealed partial class BrokerService
{
    public async Task<BrokerStatusDto> GetStatusAsync(Guid userId, CancellationToken ct = default)
    {
        var snapshot = await _brokerSetup.GetSnapshotAsync(userId, ct);
        if (snapshot is null)
            throw new InvalidOperationException("User not found.");

        var provider = snapshot.BrokerProvider;
        var at = snapshot.BrokerConnectedAt;
        return new BrokerStatusDto(!string.IsNullOrEmpty(provider), at, provider);
    }

    public async Task<IReadOnlyList<BrokerProviderAvailabilityDto>> GetOrderBrokerProvidersAsync(Guid userId, CancellationToken ct = default)
    {
        await RequireUserExistsAsync(userId, ct).ConfigureAwait(false);
        var connected = await _brokerSetup.GetConnectedBrokerProvidersAsync(userId, ct).ConfigureAwait(false);
        var set = new HashSet<string>(connected.Select(x => x.Trim().ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);
        return new[]
        {
            new BrokerProviderAvailabilityDto(BrokerZerodha, "Zerodha Kite", set.Contains(BrokerZerodha)),
            new BrokerProviderAvailabilityDto(BrokerGroww, "Groww", set.Contains(BrokerGroww)),
        };
    }

    public Task CompleteSetupAsync(Guid userId, CancellationToken ct = default) =>
        _brokerSetup.CompleteBrokerSetupAsync(userId, ct);

    public Task<KiteLoginUrlBuildResult> GetKiteLoginUrlAsync(Guid userId, CancellationToken ct = default)
    {
        _ = ct;
        var opt = _kiteOptions.Value;
        if (string.IsNullOrWhiteSpace(opt.ApiKey) || string.IsNullOrWhiteSpace(opt.RedirectUrl))
        {
            throw new InvalidOperationException(
                "Zerodha Kite is not configured. Set environment variables ZerodhaKite__ApiKey and ZerodhaKite__RedirectUrl (or use .env.development in Development; see README).");
        }

        var fullState = _stateCodec.Encode(userId);
        var shortKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        _memoryCache.Set(PendingStateCachePrefix + shortKey, fullState, PendingStateTtl);

        // redirect_params: Kite appends these to the redirect URL (see kite.trade docs). Backup if `state` is omitted.
        var redirectParams = Uri.EscapeDataString($"trader_oauth={shortKey}");
        var url =
            $"https://kite.zerodha.com/connect/login?v=3&api_key={Uri.EscapeDataString(opt.ApiKey)}&state={Uri.EscapeDataString(shortKey)}&redirect_params={redirectParams}";
        return Task.FromResult(new KiteLoginUrlBuildResult(url, shortKey));
    }

    public async Task<BrokerStatusDto> CompleteKiteOAuthAsync(string requestToken, string state, CancellationToken ct = default)
    {
        var (signedState, pendingKey) = ResolveKiteOAuthState(state);
        var userId = _stateCodec.TryDecode(signedState)
                     ?? throw new InvalidOperationException("Invalid or expired OAuth state. Try connecting again.");

        var exchanged = await _kiteSessionExchange.ExchangeAsync(requestToken, ct);
        if (!exchanged.Success || string.IsNullOrEmpty(exchanged.AccessToken) || string.IsNullOrEmpty(exchanged.KiteUserId))
        {
            throw new InvalidOperationException(exchanged.ErrorMessage ?? "Could not complete Kite login.");
        }

        await _brokerSetup.PersistKiteSessionAsync(
            userId,
            new BrokerKitePersistRequest(exchanged.AccessToken, exchanged.RefreshToken, exchanged.KiteUserId),
            ct);

        if (pendingKey is not null)
            _memoryCache.Remove(PendingStateCachePrefix + pendingKey);

        return await GetStatusAsync(userId, ct);
    }

    public async Task<BrokerStatusDto> ConnectGrowwAsync(Guid userId, GrowwConnectRequestDto body, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(body);

        var accessToken = NormalizeOptional(body.AccessToken);
        var apiKey = NormalizeOptional(body.ApiKey);
        var apiSecret = NormalizeOptional(body.ApiSecret);
        var totp = NormalizeOptional(body.Totp);

        GrowwTokenAccessResult? generated = null;
        if (accessToken is null)
        {
            if (apiKey is null)
                throw new InvalidOperationException("Provide accessToken, or provide apiKey with apiSecret/totp.");

            if (apiSecret is not null)
            {
                generated = await _growwTrading.CreateAccessTokenByApprovalAsync(apiKey, apiSecret, ct).ConfigureAwait(false);
            }
            else if (totp is not null)
            {
                generated = await _growwTrading.CreateAccessTokenByTotpAsync(apiKey, totp, ct).ConfigureAwait(false);
            }
            else
            {
                throw new InvalidOperationException("apiSecret or totp is required when accessToken is not provided.");
            }

            if (!generated.Success || string.IsNullOrWhiteSpace(generated.AccessToken))
                throw new InvalidOperationException(generated.ErrorMessage ?? "Could not create Groww access token.");

            accessToken = generated.AccessToken.Trim();
        }

        await _brokerSetup.PersistGrowwSessionAsync(
            userId,
            new BrokerGrowwPersistRequest(
                accessToken,
                generated?.ExpiresAt,
                apiKey),
            ct).ConfigureAwait(false);

        return await GetStatusAsync(userId, ct).ConfigureAwait(false);
    }

    public async Task<BrokerStatusDto> SetActiveBrokerAsync(Guid userId, string broker, CancellationToken ct = default)
    {
        var normalized = NormalizeRequired(broker, "broker").ToLowerInvariant();
        var ok = await _brokerSetup.SetActiveBrokerAsync(userId, normalized, ct).ConfigureAwait(false);
        if (!ok)
            throw new InvalidOperationException($"Broker '{normalized}' is not connected for this user.");
        return await GetStatusAsync(userId, ct).ConfigureAwait(false);
    }

    public async Task<BrokerStatusDto> DisconnectAsync(Guid userId, string? broker = null, CancellationToken ct = default)
    {
        var normalized = string.IsNullOrWhiteSpace(broker) ? null : broker.Trim();
        await _brokerSetup.DisconnectBrokerAsync(userId, normalized, ct);
        return await GetStatusAsync(userId, ct);
    }
    /// <summary>Maps callback <paramref name="state"/> to the HMAC payload. Returns the server cache key when resolved from memory (for one-time removal).</summary>
    private (string SignedState, string? PendingKey) ResolveKiteOAuthState(string state)
    {
        if (string.IsNullOrWhiteSpace(state))
            return (state, null);

        var trimmed = state.Trim();

        if (trimmed.Length == 32
            && trimmed.All(char.IsAsciiHexDigit)
            && _memoryCache.TryGetValue(
                PendingStateCachePrefix + trimmed.ToLowerInvariant(),
                out var cached)
            && cached is string full
            && !string.IsNullOrEmpty(full))
        {
            return (full, trimmed.ToLowerInvariant());
        }

        return (trimmed, null);
    }
}
