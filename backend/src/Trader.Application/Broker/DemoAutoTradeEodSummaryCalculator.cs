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
        return baseNote + legNote;
    }

    public static DemoAutoTradeEodTotals Compute(
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

        var legList = positioned.ToList();

        Dictionary<Guid, decimal> allocations;
        if (legList.Count == 0)
            allocations = new Dictionary<Guid, decimal>();
        else if (string.Equals(strategy, DemoAutoTradeStrategyIds.ConfidenceWeighted, StringComparison.Ordinal))
        {
            allocations = ConfidenceWeightedAllocation(legList, totalNotionalInr);
        }
        else
        {
            var legAmt = totalNotionalInr / legList.Count;
            allocations = legList.ToDictionary(r => r.Id, _ => legAmt);
        }

        decimal totalPnl = 0m;
        foreach (var r in directionalTradeable)
        {
            if (!allocations.TryGetValue(r.Id, out var notionalAllocated) || notionalAllocated <= 0m)
                continue;

            var dir = (r.Direction ?? string.Empty).Trim();
            var ret = DirectionalReturnFraction(dir, r.RefClose, r.NextClose!.Value);
            if (ret is null)
                continue;

            totalPnl += notionalAllocated * ret.Value;
        }

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
            HypotheticalTotalPnlInr: decimal.Round(totalPnl, 2, MidpointRounding.AwayFromZero));
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
    decimal HypotheticalTotalPnlInr);
