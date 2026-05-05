using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Trader.Application.Configuration;

namespace Trader.Application.Broker;

public sealed class BrokerService : IBrokerService
{
    private const string PendingStateCachePrefix = "Trader.KiteOAuth.PendingState:";
    private static readonly TimeSpan PendingStateTtl = TimeSpan.FromMinutes(20);

    private readonly IBrokerSetupGateway _brokerSetup;
    private readonly IKiteOAuthStateCodec _stateCodec;
    private readonly IKiteSessionExchange _kiteSessionExchange;
    private readonly IKiteInstrumentsClient _kiteInstruments;
    private readonly IOptions<ZerodhaKiteOptions> _kiteOptions;
    private readonly IMemoryCache _memoryCache;

    public BrokerService(
        IBrokerSetupGateway brokerSetup,
        IKiteOAuthStateCodec stateCodec,
        IKiteSessionExchange kiteSessionExchange,
        IKiteInstrumentsClient kiteInstruments,
        IOptions<ZerodhaKiteOptions> kiteOptions,
        IMemoryCache memoryCache)
    {
        _brokerSetup = brokerSetup;
        _stateCodec = stateCodec;
        _kiteSessionExchange = kiteSessionExchange;
        _kiteInstruments = kiteInstruments;
        _kiteOptions = kiteOptions;
        _memoryCache = memoryCache;
    }

    public async Task<BrokerStatusDto> GetStatusAsync(Guid userId, CancellationToken ct = default)
    {
        var snapshot = await _brokerSetup.GetSnapshotAsync(userId, ct);
        if (snapshot is null)
            throw new InvalidOperationException("User not found.");

        var at = snapshot.BrokerConnectedAt;
        return new BrokerStatusDto(at.HasValue, at, snapshot.BrokerProvider);
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

    public async Task<BrokerStatusDto> DisconnectAsync(Guid userId, CancellationToken ct = default)
    {
        await _brokerSetup.DisconnectBrokerAsync(userId, ct);
        return await GetStatusAsync(userId, ct);
    }

    public async Task<KiteFnoCommodityListsDto> GetKiteFnoCommodityInstrumentsAsync(Guid userId, CancellationToken ct = default)
    {
        var snapshot = await _brokerSetup.GetSnapshotAsync(userId, ct);
        if (snapshot is null)
            throw new InvalidOperationException("User not found.");

        if (!string.Equals(snapshot.BrokerProvider, "Zerodha", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Zerodha (Kite) is not connected.");

        var accessToken = await _brokerSetup.GetKiteAccessTokenAsync(userId, ct);
        if (string.IsNullOrEmpty(accessToken))
            throw new InvalidOperationException("No valid Kite session. Reconnect Zerodha.");

        var apiKey = _kiteOptions.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "Zerodha Kite is not configured. Set environment variable ZerodhaKite__ApiKey (see README).");

        var nfo = await _kiteInstruments.FetchExchangeInstrumentsAsync("NFO", apiKey, accessToken, maxRows: null, ct);
        if (!nfo.Success)
            throw new InvalidOperationException(nfo.ErrorMessage ?? "Could not load NFO instruments from Kite.");

        var fno = new List<KiteInstrumentListItemDto>(nfo.Items);

        var bfo = await _kiteInstruments.FetchExchangeInstrumentsAsync("BFO", apiKey, accessToken, maxRows: null, ct);
        if (bfo.Success)
            fno.AddRange(bfo.Items);

        var mcx = await _kiteInstruments.FetchExchangeInstrumentsAsync("MCX", apiKey, accessToken, maxRows: null, ct);
        if (!mcx.Success)
            throw new InvalidOperationException(mcx.ErrorMessage ?? "Could not load MCX instruments from Kite.");

        return new KiteFnoCommodityListsDto(fno, mcx.Items, false, false);
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
