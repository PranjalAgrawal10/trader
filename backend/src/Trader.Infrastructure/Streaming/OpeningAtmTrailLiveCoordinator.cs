using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Application.Abstractions.Persistence;
using Trader.Application.Abstractions.Streaming;
using Trader.Application.Broker;
using Trader.Application.Configuration;
using Trader.Application.Streaming;

namespace Trader.Infrastructure.Streaming;

/// <summary>
/// Tick-driven Opening ATM GTT trail. Subscribes to Kite LTP in the background and coalesces
/// per-run work so approach bumps fire as soon as ticks arrive (Kite GTT PUT RTT remains).
/// </summary>
public sealed class OpeningAtmTrailLiveCoordinator : IOpeningAtmTrailLiveCoordinator
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<NiftyOpenAutoTradeOptions> _options;
    private readonly ILogger<OpeningAtmTrailLiveCoordinator> _logger;

    private readonly ConcurrentDictionary<Guid, LiveLease> _byRunId = new();
    private readonly ConcurrentDictionary<(Guid UserId, uint Token), Guid> _byUserToken = new();
    private readonly ConcurrentDictionary<Guid, decimal> _pendingLtp = new();
    private readonly ConcurrentDictionary<Guid, byte> _inflight = new();

    public OpeningAtmTrailLiveCoordinator(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<NiftyOpenAutoTradeOptions> options,
        ILogger<OpeningAtmTrailLiveCoordinator> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    public void Register(Guid runId, Guid userId, uint instrumentToken)
    {
        if (runId == Guid.Empty || userId == Guid.Empty || instrumentToken == 0)
            return;

        var lease = new LiveLease(runId, userId, instrumentToken);
        _byRunId[runId] = lease;
        _byUserToken[(userId, instrumentToken)] = runId;

        _ = EnsureTickerAsync(lease);
    }

    public void Unregister(Guid runId)
    {
        if (!_byRunId.TryRemove(runId, out var lease))
            return;

        _byUserToken.TryRemove((lease.UserId, lease.InstrumentToken), out _);
        _pendingLtp.TryRemove(runId, out _);
        _inflight.TryRemove(runId, out _);
        _ = ReleaseTickerAsync(lease);
    }

    public void OnTick(Guid userId, MarketTickDto tick)
    {
        if (!_options.CurrentValue.Enabled || tick.LastPrice <= 0)
            return;

        if (!_byUserToken.TryGetValue((userId, tick.InstrumentToken), out var runId))
            return;

        _pendingLtp[runId] = tick.LastPrice;
        TryKick(runId);
    }

    public async Task SyncFromDatabaseAsync(CancellationToken ct = default)
    {
        if (!_options.CurrentValue.Enabled)
        {
            foreach (var id in _byRunId.Keys.ToArray())
                Unregister(id);
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var runs = scope.ServiceProvider.GetRequiredService<INiftyOpenAutoTradeRunRepository>();
        var active = await runs.ListActiveTrailingAsync(ct).ConfigureAwait(false);
        var wanted = new HashSet<Guid>();

        foreach (var run in active)
        {
            if (!TryParseToken(run.InstrumentToken, out var token))
                continue;

            wanted.Add(run.Id);
            if (!_byRunId.ContainsKey(run.Id))
                Register(run.Id, run.UserId, token);
        }

        foreach (var id in _byRunId.Keys.ToArray())
        {
            if (!wanted.Contains(id))
                Unregister(id);
        }
    }

    private void TryKick(Guid runId)
    {
        if (!_inflight.TryAdd(runId, 0))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                while (_pendingLtp.TryRemove(runId, out var ltp))
                {
                    // Drain to latest LTP if more ticks arrived while awaiting GTT.
                    while (_pendingLtp.TryRemove(runId, out var newer))
                        ltp = newer;

                    try
                    {
                        await using var scope = _scopeFactory.CreateAsyncScope();
                        var svc = scope.ServiceProvider.GetRequiredService<NiftyOpenAutoTradeService>();
                        await svc.TrailFromLiveTickAsync(runId, ltp, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Live Opening ATM trail failed for run {RunId}", runId);
                    }

                    if (!_pendingLtp.ContainsKey(runId))
                        break;
                }
            }
            finally
            {
                _inflight.TryRemove(runId, out _);
                if (_pendingLtp.ContainsKey(runId))
                    TryKick(runId);
            }
        });
    }

    private async Task EnsureTickerAsync(LiveLease lease)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var ticker = scope.ServiceProvider.GetRequiredService<IKiteTickerSessionManager>();
            await ticker
                .EnsureBackgroundSubscribeAsync(lease.UserId, lease.InstrumentToken, lease.RunId.ToString("N"), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not subscribe Opening ATM trail ticker for run {RunId} user {UserId} token {Token}",
                lease.RunId,
                lease.UserId,
                lease.InstrumentToken);
        }
    }

    private async Task ReleaseTickerAsync(LiveLease lease)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var ticker = scope.ServiceProvider.GetRequiredService<IKiteTickerSessionManager>();
            await ticker
                .ReleaseBackgroundSubscribeAsync(lease.UserId, lease.InstrumentToken, lease.RunId.ToString("N"), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Release Opening ATM trail ticker for run {RunId}", lease.RunId);
        }
    }

    private static bool TryParseToken(string? raw, out uint token)
    {
        token = 0;
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        return uint.TryParse(raw.Trim(), out token) && token != 0;
    }

    private sealed record LiveLease(Guid RunId, Guid UserId, uint InstrumentToken);
}
