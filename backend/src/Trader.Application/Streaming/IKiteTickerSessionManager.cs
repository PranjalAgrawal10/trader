namespace Trader.Application.Streaming;

/// <summary>Per-user Kite WebSocket ticker: one connection per user, shared across SignalR tabs.</summary>
public interface IKiteTickerSessionManager
{
    Task SubscribeAsync(string connectionId, Guid userId, uint instrumentToken, CancellationToken ct = default);

    Task UnsubscribeAsync(string connectionId, Guid userId, uint instrumentToken, CancellationToken ct = default);

    Task RemoveConnectionAsync(string connectionId, CancellationToken ct = default);
}
