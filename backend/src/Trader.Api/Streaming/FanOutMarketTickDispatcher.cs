using Trader.Application.Streaming;
using Trader.Infrastructure.Streaming;

namespace Trader.Api.Streaming;

/// <summary>Forwards ticks to SignalR batching and live candle persistence.</summary>
public sealed class FanOutMarketTickDispatcher : IMarketTickDispatcher
{
    private readonly SignalRMarketTickDispatcher _signalR;
    private readonly LiveCandleTickSubscriber _liveCandles;

    public FanOutMarketTickDispatcher(SignalRMarketTickDispatcher signalR, LiveCandleTickSubscriber liveCandles)
    {
        _signalR = signalR;
        _liveCandles = liveCandles;
    }

    public void Publish(Guid userId, MarketTickDto tick)
    {
        _signalR.Publish(userId, tick);
        _liveCandles.OnTick(userId, tick);
    }
}
