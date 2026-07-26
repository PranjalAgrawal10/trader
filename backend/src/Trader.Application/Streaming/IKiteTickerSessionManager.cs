namespace Trader.Application.Streaming;

/// <summary>Per-user Kite WebSocket ticker: one connection per user, shared across SignalR tabs.</summary>
public interface IKiteTickerSessionManager
{
    Task SubscribeAsync(string connectionId, Guid userId, uint instrumentToken, CancellationToken ct = default);

    Task UnsubscribeAsync(string connectionId, Guid userId, uint instrumentToken, CancellationToken ct = default);

    Task RemoveConnectionAsync(string connectionId, CancellationToken ct = default);

    /// <summary>
    /// Host-owned subscription (no SignalR tab). Used by Opening ATM trail so GTT updates
    /// run on live ticks even when the SPA is closed. <paramref name="leaseId"/> must be stable per run.
    /// </summary>
    Task EnsureBackgroundSubscribeAsync(Guid userId, uint instrumentToken, string leaseId, CancellationToken ct = default);

    Task ReleaseBackgroundSubscribeAsync(Guid userId, uint instrumentToken, string leaseId, CancellationToken ct = default);
}
