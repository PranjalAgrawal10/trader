using Trader.Application.Streaming;

namespace Trader.Application.Abstractions.Streaming;

/// <summary>
/// Near-real-time Opening ATM GTT trail: registers active runs and reacts to Kite LTP ticks
/// (instead of waiting for the background poll).
/// </summary>
public interface IOpeningAtmTrailLiveCoordinator
{
    void Register(Guid runId, Guid userId, uint instrumentToken);

    void Unregister(Guid runId);

    /// <summary>Hot path from the Kite ticker fan-out — must not block.</summary>
    void OnTick(Guid userId, MarketTickDto tick);

    /// <summary>Reconcile in-memory leases + host ticker subscriptions with DB TrailActive rows.</summary>
    Task SyncFromDatabaseAsync(CancellationToken ct = default);
}
