using Trader.Application.Prediction;

namespace Trader.Application.Broker;

/// <summary>
/// Hypothetical end-of-day demo P&amp;L from merged automation rows; allocation rules are illustrative only (no live orders).
/// When a contract lot multiplier map is supplied, allocation is floored to whole contracts and P&amp;L uses
/// <c>(exit−entry)×lotMultiplier×contracts</c> on Kite OHLC closes (long/up vs short/down); fees scale by contracts.
/// </summary>
public static class DemoAutoTradeEodSummaryCalculator
{
    public const decimal DefaultNotionalInr = 10_000m;

    public const int HighConvictionMinConfidence = 65;

    /// <summary>Start of local calendar day (inclusive) and next local midnight (exclusive), as UTC offsets.</summary>
    public static (DateTimeOffset FromUtcInclusive, DateTimeOffset ToUtcExclusive) GetLocalDayBoundsUtc(
        DateOnly date,
        string timeZoneId)
    {
        var tz = ResolveTimeZone(timeZoneId);
        var localMidnight = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(localMidnight, tz);
        var toUtc = TimeZoneInfo.ConvertTimeToUtc(localMidnight.AddDays(1), tz);
        return (fromUtc, toUtc);
    }

    public static DateOnly GetTodayInTimeZone(string timeZoneId)
    {
        var tz = ResolveTimeZone(timeZoneId);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        return DateOnly.FromDateTime(nowLocal);
    }

