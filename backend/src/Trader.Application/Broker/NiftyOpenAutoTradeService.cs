using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Application.Abstractions.Persistence;
using Trader.Application.Abstractions.Streaming;
using Trader.Application.Configuration;
using Trader.Domain.Entities;
using Trader.Domain.Enums;

namespace Trader.Application.Broker;

/// <summary>
/// NIFTY Opening ATM: live MIS BUY at ~09:15 IST with balance-aware max lots and ± GTT exits.
/// Distinct from demo/paper auto-trade.
/// </summary>
public sealed class NiftyOpenAutoTradeService
{
    private readonly IOptionsMonitor<NiftyOpenAutoTradeOptions> _options;
    private readonly IBrokerService _broker;
    private readonly IUserRepository _users;
    private readonly INiftyOpenAutoTradeRunRepository _runs;
    private readonly IOpeningAtmTrailLiveCoordinator _trailLive;
    private readonly ILogger<NiftyOpenAutoTradeService> _logger;

    public NiftyOpenAutoTradeService(
        IOptionsMonitor<NiftyOpenAutoTradeOptions> options,
        IBrokerService broker,
        IUserRepository users,
        INiftyOpenAutoTradeRunRepository runs,
        IOpeningAtmTrailLiveCoordinator trailLive,
        ILogger<NiftyOpenAutoTradeService> logger)
    {
        _options = options;
        _broker = broker;
        _users = users;
        _runs = runs;
        _trailLive = trailLive;
        _logger = logger;
    }

    public async Task RunCycleAsync(CancellationToken ct = default)
    {
        var opts = _options.CurrentValue;
        if (!opts.Enabled)
            return;

        var utcNow = DateTimeOffset.UtcNow;
        if (!NiftyOpenAutoTradeSchedule.IsInsideFireWindow(opts, utcNow))
            return;

        var tz = NiftyOpenAutoTradeSchedule.ResolveTimeZone(opts.TimeZoneId);
        var sessionDate = NiftyOpenAutoTradeSchedule.GetSessionDateIst(utcNow, tz);
        var userIds = await _users.ListIdsWithNiftyOpenAutoTradeEnabledAsync(ct).ConfigureAwait(false);
        foreach (var userId in userIds)
        {
            try
            {
                await TryExecuteForUserAsync(userId, sessionDate, dryRun: false, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NIFTY open auto-trade failed for user {UserId}", userId);
            }
        }
    }

    /// <summary>Raises active trailing GTT stops while positions remain open during the trail window.</summary>
    public async Task RunTrailCycleAsync(CancellationToken ct = default)
    {
        var opts = _options.CurrentValue;
        if (!opts.Enabled)
            return;

        var utcNow = DateTimeOffset.UtcNow;
        var tz = NiftyOpenAutoTradeSchedule.ResolveTimeZone(opts.TimeZoneId);
        var sessionDate = NiftyOpenAutoTradeSchedule.GetSessionDateIst(utcNow, tz);
        var active = await _runs.ListActiveTrailingAsync(ct).ConfigureAwait(false);
        if (active.Count == 0)
            return;

        var insideTrail = NiftyOpenAutoTradeSchedule.IsInsideTrailWindow(opts, utcNow);
        foreach (var run in active)
        {
            try
            {
                if (!insideTrail || run.SessionDateIst != sessionDate)
                {
                    run.TrailActive = false;
                    run.Message = Truncate(
                        AppendNote(
                            run.Message,
                            run.SessionDateIst != sessionDate ? "Trail cleared (prior session)." : "Trail window ended."),
                        1000);
                    await _runs.SaveChangesAsync(ct).ConfigureAwait(false);
                    _trailLive.Unregister(run.Id);
                    continue;
                }

                await TrailOneRunAsync(run, opts, liveLtp: null, ct).ConfigureAwait(false);
                if (!run.TrailActive)
                    _trailLive.Unregister(run.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "NIFTY open trail failed for run {RunId} user {UserId}", run.Id, run.UserId);
            }
        }
    }

    /// <summary>
    /// Hot path from Kite LTP ticks — same trail/approach logic as the poll cycle, without re-quoting.
    /// </summary>
    public async Task TrailFromLiveTickAsync(Guid runId, decimal liveLtp, CancellationToken ct = default)
    {
        if (liveLtp <= 0)
            return;

        var opts = _options.CurrentValue;
        if (!opts.Enabled)
            return;

        var run = await _runs.GetTrackedByIdAsync(runId, ct).ConfigureAwait(false);
        if (run is null || !run.TrailActive)
        {
            _trailLive.Unregister(runId);
            return;
        }

        var utcNow = DateTimeOffset.UtcNow;
        var tz = NiftyOpenAutoTradeSchedule.ResolveTimeZone(opts.TimeZoneId);
        var sessionDate = NiftyOpenAutoTradeSchedule.GetSessionDateIst(utcNow, tz);
        if (!NiftyOpenAutoTradeSchedule.IsInsideTrailWindow(opts, utcNow) || run.SessionDateIst != sessionDate)
        {
            run.TrailActive = false;
            run.Message = Truncate(
                AppendNote(
                    run.Message,
                    run.SessionDateIst != sessionDate ? "Trail cleared (prior session)." : "Trail window ended."),
                1000);
            await _runs.SaveChangesAsync(ct).ConfigureAwait(false);
            _trailLive.Unregister(run.Id);
            return;
        }

        await TrailOneRunAsync(run, opts, liveLtp, ct).ConfigureAwait(false);
        if (!run.TrailActive)
            _trailLive.Unregister(run.Id);
    }

    public async Task<NiftyOpenAutoTradeSettingsDto> GetSettingsAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await RequireUserAsync(userId, ct).ConfigureAwait(false);
        var last = await _runs.GetLatestByUserAsync(userId, ct).ConfigureAwait(false);
        var available = await TryListAvailableExpiriesAsync(user, ct).ConfigureAwait(false);
        return MapSettings(user, last, available);
    }

