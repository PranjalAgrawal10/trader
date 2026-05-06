namespace Trader.Application.Streaming;

/// <summary>Delivers normalized ticks toward realtime clients (SignalR, etc.).</summary>
public interface IMarketTickDispatcher
{
    void Publish(Guid userId, MarketTickDto tick);
}
