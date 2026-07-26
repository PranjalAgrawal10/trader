using Trader.Application.Abstractions.Streaming;
using Trader.Application.Streaming;
using Trader.Infrastructure.Streaming;

namespace Trader.Api.Streaming;

/// <summary>Forwards ticks to SignalR batching, live candles, and Opening ATM GTT trail.</summary>
public sealed class FanOutMarketTickDispatcher : IMarketTickDispatcher
{
    private readonly SignalRMarketTickDispatcher _signalR;
    private readonly LiveCandleTickSubscriber _liveCandles;
    private readonly IOpeningAtmTrailLiveCoordinator _openingAtmTrail;

    public FanOutMarketTickDispatcher(
        SignalRMarketTickDispatcher signalR,
        LiveCandleTickSubscriber liveCandles,
        IOpeningAtmTrailLiveCoordinator openingAtmTrail)
    {
        _signalR = signalR;
        _liveCandles = liveCandles;
        _openingAtmTrail = openingAtmTrail;
    }

    public void Publish(Guid userId, MarketTickDto tick)
    {
        _signalR.Publish(userId, tick);
        _liveCandles.OnTick(userId, tick);
        _openingAtmTrail.OnTick(userId, tick);
    }
}
