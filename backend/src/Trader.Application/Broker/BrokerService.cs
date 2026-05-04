using Microsoft.Extensions.Options;
using Trader.Application.Configuration;
using Newtonsoft.Json;

namespace Trader.Application.Broker;

public sealed class BrokerService : IBrokerService
{
    private readonly IBrokerSetupGateway _brokerSetup;
    private readonly IKiteOAuthStateCodec _stateCodec;
    private readonly IKiteSessionExchange _kiteSessionExchange;
    private readonly IKiteInstrumentsClient _kiteInstruments;
    private readonly IOptions<ZerodhaKiteOptions> _kiteOptions;

    public BrokerService(
        IBrokerSetupGateway brokerSetup,
        IKiteOAuthStateCodec stateCodec,
        IKiteSessionExchange kiteSessionExchange,
        IKiteInstrumentsClient kiteInstruments,
        IOptions<ZerodhaKiteOptions> kiteOptions)
    {
        _brokerSetup = brokerSetup;
        _stateCodec = stateCodec;
        _kiteSessionExchange = kiteSessionExchange;
        _kiteInstruments = kiteInstruments;
        _kiteOptions = kiteOptions;
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

    public Task<KiteLoginUrlDto> GetKiteLoginUrlAsync(Guid userId, CancellationToken ct = default)
    {
        _ = ct;
        var opt = _kiteOptions.Value;
        if (string.IsNullOrWhiteSpace(opt.ApiKey) || string.IsNullOrWhiteSpace(opt.RedirectUrl))
        {
            throw new InvalidOperationException(
                "Zerodha Kite is not configured. Set ZerodhaKite:ApiKey and ZerodhaKite:RedirectUrl (see README).");
        }

        var state = _stateCodec.Encode(userId);
        var url =
            $"https://kite.zerodha.com/connect/login?v=3&api_key={Uri.EscapeDataString(opt.ApiKey)}&state={Uri.EscapeDataString(state)}";
        return Task.FromResult(new KiteLoginUrlDto(url));
    }

    public async Task<BrokerStatusDto> CompleteKiteOAuthAsync(string requestToken, string state, CancellationToken ct = default)
    {
        var userId = _stateCodec.TryDecode(state)
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
            throw new InvalidOperationException("Zerodha Kite is not configured. Set ZerodhaKite:ApiKey.");

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
}
