using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Trader.Api.Hubs;
using Trader.Application.Streaming;

namespace Trader.Api.Streaming;

/// <summary>Batches ticks (~150ms) per user and sends them as <c>ticks</c> to the user's SignalR group.</summary>
public sealed class SignalRMarketTickDispatcher : IMarketTickDispatcher, IDisposable
{
    private const int FlushMilliseconds = 150;

    private readonly IHubContext<MarketHub> _hub;
    private readonly ILogger<SignalRMarketTickDispatcher> _logger;
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<uint, MarketTickDto>> _pending = new();
    private readonly Timer _timer;

    public SignalRMarketTickDispatcher(IHubContext<MarketHub> hub, ILogger<SignalRMarketTickDispatcher> logger)
    {
        _hub = hub;
        _logger = logger;
        _timer = new Timer(_ => { _ = FlushAsync(); }, null, FlushMilliseconds, FlushMilliseconds);
    }

    public void Publish(Guid userId, MarketTickDto tick)
    {
        var inner = _pending.GetOrAdd(userId, static _ => new ConcurrentDictionary<uint, MarketTickDto>());
        inner[tick.InstrumentToken] = tick;
    }

    private async Task FlushAsync()
    {
        foreach (var (userId, inner) in _pending.ToArray())
        {
            var batch = new List<MarketTickDto>();
            foreach (var key in inner.Keys.ToArray())
            {
                if (inner.TryRemove(key, out var dto))
                    batch.Add(dto);
            }

            if (batch.Count == 0)
                continue;

            try
            {
                await _hub.Clients.Group(MarketHub.UserGroupName(userId)).SendAsync("ticks", batch).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to push ticks for user {UserId}", userId);
            }
        }
    }

    public void Dispose() => _timer.Dispose();
}
