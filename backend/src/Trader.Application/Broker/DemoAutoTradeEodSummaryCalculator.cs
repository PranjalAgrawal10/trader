using Trader.Application.Prediction;

namespace Trader.Application.Broker;

/// <summary>
/// Hypothetical end-of-day demo P&amp;L from merged automation rows; allocation rules are illustrative only (no brokerage, no orders).
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
            "Illustrative only — not advice. Directions up/down vs ref→next return; neutral legs get no allocation. " +
            "No fees, slippage, leverage, position sizing, or real execution. ";

        var legNote =
            $"{totals.AllocatedLegsForPnl} directional leg(s) received allocation from {totals.DirectionalTradeableLegs} priced directional signal(s) this day.";
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
        DemoAutoTradeChargeParameters? charges = null) =>
        ComputeWithLegRows(rows, totalNotionalInr, strategyCode, charges).Totals;

    public static (DemoAutoTradeEodTotals Totals, IReadOnlyList<DemoAutoTradeLegRowDto> Legs) ComputeWithLegRows(
        IReadOnlyList<MlAutomationPredictionListItemDto> rows,
        decimal totalNotionalInr,
        string? strategyCode,
        DemoAutoTradeChargeParameters? charges = null)
    {
        var work = BuildAllocationWork(rows, totalNotionalInr, strategyCode);
        var applyCharges = charges is { ApplyRoundTripCosts: true };
        var flatPerLeg = charges?.RoundTripFlatInrPerLeg ?? 0m;
        var turnoverBps = charges?.RoundTripTurnoverBps ?? 0m;

        decimal grossPnl = 0m;
        decimal feesInr = 0m;
        foreach (var r in work.DirectionalTradeable)
        {
            if (!work.Allocations.TryGetValue(r.Id, out var notionalAllocated) || notionalAllocated <= 0m)
                continue;

            var dir = (r.Direction ?? string.Empty).Trim();
            var ret = DirectionalReturnFraction(dir, r.RefClose, r.NextClose!.Value);
            if (ret is null)
                continue;

            grossPnl += notionalAllocated * ret.Value;
            if (applyCharges)
                feesInr += flatPerLeg + Math.Abs(notionalAllocated) * (turnoverBps / 10000m);
        }

        var netPnl = grossPnl - feesInr;
        var allocatedCount = work.Allocations.Values.Count(v => v > 0m);

        var totals = new DemoAutoTradeEodTotals(
            TotalSignals: work.TotalSignals,
            PendingSignals: work.PendingSignals,
            CorrectOutcomes: work.CorrectOutcomes,
            WrongOutcomes: work.WrongOutcomes,
            SkippedNoNextClose: work.SkippedNoNextClose,
            DirectionalTradeableLegs: work.DirectionalTradeable.Count,
            AllocatedLegsForPnl: allocatedCount,
            SkippedLowConfidenceLegs: work.SkippedLowConfidenceLegs,
            HypotheticalGrossPnlInr: decimal.Round(grossPnl, 2, MidpointRounding.AwayFromZero),
            HypotheticalChargesInr: decimal.Round(feesInr, 2, MidpointRounding.AwayFromZero),
            HypotheticalTotalPnlInr: decimal.Round(netPnl, 2, MidpointRounding.AwayFromZero));

        var legs = BuildLegRows(rows, work, charges);
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
        string? strategyCode)
    {
        var strategy = DemoAutoTradeStrategyIds.NormalizeOrDefault(strategyCode);
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

            if (DirectionalReturnFraction(dir, r.RefClose, r.NextClose!.Value) is null)
                continue;

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
        DemoAutoTradeChargeParameters? charges)
    {
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
                list.Add(LegRow(r, "pending", null, 0m, 0m, 0m, 0m));
                continue;
            }

            var dir = (r.Direction ?? string.Empty).Trim();
            if (dir.Equals("neutral", StringComparison.OrdinalIgnoreCase))
            {
                list.Add(
                    LegRow(
                        r,
                        "excluded_neutral",
                        "Neutral direction receives no notional.",
                        0m,
                        0m,
                        0m,
                        0m));
                continue;
            }

            if (r.NextClose is null || r.RefClose <= 0m)
            {
                list.Add(LegRow(r, "excluded_no_price", "Missing ref close or next close.", 0m, 0m, 0m, 0m));
                continue;
            }

            if (!directionalSet.Contains(r.Id))
            {
                var retTry = DirectionalReturnFraction(dir, r.RefClose, r.NextClose.Value);
                var st = retTry is null ? "excluded_not_directional" : "excluded_no_price";
                var det = retTry is null
                    ? "Up/down move vs ref→next could not be scored."
                    : "Row not in priced directional set.";
                list.Add(LegRow(r, st, det, 0m, 0m, 0m, 0m));
                continue;
            }

            if (!legSet.Contains(r.Id))
            {
                var detail = isHighConviction
                    ? $"Below {HighConvictionMinConfidence}% confidence cutoff."
                    : "Not selected under this allocation preset (e.g. one pick per symbol/engine or top-half).";
                list.Add(
                    LegRow(
                        r,
                        isHighConviction ? "excluded_low_confidence" : "excluded_by_strategy",
                        detail,
                        0m,
                        0m,
                        0m,
                        0m));
                continue;
            }

            if (!work.Allocations.TryGetValue(r.Id, out var alloc) || alloc <= 0m)
            {
                list.Add(
                    LegRow(
                        r,
                        "excluded_zero_allocation",
                        "Preset left this leg with zero notional (e.g. implied-edge weights).",
                        0m,
                        0m,
                        0m,
                        0m));
                continue;
            }

            var ret = DirectionalReturnFraction(dir, r.RefClose, r.NextClose!.Value);
            if (ret is null)
            {
                list.Add(LegRow(r, "excluded_not_directional", null, alloc, 0m, 0m, 0m));
                continue;
            }

            var gross = alloc * ret.Value;
            var fees = 0m;
            if (applyCharges)
                fees = flatPerLeg + Math.Abs(alloc) * (turnoverBps / 10000m);
            var net = gross - fees;
            list.Add(
                LegRow(
                    r,
                    "allocated",
                    null,
                    decimal.Round(alloc, 2, MidpointRounding.AwayFromZero),
                    decimal.Round(gross, 2, MidpointRounding.AwayFromZero),
                    decimal.Round(fees, 2, MidpointRounding.AwayFromZero),
                    decimal.Round(net, 2, MidpointRounding.AwayFromZero)));
        }

        return list;
    }

    private static DemoAutoTradeLegRowDto LegRow(
        MlAutomationPredictionListItemDto r,
        string status,
        string? statusDetail,
        decimal alloc,
        decimal gross,
        decimal fees,
        decimal net) =>
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
            r.NextClose,
            status,
            statusDetail,
            alloc,
            gross,
            fees,
            net);

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

    private static decimal? DirectionalReturnFraction(string direction, decimal refClose, decimal nextClose)
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
