namespace Trader.Application.Broker;

public interface IBrokerService
{
    Task<BrokerStatusDto> GetStatusAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Marks broker setup complete without a live broker (demo / placeholder).</summary>
    Task CompleteSetupAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Kite login URL including signed <c>state</c> for the current user.</summary>
    Task<KiteLoginUrlDto> GetKiteLoginUrlAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Completes OAuth using request_token and state from Kite redirect.</summary>
    Task<BrokerStatusDto> CompleteKiteOAuthAsync(string requestToken, string state, CancellationToken ct = default);

    /// <summary>Clears stored broker session and onboarding completion for the user.</summary>
    Task<BrokerStatusDto> DisconnectAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Full F&O master (NFO + BFO) and MCX commodity instruments from Kite’s daily CSV dumps.
    /// </summary>
    Task<KiteFnoCommodityListsDto> GetKiteFnoCommodityInstrumentsAsync(Guid userId, CancellationToken ct = default);
}
