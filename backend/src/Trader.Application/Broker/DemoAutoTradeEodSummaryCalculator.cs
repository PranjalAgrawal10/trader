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
        DemoAutoTradeChargeParameters? charges = null)
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

        var applyCharges = charges is { ApplyRoundTripCosts: true };
        var flatPerLeg = charges?.RoundTripFlatInrPerLeg ?? 0m;
        var turnoverBps = charges?.RoundTripTurnoverBps ?? 0m;

        decimal grossPnl = 0m;
        decimal feesInr = 0m;
        foreach (var r in directionalTradeable)
        {
            if (!allocations.TryGetValue(r.Id, out var notionalAllocated) || notionalAllocated <= 0m)
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
        var allocatedCount = allocations.Values.Count(v => v > 0m);

        return new DemoAutoTradeEodTotals(
            TotalSignals: totalSignals,
            PendingSignals: pending,
            CorrectOutcomes: correct,
            WrongOutcomes: wrong,
            SkippedNoNextClose: skippedNoPrice,
            DirectionalTradeableLegs: directionalTradeable.Count,
            AllocatedLegsForPnl: allocatedCount,
            SkippedLowConfidenceLegs: skippedLowConfidence,
            HypotheticalGrossPnlInr: decimal.Round(grossPnl, 2, MidpointRounding.AwayFromZero),
            HypotheticalChargesInr: decimal.Round(feesInr, 2, MidpointRounding.AwayFromZero),
            HypotheticalTotalPnlInr: decimal.Round(netPnl, 2, MidpointRounding.AwayFromZero));
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