    /// <summary>Calendar date in <paramref name="timeZoneId"/> for an instant stored as UTC.</summary>
    public static DateOnly GetLocalDateOnly(DateTimeOffset instantUtc, string timeZoneId)
    {
        var tz = ResolveTimeZone(timeZoneId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(instantUtc.UtcDateTime, tz);
        return DateOnly.FromDateTime(local);
    }

    public static string BuildAllocationNote(string strategyId, DemoAutoTradeEodTotals totals)
    {
        var norm = DemoAutoTradeStrategyIds.NormalizeOrDefault(strategyId);
        var (_, _, explain) = DemoAutoTradeStrategyIds.Describe(norm);
        var baseNote =
            "Illustrative only — not advice. When next-bar open is stored, hypothetical returns use next open→next close (market-style entry); " +
            "otherwise ref close→next close. Neutral legs get no allocation. " +
            "No fees, slippage, leverage, position sizing, or real execution. ";

        var legNote =
            $"{totals.AllocatedLegsForPnl} directional leg(s) received hypothetical allocation from {totals.DirectionalTradeableLegs} priced directional signal(s) this day.";
        if (totals.SkippedLowConfidenceLegs > 0)
            legNote += $" {totals.SkippedLowConfidenceLegs} leg(s) below the confidence cutoff were excluded.";
        legNote += " " + explain;
        if (totals.HypotheticalChargesInr > 0m)
        {
            legNote +=
                $" Approximate round-trip fees applied: {totals.HypotheticalChargesInr:0.##} INR " +
                $"(gross {totals.HypotheticalGrossPnlInr:0.##} → net {totals.HypotheticalTotalPnlInr:0.##}).";
        }

        return baseNote + legNote;
    }

    public static DemoAutoTradeEodTotals Compute(
        IReadOnlyList<MlAutomationPredictionListItemDto> rows,
        decimal totalNotionalInr,
        string? strategyCode,
        DemoAutoTradeChargeParameters? charges = null,
        IReadOnlyDictionary<string, int>? lotMultipliersByInstrumentToken = null) =>
        ComputeWithLegRows(rows, totalNotionalInr, strategyCode, charges, lotMultipliersByInstrumentToken).Totals;

    public static (DemoAutoTradeEodTotals Totals, IReadOnlyList<DemoAutoTradeLegRowDto> Legs) ComputeWithLegRows(
        IReadOnlyList<MlAutomationPredictionListItemDto> rows,
        decimal totalNotionalInr,
        string? strategyCode,
        DemoAutoTradeChargeParameters? charges = null,
        IReadOnlyDictionary<string, int>? lotMultipliersByInstrumentToken = null)
    {
        var work = BuildAllocationWork(rows, totalNotionalInr, strategyCode, lotMultipliersByInstrumentToken);
        var legs = BuildLegRows(rows, work, charges, lotMultipliersByInstrumentToken);

        var allocated = legs.Where(l => string.Equals(l.Status, "allocated", StringComparison.Ordinal)).ToList();
        var grossPnl = allocated.Sum(l => l.LegGrossPnlInr);
        var feesInr = allocated.Sum(l => l.LegFeesInr);
        var netPnl = grossPnl - feesInr;

        var totals = new DemoAutoTradeEodTotals(
            TotalSignals: work.TotalSignals,
            PendingSignals: work.PendingSignals,
            CorrectOutcomes: work.CorrectOutcomes,
            WrongOutcomes: work.WrongOutcomes,
            SkippedNoNextClose: work.SkippedNoNextClose,
            DirectionalTradeableLegs: work.DirectionalTradeable.Count,
            AllocatedLegsForPnl: allocated.Count,
            SkippedLowConfidenceLegs: work.SkippedLowConfidenceLegs,
            HypotheticalGrossPnlInr: decimal.Round(grossPnl, 2, MidpointRounding.AwayFromZero),
            HypotheticalChargesInr: decimal.Round(feesInr, 2, MidpointRounding.AwayFromZero),
            HypotheticalTotalPnlInr: decimal.Round(netPnl, 2, MidpointRounding.AwayFromZero));

        return (totals, legs);
    }

    private sealed record AllocationWork(
        string NormalizedStrategy,
        int TotalSignals,
        int PendingSignals,
        int CorrectOutcomes,
        int WrongOutcomes,
        int SkippedNoNextClose,
        List<MlAutomationPredictionListItemDto> DirectionalTradeable,
        List<MlAutomationPredictionListItemDto> LegList,
        Dictionary<Guid, decimal> Allocations,
        int SkippedLowConfidenceLegs);

    private static AllocationWork BuildAllocationWork(
        IReadOnlyList<MlAutomationPredictionListItemDto> rows,
        decimal totalNotionalInr,
        string? strategyCode,
        IReadOnlyDictionary<string, int>? lotMultipliersByInstrumentToken)
    {
        var strategy = DemoAutoTradeStrategyIds.NormalizeOrDefault(strategyCode);
        var useLots = lotMultipliersByInstrumentToken is not null;
        var totalSignals = rows.Count;
        var pending = 0;
        var correct = 0;
        var wrong = 0;
        var skippedNoPrice = 0;

        foreach (var r in rows)
        {
            if (string.Equals(r.Outcome, "pending", StringComparison.OrdinalIgnoreCase))
            {
                pending++;
                continue;
            }

            if (string.Equals(r.Outcome, "correct", StringComparison.OrdinalIgnoreCase))
                correct++;
            else if (string.Equals(r.Outcome, "wrong", StringComparison.OrdinalIgnoreCase))
                wrong++;

            if (r.NextClose is not { } || r.RefClose <= 0m)
                skippedNoPrice++;
        }

        var directionalTradeable = new List<MlAutomationPredictionListItemDto>();
        foreach (var r in rows)
        {
            if (string.Equals(r.Outcome, "pending", StringComparison.OrdinalIgnoreCase))
                continue;

            if (r.NextClose is null || r.RefClose <= 0m)
                continue;

            var dir = (r.Direction ?? string.Empty).Trim();
            if (dir.Equals("neutral", StringComparison.OrdinalIgnoreCase))
                continue;

            if (DirectionalReturnFraction(dir, r.RefClose, r.NextOpen, r.NextClose!.Value) is null)
                continue;

            if (useLots)
            {
                var tok = (r.InstrumentToken ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(tok)
                    || !lotMultipliersByInstrumentToken!.TryGetValue(tok, out var mult)
                    || mult < 1)
                {
                    continue;
                }
            }

            directionalTradeable.Add(r);
        }

        var skippedLowConfidence = 0;
        IEnumerable<MlAutomationPredictionListItemDto> positioned = directionalTradeable;

        if (string.Equals(strategy, DemoAutoTradeStrategyIds.HighConviction, StringComparison.Ordinal))
        {
            var kept = directionalTradeable.Where(r => r.Confidence >= HighConvictionMinConfidence).ToList();
            skippedLowConfidence = directionalTradeable.Count - kept.Count;
            positioned = kept;
        }
        else if (string.Equals(strategy, DemoAutoTradeStrategyIds.OneSignalPerInstrument, StringComparison.Ordinal))
        {
            positioned = directionalTradeable
                .GroupBy(r => r.InstrumentToken.Trim(), StringComparer.Ordinal)
                .Select(g =>
                    g.OrderByDescending(x => x.Confidence)
                        .ThenByDescending(x => x.PredictedAt)
                        .First())
                .ToList();
        }
        else if (string.Equals(strategy, DemoAutoTradeStrategyIds.OneSignalPerEngine, StringComparison.Ordinal))
        {
            positioned = directionalTradeable
                .GroupBy(
                    r => string.IsNullOrWhiteSpace(r.EngineModelId) ? "(unknown)" : r.EngineModelId.Trim(),
                    StringComparer.Ordinal)
                .Select(g =>
                    g.OrderByDescending(x => x.Confidence)
                        .ThenByDescending(x => x.PredictedAt)
                        .First())
                .ToList();
        }
        else if (string.Equals(strategy, DemoAutoTradeStrategyIds.TopHalfConfidence, StringComparison.Ordinal))
        {
            var sorted = directionalTradeable
                .OrderByDescending(r => r.Confidence)
                .ThenByDescending(r => r.PredictedAt)
                .ToList();
            var k = (sorted.Count + 1) / 2;
            positioned = sorted.Take(k).ToList();
        }

        var legList = positioned.ToList();

        if (string.Equals(strategy, DemoAutoTradeStrategyIds.OneSignalPerInstrument, StringComparison.Ordinal)
            || string.Equals(strategy, DemoAutoTradeStrategyIds.OneSignalPerEngine, StringComparison.Ordinal)
            || string.Equals(strategy, DemoAutoTradeStrategyIds.TopHalfConfidence, StringComparison.Ordinal))
        {
            skippedLowConfidence = Math.Max(0, directionalTradeable.Count - legList.Count);
        }

        Dictionary<Guid, decimal> allocations;
        if (legList.Count == 0)
            allocations = new Dictionary<Guid, decimal>();
        else if (string.Equals(strategy, DemoAutoTradeStrategyIds.ConfidenceWeighted, StringComparison.Ordinal))
        {
            allocations = ConfidenceWeightedAllocation(legList, totalNotionalInr);
        }
        else if (string.Equals(strategy, DemoAutoTradeStrategyIds.SignalStrengthSquared, StringComparison.Ordinal))
        {
            allocations = SquaredConfidenceAllocation(legList, totalNotionalInr);
        }
        else if (string.Equals(strategy, DemoAutoTradeStrategyIds.ImpliedEdgeWeighted, StringComparison.Ordinal))
        {
            allocations = ImpliedEdgeWeightedAllocation(legList, totalNotionalInr);
            skippedLowConfidence = legList.Count(r => r.Confidence <= 50);
        }
        else
        {
            var legAmt = totalNotionalInr / legList.Count;
            allocations = legList.ToDictionary(r => r.Id, _ => legAmt);
        }

        return new AllocationWork(
            strategy,
            totalSignals,
            pending,
            correct,
            wrong,
            skippedNoPrice,
            directionalTradeable,
            legList,
            allocations,
            skippedLowConfidence);
    }

    private static List<DemoAutoTradeLegRowDto> BuildLegRows(
        IReadOnlyList<MlAutomationPredictionListItemDto> rows,
        AllocationWork work,
        DemoAutoTradeChargeParameters? charges,
        IReadOnlyDictionary<string, int>? lotMultipliersByInstrumentToken)
    {
        var useLots = lotMultipliersByInstrumentToken is not null;
        var applyCharges = charges is { ApplyRoundTripCosts: true };
        var flatPerLeg = charges?.RoundTripFlatInrPerLeg ?? 0m;
        var turnoverBps = charges?.RoundTripTurnoverBps ?? 0m;
        var directionalSet = work.DirectionalTradeable.Select(x => x.Id).ToHashSet();
        var legSet = work.LegList.Select(x => x.Id).ToHashSet();
        var isHighConviction = string.Equals(
            work.NormalizedStrategy,
            DemoAutoTradeStrategyIds.HighConviction,
            StringComparison.Ordinal);

        var list = new List<DemoAutoTradeLegRowDto>(rows.Count);
        foreach (var r in rows.OrderByDescending(x => x.PredictedAt))
        {
            if (string.Equals(r.Outcome, "pending", StringComparison.OrdinalIgnoreCase))
            {
                list.Add(ExcludeLeg(r, "pending", null));
                continue;
            }

            var dir = (r.Direction ?? string.Empty).Trim();
            if (dir.Equals("neutral", StringComparison.OrdinalIgnoreCase))
            {
                list.Add(
                    ExcludeLeg(r, "excluded_neutral", "Neutral direction receives no notional."));
                continue;
            }

            if (r.NextClose is null || r.RefClose <= 0m)
            {
                list.Add(ExcludeLeg(r, "excluded_no_price", "Missing ref close or next close."));
                continue;
            }

            if (!directionalSet.Contains(r.Id))
            {
                var retTry = DirectionalReturnFraction(dir, r.RefClose, r.NextOpen, r.NextClose.Value);
                if (retTry is null)
                {
                    list.Add(
                        ExcludeLeg(r, "excluded_not_directional", "Up/down directional return could not be scored."));
                    continue;
                }

                if (useLots)
                {
                    var tokBad = (r.InstrumentToken ?? string.Empty).Trim();
                    if (string.IsNullOrEmpty(tokBad)
                        || !lotMultipliersByInstrumentToken!.TryGetValue(tokBad, out var lm)
                        || lm < 1)
                    {
                        list.Add(
                            ExcludeLeg(
                                r,
                                "excluded_missing_lot_size",
                                "No Kite lot size on this Locked for trading row; lock the contract again from Browse."));
                        continue;
                    }
                }

                list.Add(ExcludeLeg(r, "excluded_no_price", "Row not in priced directional set."));
                continue;
            }

            if (!legSet.Contains(r.Id))
            {
                var detail = isHighConviction
                    ? $"Below {HighConvictionMinConfidence}% confidence cutoff."
                    : "Not selected under this allocation preset (e.g. one pick per symbol/engine or top-half).";
                list.Add(
                    ExcludeLeg(
                        r,
                        isHighConviction ? "excluded_low_confidence" : "excluded_by_strategy",
                        detail));
                continue;
            }

            if (!work.Allocations.TryGetValue(r.Id, out var alloc) || alloc <= 0m)
            {
                list.Add(
                    ExcludeLeg(
                        r,
                        "excluded_zero_allocation",
                        "Preset left this leg with zero notional (e.g. implied-edge weights)."));
                continue;
            }

            if (useLots)
            {
                var token = (r.InstrumentToken ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(token)
                    || !lotMultipliersByInstrumentToken!.TryGetValue(token, out var lotMult)
                    || lotMult < 1)
                {
                    list.Add(
                        ExcludeLeg(
                            r,
                            "excluded_missing_lot_size",
                            "No Kite lot size on this Locked for trading row; lock the contract again from Browse."));
                    continue;
                }

                if (!TryResolveEntryPrice(r.RefClose, r.NextOpen, out var entryPx))
                {
                    list.Add(ExcludeLeg(r, "excluded_no_price", "Could not resolve entry price."));
                    continue;
                }

                var oneContractAnchor = entryPx * lotMult;
                if (oneContractAnchor <= 0m)
                {
                    list.Add(ExcludeLeg(r, "excluded_no_price", "Invalid entry × lot multiplier."));
                    continue;
                }

                var whole = (int)Math.Floor(alloc / oneContractAnchor);
                if (whole < 1)
                {
                    list.Add(
                        LegRow(
                            r,
                            "excluded_cannot_buy_one_lot",
                            $"INR slice {decimal.Round(alloc, 2, MidpointRounding.AwayFromZero):0.##} is below one whole contract (~{decimal.Round(oneContractAnchor, 2, MidpointRounding.AwayFromZero):0.##} at entry).",
                            decimal.Round(alloc, 2, MidpointRounding.AwayFromZero),
                            lotMult,
                            0,
                            0m,
                            null,
                            null,
                            0m,
                            0m,
                            0m));
                    continue;
                }

                var exitPx = r.NextClose!.Value;
                var grossOpt = TryDirectionalRupeePnl(
                    dir,
                    entryPx,
                    exitPx,
                    lotMult,
                    whole,
                    out var buyPx,
                    out var sellPx);
                if (grossOpt is null)
                {
                    list.Add(
                        LegRow(
                            r,
                            "excluded_not_directional",
                            null,
                            decimal.Round(alloc, 2, MidpointRounding.AwayFromZero),
                            lotMult,
                            0,
                            0m,
                            null,
                            null,
                            0m,
                            0m,
                            0m));
                    continue;
                }

                var gross = grossOpt.Value;
                var fees = EstimateContractLegFees(applyCharges, flatPerLeg, turnoverBps, entryPx, lotMult, whole);
                var net = gross - fees;
                var committed = entryPx * lotMult * whole;
                list.Add(
                    LegRow(
                        r,
                        "allocated",
                        null,
                        decimal.Round(alloc, 2, MidpointRounding.AwayFromZero),
                        lotMult,
                        whole,
                        committed,
                        buyPx,
                        sellPx,
                        gross,
                        fees,
                        net));
                continue;
            }

            var ret = DirectionalReturnFraction(dir, r.RefClose, r.NextOpen, r.NextClose!.Value);
            if (ret is null)
            {
                list.Add(
                    LegRow(
                        r,
                        "excluded_not_directional",
                        null,
                        decimal.Round(alloc, 2, MidpointRounding.AwayFromZero),
                        0,
                        0,
                        decimal.Round(alloc, 2, MidpointRounding.AwayFromZero),
                        null,
                        null,
                        0m,
                        0m,
                        0m));
                continue;
            }

            var grossLegacy = alloc * ret.Value;
            var feesLegacy = 0m;
            if (applyCharges)
                feesLegacy = flatPerLeg + Math.Abs(alloc) * (turnoverBps / 10000m);
            var netLegacy = grossLegacy - feesLegacy;
            list.Add(
                LegRow(
                    r,
                    "allocated",
                    null,
                    decimal.Round(alloc, 2, MidpointRounding.AwayFromZero),
                    0,
                    0,
                    decimal.Round(alloc, 2, MidpointRounding.AwayFromZero),
                    null,
                    null,
                    grossLegacy,
                    feesLegacy,
                    netLegacy));
        }

        return list;
    }

    private static DemoAutoTradeLegRowDto ExcludeLeg(
        MlAutomationPredictionListItemDto r,
        string status,
        string? statusDetail) =>
        LegRow(r, status, statusDetail, 0m, 0, 0, 0m, null, null, 0m, 0m, 0m);

    private static DemoAutoTradeLegRowDto LegRow(
        MlAutomationPredictionListItemDto r,
        string status,
        string? statusDetail,
        decimal allocatedNotionalInr,
        int instrumentLotMultiplier,
        int demoWholeLotsTraded,
        decimal committedExposureApproxInr,
        decimal? hypotheticalBuyPrice,
        decimal? hypotheticalSellPrice,
        decimal legGrossPnlInr,
        decimal legFeesInr,
        decimal legNetPnlInr) =>
        new(
            r.Id,
            r.PredictedAt,
            (r.InstrumentToken ?? string.Empty).Trim(),
            r.Tradingsymbol,
            r.Exchange,
            r.Interval,
            string.IsNullOrWhiteSpace(r.EngineModelId) ? string.Empty : r.EngineModelId.Trim(),
            (r.Direction ?? string.Empty).Trim(),
            r.Confidence,
            (r.Outcome ?? string.Empty).Trim(),
            r.RefClose,
            r.NextOpen,
            r.NextClose,
            status,
            statusDetail,
            allocatedNotionalInr,
            instrumentLotMultiplier,
            demoWholeLotsTraded,
            committedExposureApproxInr,
            hypotheticalBuyPrice is { } bp ? decimal.Round(bp, 4, MidpointRounding.AwayFromZero) : null,
            hypotheticalSellPrice is { } sp ? decimal.Round(sp, 4, MidpointRounding.AwayFromZero) : null,
            decimal.Round(legGrossPnlInr, 2, MidpointRounding.AwayFromZero),
            decimal.Round(legFeesInr, 2, MidpointRounding.AwayFromZero),
            decimal.Round(legNetPnlInr, 2, MidpointRounding.AwayFromZero));

    private static bool TryResolveEntryPrice(decimal refClose, decimal? nextOpen, out decimal entry)
    {
        if (nextOpen is { } o && o > 0m)
        {
            entry = o;
            return true;
        }

        entry = refClose;
        return refClose > 0m;
    }

    private static decimal? TryDirectionalRupeePnl(
        string direction,
        decimal entryPrice,
        decimal exitPrice,
        int lotMultiplier,
        int wholeContracts,
        out decimal buyPrice,
        out decimal sellPrice)
    {
        buyPrice = 0m;
        sellPrice = 0m;
        if (lotMultiplier < 1 || wholeContracts < 1)
            return null;

        var qty = lotMultiplier * wholeContracts;
        if (direction.Equals("up", StringComparison.OrdinalIgnoreCase))
        {
            buyPrice = entryPrice;
            sellPrice = exitPrice;
            return (exitPrice - entryPrice) * qty;
        }

        if (direction.Equals("down", StringComparison.OrdinalIgnoreCase))
        {
            sellPrice = entryPrice;
            buyPrice = exitPrice;
            return (entryPrice - exitPrice) * qty;
        }

        return null;
    }

    private static decimal EstimateContractLegFees(
        bool applyCharges,
        decimal flatPerLeg,
        decimal turnoverBps,
        decimal entryPrice,
        int lotMultiplier,
        int wholeContracts)
    {
        if (!applyCharges || wholeContracts < 1 || lotMultiplier < 1)
            return 0m;

        var exposure = Math.Abs(entryPrice * lotMultiplier * wholeContracts);
        return wholeContracts * flatPerLeg + exposure * (turnoverBps / 10000m) * 2m;
    }

    private static Dictionary<Guid, decimal> ConfidenceWeightedAllocation(
        IReadOnlyList<MlAutomationPredictionListItemDto> legs,
        decimal totalNotionalInr)
    {
        decimal sumWeights = 0m;
        var weightsById = new Dictionary<Guid, decimal>();
        foreach (var r in legs)
        {
            var w = Math.Clamp(r.Confidence, 1, 100);
            weightsById[r.Id] = w;
            sumWeights += w;
        }

        var allocations = new Dictionary<Guid, decimal>();
        foreach (var r in legs)
        {
            var w = weightsById[r.Id];
            allocations[r.Id] = sumWeights <= 0m ? 0m : totalNotionalInr * (w / sumWeights);
        }

        return allocations;
    }

    private static Dictionary<Guid, decimal> SquaredConfidenceAllocation(
        IReadOnlyList<MlAutomationPredictionListItemDto> legs,
        decimal totalNotionalInr)
    {
        decimal sumWeights = 0m;
        var weightsById = new Dictionary<Guid, decimal>();
        foreach (var r in legs)
        {
            var b = (decimal)Math.Clamp(r.Confidence, 1, 100);
            var w = b * b;
            weightsById[r.Id] = w;
            sumWeights += w;
        }

        var allocations = new Dictionary<Guid, decimal>();
        foreach (var r in legs)
        {
            var w = weightsById[r.Id];
            allocations[r.Id] = sumWeights <= 0m ? 0m : totalNotionalInr * (w / sumWeights);
        }

        return allocations;
    }

    /// <summary>Weights ∝ max(0, 2p−1) for p = confidence in [0,1]. All weights zero → no allocation.</summary>
    private static Dictionary<Guid, decimal> ImpliedEdgeWeightedAllocation(
        IReadOnlyList<MlAutomationPredictionListItemDto> legs,
        decimal totalNotionalInr)
    {
        decimal sumWeights = 0m;
        var weightsById = new Dictionary<Guid, decimal>();
        foreach (var r in legs)
        {
            var p = Math.Clamp(r.Confidence, 0, 100) / 100m;
            var w = Math.Max(0m, 2m * p - 1m);
            weightsById[r.Id] = w;
            sumWeights += w;
        }

        var allocations = new Dictionary<Guid, decimal>();
        if (sumWeights <= 0m)
        {
            foreach (var r in legs)
                allocations[r.Id] = 0m;
            return allocations;
        }

        foreach (var r in legs)
        {
            var w = weightsById[r.Id];
            allocations[r.Id] = totalNotionalInr * (w / sumWeights);
        }

        return allocations;
    }

    /// <summary>
    /// Signed fractional return for a directional bet. When <paramref name="nextOpen"/> is positive, uses next open→next close (market-style);
    /// otherwise ref close→next close for backward compatibility with older rows.
    /// </summary>
    private static decimal? DirectionalReturnFraction(
        string direction,
        decimal refClose,
        decimal? nextOpen,
        decimal nextClose)
    {
        if (nextOpen is { } entry && entry > 0m)
        {
            var move = (nextClose - entry) / entry;
            if (direction.Equals("up", StringComparison.OrdinalIgnoreCase))
                return move;

            if (direction.Equals("down", StringComparison.OrdinalIgnoreCase))
                return -move;

            return null;
        }

        return DirectionalReturnFractionRefToNextClose(direction, refClose, nextClose);
    }

    private static decimal? DirectionalReturnFractionRefToNextClose(string direction, decimal refClose, decimal nextClose)
    {
        if (refClose <= 0m)
            return null;

        var move = (nextClose - refClose) / refClose;
        if (direction.Equals("up", StringComparison.OrdinalIgnoreCase))
            return move;

        if (direction.Equals("down", StringComparison.OrdinalIgnoreCase))
            return -move;

        return null;
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            return TimeZoneInfo.Utc;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(
                    timeZoneId.Trim().Equals("Asia/Kolkata", StringComparison.OrdinalIgnoreCase)
                        ? "India Standard Time"
                        : timeZoneId.Trim());
            }
            catch
            {
                return TimeZoneInfo.Utc;
            }
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}

public sealed record DemoAutoTradeEodTotals(
    int TotalSignals,
    int PendingSignals,
    int CorrectOutcomes,
    int WrongOutcomes,
    int SkippedNoNextClose,
    /// <summary>Priced up/down automation rows counted for sizing (same day).</summary>
    int DirectionalTradeableLegs,
    /// <summary>Rows that receive a positive hypothetical notional under the chosen strategy.</summary>
    int AllocatedLegsForPnl,
    int SkippedLowConfidenceLegs,
    /// <summary>Hypothetical P&amp;L before host-configured round-trip fees.</summary>
    decimal HypotheticalGrossPnlInr,
    /// <summary>Sum of flat + turnover-based fees per allocated leg (INR).</summary>
    decimal HypotheticalChargesInr,
    /// <summary>Hypothetical P&amp;L after fees (gross − charges).</summary>
    decimal HypotheticalTotalPnlInr);