    public async Task SaveSettingsAsync(Guid userId, NiftyOpenAutoTradeSettingsPutDto body, CancellationToken ct = default)
    {
        if (body is null)
            throw new InvalidOperationException("Request body is required.");

        var opts = _options.CurrentValue;
        var user = await RequireUserAsync(userId, ct).ConfigureAwait(false);
        var previousUnderlying = OpeningAtmUnderlyings.Resolve(user.NiftyOpenAutoTradeUnderlying).Key;
        var nextUnderlying = OpeningAtmUnderlyings.Resolve(body.Underlying);
        user.NiftyOpenAutoTradeEnabled = body.Enabled;
        user.NiftyOpenAutoTradeUnderlying = nextUnderlying.Key;
        user.NiftyOpenAutoTradeOptionSide = NiftyOpenAutoTradeOptionSideParser.ToEnum(body.OptionSide);
        var maxLots = body.MaxLots > 0 ? body.MaxLots : opts.DefaultMaxLots;
        user.NiftyOpenAutoTradeMaxLots = Math.Clamp(maxLots, 1, Math.Max(1, opts.AbsoluteMaxLots));
        // Changing underlying invalidates a leftover expiry from another index unless the client sends a new one.
        if (!string.Equals(previousUnderlying, nextUnderlying.Key, StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(body.Expiry))
        {
            user.NiftyOpenAutoTradeExpiry = null;
        }
        else
        {
            user.NiftyOpenAutoTradeExpiry = ParsePreferredExpiry(body.Expiry);
        }
        user.NiftyOpenAutoTradeStopLossPoints = NiftyOpenAutoTradeTrail.ClampGttPercent(
            body.StopLossPercent > 0 ? body.StopLossPercent : opts.DefaultStopLossPoints);
        user.NiftyOpenAutoTradeTargetPoints = NiftyOpenAutoTradeTrail.ClampGttPercent(
            body.TargetPercent > 0 ? body.TargetPercent : opts.DefaultTargetPoints);
        user.NiftyOpenAutoTradeStopLossEnabled = body.StopLossEnabled;
        user.NiftyOpenAutoTradeTargetEnabled = body.TargetEnabled;
        // Trail requires −ve SL %; distance is the same stopLossPercent (synced into TrailPoints for the run).
        user.NiftyOpenAutoTradeTrailEnabled = body.TrailEnabled;
        if (user.NiftyOpenAutoTradeTrailEnabled)
        {
            user.NiftyOpenAutoTradeStopLossEnabled = true;
            user.NiftyOpenAutoTradeTrailPoints = user.NiftyOpenAutoTradeStopLossPoints;
        }
        else
        {
            // Keep stored trail gap aligned with last SL % so re-enabling trail stays consistent.
            user.NiftyOpenAutoTradeTrailPoints = user.NiftyOpenAutoTradeStopLossPoints;
        }

        if (!user.NiftyOpenAutoTradeStopLossEnabled && !user.NiftyOpenAutoTradeTargetEnabled)
        {
            throw new InvalidOperationException(
                "Enable at least one of −ve GTT (stop-loss) or +ve GTT (target).");
        }

        await _users.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<NiftyOpenAutoTradeRunDto>> ListRunsAsync(
        Guid userId,
        int? take,
        CancellationToken ct = default)
    {
        await RequireUserAsync(userId, ct).ConfigureAwait(false);
        var n = Math.Clamp(take ?? 20, 1, 100);
        var rows = await _runs.ListByUserAsync(userId, n, ct).ConfigureAwait(false);
        return rows.Select(MapRun).ToList();
    }

    public Task<NiftyOpenAutoTradePreviewDto> PreviewAsync(Guid userId, CancellationToken ct = default) =>
        BuildPreviewOrExecuteAsync(userId, dryRun: true, ct);

    private async Task TryExecuteForUserAsync(
        Guid userId,
        DateOnly sessionDate,
        bool dryRun,
        CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(userId, ct).ConfigureAwait(false);
        if (user is null || !user.NiftyOpenAutoTradeEnabled)
            return;

        if (user.NiftyOpenAutoTradeLastSessionDateIst == sessionDate)
            return;

        // Claim the session day before broker calls to avoid double-fire under overlapping polls.
        user.NiftyOpenAutoTradeLastSessionDateIst = sessionDate;
        await _users.SaveChangesAsync(ct).ConfigureAwait(false);

        await BuildPreviewOrExecuteAsync(userId, dryRun: false, ct, claimedSessionDate: sessionDate)
            .ConfigureAwait(false);
    }

    private async Task<NiftyOpenAutoTradePreviewDto> BuildPreviewOrExecuteAsync(
        Guid userId,
        bool dryRun,
        CancellationToken ct,
        DateOnly? claimedSessionDate = null)
    {
        var opts = _options.CurrentValue;
        var user = await RequireUserAsync(userId, ct).ConfigureAwait(false);
        var underlying = OpeningAtmUnderlyings.Resolve(user.NiftyOpenAutoTradeUnderlying);
        var side = NiftyOpenAutoTradeOptionSideParser.FromEnum(user.NiftyOpenAutoTradeOptionSide);
        var maxLots = Math.Clamp(
            user.NiftyOpenAutoTradeMaxLots > 0 ? user.NiftyOpenAutoTradeMaxLots : opts.DefaultMaxLots,
            1,
            Math.Max(1, opts.AbsoluteMaxLots));
        var stopLossPercent = NiftyOpenAutoTradeTrail.ClampGttPercent(
            user.NiftyOpenAutoTradeStopLossPoints > 0
                ? user.NiftyOpenAutoTradeStopLossPoints
                : opts.DefaultStopLossPoints);
        var targetPercent = NiftyOpenAutoTradeTrail.ClampGttPercent(
            user.NiftyOpenAutoTradeTargetPoints > 0
                ? user.NiftyOpenAutoTradeTargetPoints
                : opts.DefaultTargetPoints);
        var trailEnabled = user.NiftyOpenAutoTradeTrailEnabled;
        // Trail distance is the −ve SL % (same gap from peak as initial stop from entry).
        var trailPercent = stopLossPercent;
        var stopLossEnabled = user.NiftyOpenAutoTradeStopLossEnabled || trailEnabled;
        var targetEnabled = user.NiftyOpenAutoTradeTargetEnabled;
        var effectiveStopPercent = stopLossPercent;
        if (trailEnabled && !stopLossEnabled)
        {
            return await FinishAsync(
                userId,
                dryRun,
                claimedSessionDate,
                false,
                "Trail SL requires −ve GTT (SL %) to be enabled.",
                underlying.Key,
                side,
                maxLots,
                null,
                ct).ConfigureAwait(false);
        }

        if (!stopLossEnabled && !targetEnabled)
        {
            return await FinishAsync(
                userId,
                dryRun,
                claimedSessionDate,
                false,
                "Configure at least one of −ve GTT or +ve GTT on Opening ATM.",
                underlying.Key,
                side,
                maxLots,
                null,
                ct).ConfigureAwait(false);
        }

        try
        {
            var brokerStatus = await _broker.GetStatusAsync(userId, ct).ConfigureAwait(false);
            if (!brokerStatus.Connected
                || !string.Equals(brokerStatus.Provider, "zerodha", StringComparison.OrdinalIgnoreCase))
            {
                return await FinishAsync(
                    userId,
                    dryRun,
                    claimedSessionDate,
                    CanTrade: false,
                    Reason: "Connect Zerodha to enable Opening ATM.",
                    underlying.Key,
                    side,
                    maxLots,
                    null,
                    ct).ConfigureAwait(false);
            }

            var spotSearch = await _broker
                .SearchKiteInstrumentsAsync(userId, underlying.SpotSearchQuery, KiteInstrumentSearchSegment.Spot, ct)
                .ConfigureAwait(false);
            var spotRow = NiftyOpenAutoTradeAtm.ChooseSpotRow(spotSearch.Items, underlying);
            if (spotRow is null)
            {
                return await FinishAsync(
                    userId, dryRun, claimedSessionDate, false, $"Could not resolve {underlying.Label} spot instrument.",
                    underlying.Key, side, maxLots, null, ct).ConfigureAwait(false);
            }

            var spotQuote = await _broker
                .GetKiteInstrumentLiveQuoteAsync(userId, spotRow.Exchange, spotRow.Tradingsymbol, ct)
                .ConfigureAwait(false);
            if (spotQuote.LastPrice <= 0)
            {
                return await FinishAsync(
                    userId, dryRun, claimedSessionDate, false, $"{underlying.Label} spot LTP unavailable.",
                    underlying.Key, side, maxLots, null, ct).ConfigureAwait(false);
            }

            // Overlap F&O master search with margins while spot LTP is known.
            var optionSearchTask = _broker.SearchKiteInstrumentsAsync(
                userId, underlying.OptionSearchQuery, KiteInstrumentSearchSegment.Fno, ct);
            var marginsTask = _broker.GetKiteUserMarginsAsync(userId, ct);
            await Task.WhenAll(optionSearchTask, marginsTask).ConfigureAwait(false);

            var optionSearch = await optionSearchTask.ConfigureAwait(false);
            var optionRows = NiftyOpenAutoTradeAtm.FilterOptionsForUnderlying(optionSearch.Items, underlying);
            var expiry = NiftyOpenAutoTradeAtm.ResolveExpiryUtc(
                optionRows,
                user.NiftyOpenAutoTradeExpiry,
                DateTimeOffset.UtcNow);
            if (expiry is null)
            {
                return await FinishAsync(
                    userId, dryRun, claimedSessionDate, false, $"No {underlying.Label} option expiry found.",
                    underlying.Key, side, maxLots, spotQuote.LastPrice, ct).ConfigureAwait(false);
            }

            var candidates = NiftyOpenAutoTradeAtm.BuildStrikeCandidates(
                optionRows,
                expiry.Value,
                spotQuote.LastPrice,
                side,
                opts.MaxStrikeStepsAwayFromAtm);
            if (candidates.Count == 0)
            {
                return await FinishAsync(
                    userId, dryRun, claimedSessionDate, false, $"No {side} contracts near ATM.",
                    underlying.Key, side, maxLots, spotQuote.LastPrice, ct).ConfigureAwait(false);
            }

            var margins = await marginsTask.ConfigureAwait(false);
            var available = ResolveAvailableCash(margins);
            if (available <= 0)
            {
                return await FinishAsync(
                    userId, dryRun, claimedSessionDate, false, "Zerodha available cash is zero or unavailable.",
                    underlying.Key, side, maxLots, spotQuote.LastPrice, ct, availableBalance: available).ConfigureAwait(false);
            }

            NiftyOpenAutoTradeAtm.OptionCandidate? chosen = null;
            decimal chosenLtp = 0;
            var chosenLots = 0;

            // Batch LTP for near-ATM candidates (one quote call instead of up to 7 sequential).
            var quoteKeys = candidates
                .Select(c => $"{c.Exchange.Trim().ToUpperInvariant()}:{c.Tradingsymbol.Trim()}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var ltpByKey = await _broker.GetKiteLastPricesAsync(userId, quoteKeys, ct).ConfigureAwait(false);

            foreach (var candidate in candidates)
            {
                var key = $"{candidate.Exchange.Trim().ToUpperInvariant()}:{candidate.Tradingsymbol.Trim()}";
                if (!ltpByKey.TryGetValue(key, out var ltp) || ltp <= 0)
                    continue;

                var lots = NiftyOpenAutoTradeAtm.ComputeAffordableLots(
                    available,
                    ltp,
                    candidate.LotSize,
                    maxLots,
                    opts.BalanceUtilizationFraction);
                if (lots < 1)
                    continue;

                chosen = candidate;
                chosenLtp = ltp;
                chosenLots = lots;
                break;
            }

            if (chosen is null || chosenLots < 1)
            {
                return await FinishAsync(
                    userId,
                    dryRun,
                    claimedSessionDate,
                    false,
                    "Insufficient Zerodha balance for 1 near-ATM lot.",
                    underlying.Key,
                    side,
                    maxLots,
                    spotQuote.LastPrice,
                    ct,
                    availableBalance: available).ConfigureAwait(false);
            }

            var qty = checked(chosenLots * chosen.LotSize);
            var premium = chosenLtp * qty;
            var seedStop = stopLossEnabled
                ? NiftyOpenAutoTradeTrail.InitialStopPriceFromPercent(chosenLtp, effectiveStopPercent, 0.05m)
                : (decimal?)null;
            var seedTarget = targetEnabled
                ? NiftyOpenAutoTradeTrail.InitialTargetPriceFromPercent(chosenLtp, targetPercent, 0.05m)
                : (decimal?)null;
            var preview = new NiftyOpenAutoTradePreviewDto(
                CanTrade: true,
                Reason: null,
                SpotLtp: spotQuote.LastPrice,
                AvailableBalanceInr: available,
                Underlying: underlying.Key,
                OptionSide: side,
                Exchange: chosen.Exchange,
                Tradingsymbol: chosen.Tradingsymbol,
                Strike: chosen.Strike,
                Expiry: chosen.Expiry,
                Lots: chosenLots,
                Quantity: qty,
                OptionLtp: chosenLtp,
                EstimatedPremiumInr: premium,
                MaxLots: maxLots,
                StopLossPrice: seedStop,
                TargetPrice: seedTarget,
                TrailEnabled: trailEnabled,
                TrailPoints: trailEnabled ? trailPercent : null);

            if (dryRun)
                return preview;

            string? orderId = null;
            string? gttId = null;
            string? targetGttId = null;
            decimal? seedTargetPrice = seedTarget;
            string message;
            var runStatus = "failed";
            var product = string.IsNullOrWhiteSpace(opts.Product) ? "MIS" : opts.Product.Trim().ToUpperInvariant();
            var armTrail = false;

            try
            {
                var place = await _broker.PlaceKiteOrderAsync(
                    userId,
                    new KiteOrderPlaceRequestDto
                    {
                        Variety = "regular",
                        Exchange = chosen.Exchange,
                        Tradingsymbol = chosen.Tradingsymbol,
                        TransactionType = "BUY",
                        Quantity = qty,
                        Product = product,
                        OrderType = "MARKET",
                        Validity = "DAY",
                        Tag = opts.OrderTag,
                    },
                    ct).ConfigureAwait(false);
                orderId = place.OrderId;

                try
                {
                    if (trailEnabled && stopLossEnabled)
                    {
                        // Single-leg SL so trail can PUT /gtt/triggers/{id} (OCO cannot be trailed via single-stop modify).
                        var slGtt = await _broker.CreateKiteGttOcoAsync(
                            userId,
                            new KiteGttCreateRequestDto
                            {
                                Exchange = chosen.Exchange,
                                Tradingsymbol = chosen.Tradingsymbol,
                                EntryTransactionType = "BUY",
                                Quantity = qty,
                                Product = product,
                                ReferencePrice = chosenLtp,
                                LastPrice = chosenLtp,
                                StopLossPercent = stopLossPercent,
                                StopLossEnabled = true,
                                ProfitEnabled = false,
                                TickSize = chosen.TickSize,
                                Tag = opts.OrderTag,
                            },
                            ct).ConfigureAwait(false);
                        gttId = slGtt.TriggerId;

                        string tpPart = "+ve off";
                        if (targetEnabled)
                        {
                            var tpGtt = await _broker.CreateKiteGttOcoAsync(
                                userId,
                                new KiteGttCreateRequestDto
                                {
                                    Exchange = chosen.Exchange,
                                    Tradingsymbol = chosen.Tradingsymbol,
                                    EntryTransactionType = "BUY",
                                    Quantity = qty,
                                    Product = product,
                                    ReferencePrice = chosenLtp,
                                    LastPrice = chosenLtp,
                                    TriggerPercent = targetPercent,
                                    StopLossEnabled = false,
                                    ProfitEnabled = true,
                                    TickSize = chosen.TickSize,
                                    Tag = opts.OrderTag,
                                },
                                ct).ConfigureAwait(false);
                            targetGttId = tpGtt.TriggerId;
                            seedTargetPrice = tpGtt.TargetPrice > 0 ? tpGtt.TargetPrice : seedTarget;
                            tpPart =
                                $"+ve {tpGtt.TargetPrice:0.##} (+{targetPercent:0.##}%; approach trail +10% / SL −5%)";
                        }

                        message =
                            $"Bought {chosenLots} lot(s) {chosen.Tradingsymbol} (max affordable ≤{maxLots}); " +
                            $"trail SL {slGtt.StopLossPrice:0.##} (−{trailPercent:0.##}% of peak); {tpPart}.";
                        armTrail = !string.IsNullOrWhiteSpace(gttId) && slGtt.StopLossPrice > 0;
                        seedStop = slGtt.StopLossPrice > 0 ? slGtt.StopLossPrice : seedStop;
                        runStatus = "success";
                    }
                    else
                    {
                        // Fixed −ve/+ve: one Kite OCO (two-leg) when both on.
                        var gtt = await _broker.CreateKiteGttOcoAsync(
                            userId,
                            new KiteGttCreateRequestDto
                            {
                                Exchange = chosen.Exchange,
                                Tradingsymbol = chosen.Tradingsymbol,
                                EntryTransactionType = "BUY",
                                Quantity = qty,
                                Product = product,
                                ReferencePrice = chosenLtp,
                                LastPrice = chosenLtp,
                                StopLossPercent = stopLossPercent,
                                TriggerPercent = targetPercent,
                                StopLossEnabled = stopLossEnabled,
                                ProfitEnabled = targetEnabled,
                                TickSize = chosen.TickSize,
                                Tag = opts.OrderTag,
                            },
                            ct).ConfigureAwait(false);
                        gttId = gtt.TriggerId;
                        var slPart = stopLossEnabled
                            ? $"−ve {gtt.StopLossPrice:0.##} (−{stopLossPercent:0.##}%)"
                            : "−ve off";
                        var tpPart = targetEnabled
                            ? $"+ve {gtt.TargetPrice:0.##} (+{targetPercent:0.##}%)"
                            : "+ve off";
                        var gttKind = stopLossEnabled && targetEnabled ? "Kite OCO GTT" : "Kite GTT";
                        message =
                            $"Bought {chosenLots} lot(s) {chosen.Tradingsymbol} (max affordable ≤{maxLots}); {gttKind} {slPart}; {tpPart}.";
                        runStatus = "success";
                    }
                }
                catch (Exception gttEx)
                {
                    _logger.LogWarning(gttEx, "Opening ATM order placed but GTT failed for {UserId}", userId);
                    message = $"Order placed ({orderId}) but GTT failed: {gttEx.Message}";
                    runStatus = "success";
                    armTrail = false;
                    targetGttId = null;
                    seedTargetPrice = null;
                }
            }
            catch (Exception placeEx)
            {
                message = placeEx.Message;
                runStatus = "failed";
                armTrail = false;
            }

            var persisted = await PersistRunAsync(
                userId,
                claimedSessionDate ?? NiftyOpenAutoTradeSchedule.GetSessionDateIst(
                    DateTimeOffset.UtcNow,
                    NiftyOpenAutoTradeSchedule.ResolveTimeZone(opts.TimeZoneId)),
                runStatus,
                side,
                chosen,
                chosenLots,
                qty,
                chosenLtp,
                spotQuote.LastPrice,
                available,
                orderId,
                gttId,
                trailActive: armTrail,
                trailPeak: armTrail ? chosenLtp : null,
                trailStop: armTrail ? seedStop : null,
                trailPoints: armTrail ? trailPercent : null,
                message,
                ct,
                targetGttId: armTrail ? targetGttId : null,
                trailTarget: armTrail && !string.IsNullOrWhiteSpace(targetGttId) ? seedTargetPrice : null).ConfigureAwait(false);

            if (armTrail
                && persisted is not null
                && uint.TryParse(chosen.InstrumentToken.Trim(), out var tok)
                && tok != 0)
            {
                _trailLive.Register(persisted.Id, userId, tok);
            }

            return preview with { CanTrade = runStatus == "success", Reason = runStatus == "success" ? null : message };
        }
        catch (Exception ex)
        {
            if (!dryRun && claimedSessionDate is not null)
            {
                await PersistRunAsync(
                    userId,
                    claimedSessionDate.Value,
                    "failed",
                    side,
                    null,
                    0,
                    0,
                    null,
                    null,
                    null,
                    null,
                    null,
                    false,
                    null,
                    null,
                    null,
                    ex.Message,
                    ct).ConfigureAwait(false);
            }

            if (dryRun)
            {
                return new NiftyOpenAutoTradePreviewDto(
                    false,
                    ex.Message,
                    null,
                    null,
                    underlying.Key,
                    side,
                    null,
                    null,
                    null,
                    null,
                    0,
                    0,
                    null,
                    0,
                    maxLots,
                    null,
                    null,
                    false,
                    null);
            }

            throw;
        }
    }

    private async Task TrailOneRunAsync(
        NiftyOpenAutoTradeRun run,
        NiftyOpenAutoTradeOptions opts,
        decimal? liveLtp,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(run.Exchange)
            || string.IsNullOrWhiteSpace(run.Tradingsymbol)
            || string.IsNullOrWhiteSpace(run.GttTriggerId)
            || run.Quantity < 1
            || run.TrailStopPrice is null or <= 0
            || run.TrailPeakPrice is null or <= 0)
        {
            run.TrailActive = false;
            await _runs.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        // Live ticks skip REST position/quote calls for latency; poll cycle clears flats.
        if (liveLtp is null or <= 0)
        {
            var positions = await _broker.GetKiteNetPositionsAsync(run.UserId, ct).ConfigureAwait(false);
            var openQty = positions
                .Where(p =>
                    string.Equals(p.Exchange, run.Exchange, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(p.Tradingsymbol, run.Tradingsymbol, StringComparison.OrdinalIgnoreCase)
                    && (string.IsNullOrWhiteSpace(opts.Product)
                        || string.Equals(p.Product, opts.Product, StringComparison.OrdinalIgnoreCase)))
                .Sum(p => p.Quantity);

            if (openQty <= 0)
            {
                run.TrailActive = false;
                run.Message = Truncate(
                    AppendNote(run.Message, "Trail stopped: position flat."),
                    1000);
                await _runs.SaveChangesAsync(ct).ConfigureAwait(false);
                return;
            }
        }

        decimal ltp;
        if (liveLtp is > 0)
        {
            ltp = liveLtp.Value;
        }
        else
        {
            var quote = await _broker
                .GetKiteInstrumentLiveQuoteAsync(run.UserId, run.Exchange, run.Tradingsymbol, ct)
                .ConfigureAwait(false);
            if (quote.LastPrice <= 0)
                return;
            ltp = quote.LastPrice;
        }

        var trailPercent = NiftyOpenAutoTradeTrail.ClampGttPercent(
            run.TrailPoints ?? opts.DefaultStopLossPoints);
        // Tick resolved inside Modify; seed with 0.05 for local compare then re-round on broker.
        var tickHint = 0.05m;
        var product = string.IsNullOrWhiteSpace(opts.Product) ? "MIS" : opts.Product.Trim().ToUpperInvariant();
        var notes = new List<string>();

        // 1) When LTP is about to hit +ve GTT: raise target to LTP+10% and SL to LTP−5%.
        if (!string.IsNullOrWhiteSpace(run.TargetGttTriggerId)
            && run.TrailTargetPrice is > 0)
        {
            var (bumpTarget, bumpStop) = NiftyOpenAutoTradeTrail.ComputeTargetApproachBump(
                ltp,
                run.TrailTargetPrice.Value,
                run.TrailStopPrice.Value,
                tickHint);

            if (bumpTarget is > 0)
            {
                var modifiedTp = await _broker.ModifyKiteGttSingleTargetAsync(
                    run.UserId,
                    run.TargetGttTriggerId!,
                    new KiteGttCreateRequestDto
                    {
                        Exchange = run.Exchange,
                        Tradingsymbol = run.Tradingsymbol,
                        EntryTransactionType = "BUY",
                        Quantity = run.Quantity,
                        Product = product,
                        LastPrice = ltp,
                        TriggerPrice = bumpTarget.Value,
                        StopLossEnabled = false,
                        ProfitEnabled = true,
                        Tag = opts.OrderTag,
                    },
                    ct).ConfigureAwait(false);

                run.TrailTargetPrice = modifiedTp.TargetPrice > 0 ? modifiedTp.TargetPrice : bumpTarget.Value;
                if (!string.IsNullOrWhiteSpace(modifiedTp.TriggerId))
                    run.TargetGttTriggerId = modifiedTp.TriggerId;
                notes.Add(
                    $"+ve approach → target {run.TrailTargetPrice:0.##} (LTP {ltp:0.##} +{NiftyOpenAutoTradeTrail.DefaultTargetApproachBumpPercent:0.##}%)");
            }

            if (bumpStop is > 0)
            {
                var modifiedSl = await _broker.ModifyKiteGttSingleStopAsync(
                    run.UserId,
                    run.GttTriggerId,
                    new KiteGttCreateRequestDto
                    {
                        Exchange = run.Exchange,
                        Tradingsymbol = run.Tradingsymbol,
                        EntryTransactionType = "BUY",
                        Quantity = run.Quantity,
                        Product = product,
                        LastPrice = ltp,
                        StopLossPrice = bumpStop.Value,
                        StopLossEnabled = true,
                        ProfitEnabled = false,
                        Tag = opts.OrderTag,
                    },
                    ct).ConfigureAwait(false);

                run.TrailStopPrice = modifiedSl.StopLossPrice > 0 ? modifiedSl.StopLossPrice : bumpStop.Value;
                if (!string.IsNullOrWhiteSpace(modifiedSl.TriggerId))
                    run.GttTriggerId = modifiedSl.TriggerId;
                // Keep peak at least at LTP so classic trail does not fight the approach bump.
                if (ltp > run.TrailPeakPrice.Value)
                    run.TrailPeakPrice = ltp;
                notes.Add(
                    $"SL → {run.TrailStopPrice:0.##} (LTP −{NiftyOpenAutoTradeTrail.DefaultTargetApproachStopPercent:0.##}%)");
            }
        }

        var peakBefore = run.TrailPeakPrice.Value;
        var stopBefore = run.TrailStopPrice.Value;

        // 2) Classic peak trail: keep SL at trail% below peak (ratchet up only).
        var (newPeak, newStop) = NiftyOpenAutoTradeTrail.ComputeTrailUpdateFromPercent(
            run.TrailPeakPrice.Value,
            run.TrailStopPrice.Value,
            ltp,
            trailPercent,
            tickHint);

        run.TrailPeakPrice = newPeak;
        if (newStop is > 0)
        {
            var modified = await _broker.ModifyKiteGttSingleStopAsync(
                run.UserId,
                run.GttTriggerId,
                new KiteGttCreateRequestDto
                {
                    Exchange = run.Exchange,
                    Tradingsymbol = run.Tradingsymbol,
                    EntryTransactionType = "BUY",
                    Quantity = run.Quantity,
                    Product = product,
                    LastPrice = ltp,
                    StopLossPrice = newStop.Value,
                    StopLossEnabled = true,
                    ProfitEnabled = false,
                    Tag = opts.OrderTag,
                },
                ct).ConfigureAwait(false);

            run.TrailStopPrice = modified.StopLossPrice > 0 ? modified.StopLossPrice : newStop.Value;
            if (!string.IsNullOrWhiteSpace(modified.TriggerId))
                run.GttTriggerId = modified.TriggerId;
            notes.Add($"Trail SL → {run.TrailStopPrice:0.##} (peak {newPeak:0.##}, −{trailPercent:0.##}%)");
        }

        if (notes.Count > 0)
            run.Message = Truncate(AppendNote(run.Message, string.Join("; ", notes)), 1000);

        if (notes.Count > 0
            || run.TrailPeakPrice != peakBefore
            || run.TrailStopPrice != stopBefore
            || liveLtp is null)
        {
            await _runs.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task<NiftyOpenAutoTradePreviewDto> FinishAsync(
        Guid userId,
        bool dryRun,
        DateOnly? claimedSessionDate,
        bool CanTrade,
        string Reason,
        string underlyingKey,
        string side,
        int maxLots,
        decimal? spotLtp,
        CancellationToken ct,
        decimal? availableBalance = null)
    {
        var dto = new NiftyOpenAutoTradePreviewDto(
            CanTrade,
            Reason,
            spotLtp,
            availableBalance,
            underlyingKey,
            side,
            null,
            null,
            null,
            null,
            0,
            0,
            null,
            0,
            maxLots,
            null,
            null,
            false,
            null);

        if (!dryRun && claimedSessionDate is not null)
        {
            await PersistRunAsync(
                userId,
                claimedSessionDate.Value,
                CanTrade ? "success" : "skipped",
                side,
                null,
                0,
                0,
                null,
                spotLtp,
                availableBalance,
                null,
                null,
                false,
                null,
                null,
                null,
                Reason,
                ct).ConfigureAwait(false);
        }

        return dto;
    }

    private async Task<NiftyOpenAutoTradeRun> PersistRunAsync(
        Guid userId,
        DateOnly sessionDate,
        string status,
        string side,
        NiftyOpenAutoTradeAtm.OptionCandidate? chosen,
        int lots,
        int quantity,
        decimal? optionLtp,
        decimal? spotLtp,
        decimal? available,
        string? orderId,
        string? gttId,
        bool trailActive,
        decimal? trailPeak,
        decimal? trailStop,
        decimal? trailPoints,
        string? message,
        CancellationToken ct,
        string? targetGttId = null,
        decimal? trailTarget = null)
    {
        var run = new NiftyOpenAutoTradeRun
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SessionDateIst = sessionDate,
            Status = status,
            OptionSide = side,
            Exchange = chosen?.Exchange,
            Tradingsymbol = chosen?.Tradingsymbol,
            InstrumentToken = chosen?.InstrumentToken,
            Strike = chosen?.Strike,
            Expiry = chosen?.Expiry,
            Lots = lots,
            Quantity = quantity,
            OptionLtp = optionLtp,
            SpotLtp = spotLtp,
            AvailableBalanceInr = available,
            OrderId = orderId,
            GttTriggerId = gttId,
            TargetGttTriggerId = targetGttId,
            TrailActive = trailActive,
            TrailPeakPrice = trailPeak,
            TrailStopPrice = trailStop,
            TrailTargetPrice = trailTarget,
            TrailPoints = trailPoints,
            Message = Truncate(message, 1000),
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        await _runs.AddAsync(run, ct).ConfigureAwait(false);
        await _runs.SaveChangesAsync(ct).ConfigureAwait(false);
        return run;
    }

    private static decimal ResolveAvailableCash(KiteUserMarginsDto margins)
    {
        var eq = margins.Equity;
        if (eq is null || !eq.Enabled)
            return 0m;
        if (eq.AvailableCash > 0)
            return eq.AvailableCash;
        if (eq.LiveBalance > 0)
            return eq.LiveBalance;
        return eq.Net;
    }

    private async Task<User> RequireUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(userId, ct).ConfigureAwait(false);
        if (user is null)
            throw new InvalidOperationException("Account not found.");
        return user;
    }

    private async Task<IReadOnlyList<string>> TryListAvailableExpiriesAsync(User user, CancellationToken ct)
    {
        try
        {
            var underlying = OpeningAtmUnderlyings.Resolve(user.NiftyOpenAutoTradeUnderlying);
            var brokerStatus = await _broker.GetStatusAsync(user.Id, ct).ConfigureAwait(false);
            if (!brokerStatus.Connected
                || !string.Equals(brokerStatus.Provider, "zerodha", StringComparison.OrdinalIgnoreCase))
            {
                return Array.Empty<string>();
            }

            var optionSearch = await _broker
                .SearchKiteInstrumentsAsync(user.Id, underlying.OptionSearchQuery, KiteInstrumentSearchSegment.Fno, ct)
                .ConfigureAwait(false);
            return NiftyOpenAutoTradeAtm.ListDistinctExpiryDates(
                NiftyOpenAutoTradeAtm.FilterOptionsForUnderlying(optionSearch.Items, underlying));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not list Opening ATM expiries for {UserId}", user.Id);
            return Array.Empty<string>();
        }
    }

    private static DateOnly? ParsePreferredExpiry(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var s = raw.Trim();
        if (DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;
        if (DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return date;
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
            return DateOnly.FromDateTime(dto.UtcDateTime);

        throw new InvalidOperationException("expiry must be yyyy-MM-dd or empty for nearest future.");
    }

    private static NiftyOpenAutoTradeSettingsDto MapSettings(
        User user,
        NiftyOpenAutoTradeRun? last,
        IReadOnlyList<string> availableExpiries)
    {
        var underlying = OpeningAtmUnderlyings.Resolve(user.NiftyOpenAutoTradeUnderlying);
        var availableUnderlyings = OpeningAtmUnderlyings.All
            .Select(x => new OpeningAtmUnderlyingDto(x.Key, x.Label))
            .ToList();
        return new(
            user.NiftyOpenAutoTradeEnabled,
            underlying.Key,
            NiftyOpenAutoTradeOptionSideParser.FromEnum(user.NiftyOpenAutoTradeOptionSide),
            user.NiftyOpenAutoTradeMaxLots > 0 ? user.NiftyOpenAutoTradeMaxLots : 10,
            user.NiftyOpenAutoTradeExpiry?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            user.NiftyOpenAutoTradeStopLossPoints > 0 ? user.NiftyOpenAutoTradeStopLossPoints : 5m,
            user.NiftyOpenAutoTradeTargetPoints > 0 ? user.NiftyOpenAutoTradeTargetPoints : 5m,
            user.NiftyOpenAutoTradeStopLossEnabled,
            user.NiftyOpenAutoTradeTargetEnabled,
            user.NiftyOpenAutoTradeTrailEnabled,
            user.NiftyOpenAutoTradeTrailPoints > 0 ? user.NiftyOpenAutoTradeTrailPoints : 5m,
            user.NiftyOpenAutoTradeLastSessionDateIst,
            availableExpiries,
            availableUnderlyings,
            last is null ? null : MapRun(last));
    }

    private static NiftyOpenAutoTradeRunDto MapRun(NiftyOpenAutoTradeRun r) =>
        new(
            r.Id,
            r.SessionDateIst,
            r.Status,
            r.OptionSide,
            r.Exchange,
            r.Tradingsymbol,
            r.Strike,
            r.Expiry,
            r.Lots,
            r.Quantity,
            r.OptionLtp,
            r.SpotLtp,
            r.AvailableBalanceInr,
            r.OrderId,
            r.GttTriggerId,
            r.Message,
            r.CreatedAtUtc);

    private static string AppendNote(string? existing, string note)
    {
        if (string.IsNullOrWhiteSpace(existing))
            return note;
        return $"{existing} | {note}";
    }

    private static string? Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s;
        return s[..max];
    }
}
