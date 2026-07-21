using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Application.Abstractions.Persistence;
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
    private readonly ILogger<NiftyOpenAutoTradeService> _logger;

    public NiftyOpenAutoTradeService(
        IOptionsMonitor<NiftyOpenAutoTradeOptions> options,
        IBrokerService broker,
        IUserRepository users,
        INiftyOpenAutoTradeRunRepository runs,
        ILogger<NiftyOpenAutoTradeService> logger)
    {
        _options = options;
        _broker = broker;
        _users = users;
        _runs = runs;
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
                    continue;
                }

                await TrailOneRunAsync(run, opts, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "NIFTY open trail failed for run {RunId} user {UserId}", run.Id, run.UserId);
            }
        }
    }

    public async Task<NiftyOpenAutoTradeSettingsDto> GetSettingsAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await RequireUserAsync(userId, ct).ConfigureAwait(false);
        var last = await _runs.GetLatestByUserAsync(userId, ct).ConfigureAwait(false);
        var available = await TryListAvailableExpiriesAsync(userId, ct).ConfigureAwait(false);
        return MapSettings(user, last, available);
    }

    public async Task SaveSettingsAsync(Guid userId, NiftyOpenAutoTradeSettingsPutDto body, CancellationToken ct = default)
    {
        if (body is null)
            throw new InvalidOperationException("Request body is required.");

        var opts = _options.CurrentValue;
        var user = await RequireUserAsync(userId, ct).ConfigureAwait(false);
        user.NiftyOpenAutoTradeEnabled = body.Enabled;
        user.NiftyOpenAutoTradeOptionSide = NiftyOpenAutoTradeOptionSideParser.ToEnum(body.OptionSide);
        var maxLots = body.MaxLots > 0 ? body.MaxLots : opts.DefaultMaxLots;
        user.NiftyOpenAutoTradeMaxLots = Math.Clamp(maxLots, 1, Math.Max(1, opts.AbsoluteMaxLots));
        user.NiftyOpenAutoTradeExpiry = ParsePreferredExpiry(body.Expiry);
        user.NiftyOpenAutoTradeStopLossPoints = NiftyOpenAutoTradeTrail.ClampGttPoints(
            body.StopLossPoints > 0 ? body.StopLossPoints : opts.DefaultStopLossPoints);
        user.NiftyOpenAutoTradeTargetPoints = NiftyOpenAutoTradeTrail.ClampGttPoints(
            body.TargetPoints > 0 ? body.TargetPoints : opts.DefaultTargetPoints);
        user.NiftyOpenAutoTradeStopLossEnabled = body.StopLossEnabled;
        user.NiftyOpenAutoTradeTargetEnabled = body.TargetEnabled;
        if (!user.NiftyOpenAutoTradeStopLossEnabled && !user.NiftyOpenAutoTradeTargetEnabled)
            throw new InvalidOperationException("Enable at least one of −ve GTT (stop-loss) or +ve GTT (target).");
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
        var side = NiftyOpenAutoTradeOptionSideParser.FromEnum(user.NiftyOpenAutoTradeOptionSide);
        var maxLots = Math.Clamp(
            user.NiftyOpenAutoTradeMaxLots > 0 ? user.NiftyOpenAutoTradeMaxLots : opts.DefaultMaxLots,
            1,
            Math.Max(1, opts.AbsoluteMaxLots));
        var trailPoints = NiftyOpenAutoTradeTrail.ClampGttPoints(
            user.NiftyOpenAutoTradeStopLossPoints > 0
                ? user.NiftyOpenAutoTradeStopLossPoints
                : opts.DefaultStopLossPoints);
        var targetPoints = NiftyOpenAutoTradeTrail.ClampGttPoints(
            user.NiftyOpenAutoTradeTargetPoints > 0
                ? user.NiftyOpenAutoTradeTargetPoints
                : opts.DefaultTargetPoints);
        var stopLossEnabled = user.NiftyOpenAutoTradeStopLossEnabled;
        var targetEnabled = user.NiftyOpenAutoTradeTargetEnabled;
        if (!stopLossEnabled && !targetEnabled)
        {
            return await FinishAsync(
                userId,
                dryRun,
                claimedSessionDate,
                false,
                "Configure at least one of −ve GTT or +ve GTT on NIFTY Opening ATM.",
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
                    Reason: "Connect Zerodha to enable NIFTY open auto-trade.",
                    side,
                    maxLots,
                    null,
                    ct).ConfigureAwait(false);
            }

            var spotSearch = await _broker
                .SearchKiteInstrumentsAsync(userId, opts.SpotSearchQuery, KiteInstrumentSearchSegment.Spot, ct)
                .ConfigureAwait(false);
            var spotRow = NiftyOpenAutoTradeAtm.ChooseNiftySpotRow(spotSearch.Items, opts.PreferredSpotExchange);
            if (spotRow is null)
            {
                return await FinishAsync(
                    userId, dryRun, claimedSessionDate, false, "Could not resolve NIFTY 50 spot instrument.",
                    side, maxLots, null, ct).ConfigureAwait(false);
            }

            var spotQuote = await _broker
                .GetKiteInstrumentLiveQuoteAsync(userId, spotRow.Exchange, spotRow.Tradingsymbol, ct)
                .ConfigureAwait(false);
            if (spotQuote.LastPrice <= 0)
            {
                return await FinishAsync(
                    userId, dryRun, claimedSessionDate, false, "NIFTY spot LTP unavailable.",
                    side, maxLots, null, ct).ConfigureAwait(false);
            }

            var optionSearch = await _broker
                .SearchKiteInstrumentsAsync(userId, opts.OptionSearchQuery, KiteInstrumentSearchSegment.Fno, ct)
                .ConfigureAwait(false);
            var niftyOptions = NiftyOpenAutoTradeAtm.FilterNiftyOptions(optionSearch.Items);
            var expiry = NiftyOpenAutoTradeAtm.ResolveExpiryUtc(
                niftyOptions,
                user.NiftyOpenAutoTradeExpiry,
                DateTimeOffset.UtcNow);
            if (expiry is null)
            {
                return await FinishAsync(
                    userId, dryRun, claimedSessionDate, false, "No NIFTY option expiry found.",
                    side, maxLots, spotQuote.LastPrice, ct).ConfigureAwait(false);
            }

            var candidates = NiftyOpenAutoTradeAtm.BuildStrikeCandidates(
                niftyOptions,
                expiry.Value,
                spotQuote.LastPrice,
                side,
                opts.MaxStrikeStepsAwayFromAtm);
            if (candidates.Count == 0)
            {
                return await FinishAsync(
                    userId, dryRun, claimedSessionDate, false, $"No {side} contracts near ATM.",
                    side, maxLots, spotQuote.LastPrice, ct).ConfigureAwait(false);
            }

            var margins = await _broker.GetKiteUserMarginsAsync(userId, ct).ConfigureAwait(false);
            var available = ResolveAvailableCash(margins);
            if (available <= 0)
            {
                return await FinishAsync(
                    userId, dryRun, claimedSessionDate, false, "Zerodha available cash is zero or unavailable.",
                    side, maxLots, spotQuote.LastPrice, ct, availableBalance: available).ConfigureAwait(false);
            }

            NiftyOpenAutoTradeAtm.OptionCandidate? chosen = null;
            decimal chosenLtp = 0;
            var chosenLots = 0;

            foreach (var candidate in candidates)
            {
                var quote = await _broker
                    .GetKiteInstrumentLiveQuoteAsync(userId, candidate.Exchange, candidate.Tradingsymbol, ct)
                    .ConfigureAwait(false);
                if (quote.LastPrice <= 0)
                    continue;

                var lots = NiftyOpenAutoTradeAtm.ComputeAffordableLots(
                    available,
                    quote.LastPrice,
                    candidate.LotSize,
                    maxLots,
                    opts.BalanceUtilizationFraction);
                if (lots < 1)
                    continue;

                chosen = candidate;
                chosenLtp = quote.LastPrice;
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
                    side,
                    maxLots,
                    spotQuote.LastPrice,
                    ct,
                    availableBalance: available).ConfigureAwait(false);
            }

            var qty = checked(chosenLots * chosen.LotSize);
            var premium = chosenLtp * qty;
            var seedStop = stopLossEnabled
                ? NiftyOpenAutoTradeTrail.InitialStopPrice(chosenLtp, trailPoints, 0.05m)
                : (decimal?)null;
            var seedTarget = targetEnabled
                ? NiftyOpenAutoTradeTrail.InitialTargetPrice(chosenLtp, targetPoints, 0.05m)
                : (decimal?)null;
            var preview = new NiftyOpenAutoTradePreviewDto(
                CanTrade: true,
                Reason: null,
                SpotLtp: spotQuote.LastPrice,
                AvailableBalanceInr: available,
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
                TargetPrice: seedTarget);

            if (dryRun)
                return preview;

            string? orderId = null;
            string? gttId = null;
            string message;
            var runStatus = "failed";
            var product = string.IsNullOrWhiteSpace(opts.Product) ? "MIS" : opts.Product.Trim().ToUpperInvariant();

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
                            StopLossPrice = seedStop,
                            TriggerPrice = seedTarget,
                            StopLossEnabled = stopLossEnabled,
                            ProfitEnabled = targetEnabled,
                            Tag = opts.OrderTag,
                        },
                        ct).ConfigureAwait(false);
                    gttId = gtt.TriggerId;
                    var slPart = stopLossEnabled
                        ? $"−ve GTT {gtt.StopLossPrice:0.##} (−{trailPoints:0.##} pts)"
                        : "−ve GTT off";
                    var tpPart = targetEnabled
                        ? $"+ve GTT {gtt.TargetPrice:0.##} (+{targetPoints:0.##} pts)"
                        : "+ve GTT off";
                    message =
                        $"Bought {chosenLots} lot(s) {chosen.Tradingsymbol} (max affordable ≤{maxLots}); {slPart}; {tpPart}.";
                    runStatus = "success";
                }
                catch (Exception gttEx)
                {
                    _logger.LogWarning(gttEx, "NIFTY Opening ATM order placed but GTT failed for {UserId}", userId);
                    message = $"Order placed ({orderId}) but GTT failed: {gttEx.Message}";
                    runStatus = "success";
                }
            }
            catch (Exception placeEx)
            {
                message = placeEx.Message;
                runStatus = "failed";
            }

            await PersistRunAsync(
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
                trailActive: false,
                trailPeak: null,
                trailStop: null,
                trailPoints: null,
                message,
                ct).ConfigureAwait(false);

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
                    null);
            }

            throw;
        }
    }

    private async Task TrailOneRunAsync(
        NiftyOpenAutoTradeRun run,
        NiftyOpenAutoTradeOptions opts,
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

        var quote = await _broker
            .GetKiteInstrumentLiveQuoteAsync(run.UserId, run.Exchange, run.Tradingsymbol, ct)
            .ConfigureAwait(false);
        if (quote.LastPrice <= 0)
            return;

        var trailPoints = NiftyOpenAutoTradeTrail.ClampTrailPoints(
            run.TrailPoints ?? opts.DefaultStopLossPoints);
        // Tick resolved inside Modify; seed with 0.05 for local compare then re-round on broker.
        var tickHint = 0.05m;
        var (newPeak, newStop) = NiftyOpenAutoTradeTrail.ComputeTrailUpdate(
            run.TrailPeakPrice.Value,
            run.TrailStopPrice.Value,
            quote.LastPrice,
            trailPoints,
            tickHint);

        run.TrailPeakPrice = newPeak;
        if (newStop is null)
        {
            await _runs.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        var product = string.IsNullOrWhiteSpace(opts.Product) ? "MIS" : opts.Product.Trim().ToUpperInvariant();
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
                LastPrice = quote.LastPrice,
                StopLossPrice = newStop.Value,
                StopLossEnabled = true,
                ProfitEnabled = false,
                Tag = opts.OrderTag,
            },
            ct).ConfigureAwait(false);

        run.TrailStopPrice = modified.StopLossPrice > 0 ? modified.StopLossPrice : newStop.Value;
        if (!string.IsNullOrWhiteSpace(modified.TriggerId))
            run.GttTriggerId = modified.TriggerId;
        run.Message = Truncate(
            AppendNote(run.Message, $"Trail SL → {run.TrailStopPrice:0.##} (peak {newPeak:0.##})."),
            1000);
        await _runs.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task<NiftyOpenAutoTradePreviewDto> FinishAsync(
        Guid userId,
        bool dryRun,
        DateOnly? claimedSessionDate,
        bool CanTrade,
        string Reason,
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

    private async Task PersistRunAsync(
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
        CancellationToken ct)
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
            Strike = chosen?.Strike,
            Expiry = chosen?.Expiry,
            Lots = lots,
            Quantity = quantity,
            OptionLtp = optionLtp,
            SpotLtp = spotLtp,
            AvailableBalanceInr = available,
            OrderId = orderId,
            GttTriggerId = gttId,
            TrailActive = trailActive,
            TrailPeakPrice = trailPeak,
            TrailStopPrice = trailStop,
            TrailPoints = trailPoints,
            Message = Truncate(message, 1000),
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        await _runs.AddAsync(run, ct).ConfigureAwait(false);
        await _runs.SaveChangesAsync(ct).ConfigureAwait(false);
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

    private async Task<IReadOnlyList<string>> TryListAvailableExpiriesAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            var opts = _options.CurrentValue;
            var brokerStatus = await _broker.GetStatusAsync(userId, ct).ConfigureAwait(false);
            if (!brokerStatus.Connected
                || !string.Equals(brokerStatus.Provider, "zerodha", StringComparison.OrdinalIgnoreCase))
            {
                return Array.Empty<string>();
            }

            var optionSearch = await _broker
                .SearchKiteInstrumentsAsync(userId, opts.OptionSearchQuery, KiteInstrumentSearchSegment.Fno, ct)
                .ConfigureAwait(false);
            return NiftyOpenAutoTradeAtm.ListDistinctExpiryDates(
                NiftyOpenAutoTradeAtm.FilterNiftyOptions(optionSearch.Items));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not list NIFTY open-auto expiries for {UserId}", userId);
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
        IReadOnlyList<string> availableExpiries) =>
        new(
            user.NiftyOpenAutoTradeEnabled,
            NiftyOpenAutoTradeOptionSideParser.FromEnum(user.NiftyOpenAutoTradeOptionSide),
            user.NiftyOpenAutoTradeMaxLots > 0 ? user.NiftyOpenAutoTradeMaxLots : 10,
            user.NiftyOpenAutoTradeExpiry?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            user.NiftyOpenAutoTradeStopLossPoints > 0 ? user.NiftyOpenAutoTradeStopLossPoints : 5m,
            user.NiftyOpenAutoTradeTargetPoints > 0 ? user.NiftyOpenAutoTradeTargetPoints : 5m,
            user.NiftyOpenAutoTradeStopLossEnabled,
            user.NiftyOpenAutoTradeTargetEnabled,
            user.NiftyOpenAutoTradeLastSessionDateIst,
            availableExpiries,
            last is null ? null : MapRun(last));

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
