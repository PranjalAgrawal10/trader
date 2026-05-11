using Trader.Application.Prediction;

namespace Trader.Application.Broker;

/// <summary>
/// Hypothetical end-of-day demo P&amp;L from automation rows: splits <paramref name="totalNotionalInr"/>
/// equally across resolved (non-pending) rows that have next-bar closes, then applies direction vs ref→next return.
/// </summary>
public static class DemoAutoTradeEodSummaryCalculator
{
    public const decimal DefaultNotionalInr = 100_000m;

    public static readonly string AllocationNote =
        "Demo allocates the full notional equally across each resolved same-day automation signal (ref→next close); " +
        "neutral direction legs contribute 0. No brokerage or real orders.";

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

    public static DemoAutoTradeEodTotals Compute(
        IReadOnlyList<MlAutomationPredictionListItemDto> rows,
        decimal totalNotionalInr)
    {
        var totalSignals = rows.Count;
        var pending = 0;
        var correct = 0;
        var wrong = 0;
        var skippedNoPrice = 0;
        var resolvedForPnl = new List<MlAutomationPredictionListItemDto>();

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

            if (r.NextClose is not { } next || r.RefClose <= 0m)
            {
                skippedNoPrice++;
                continue;
            }

            resolvedForPnl.Add(r);
        }

        var n = resolvedForPnl.Count;
        var leg = n > 0 ? totalNotionalInr / n : 0m;
        decimal totalPnl = 0m;

        foreach (var r in resolvedForPnl)
        {
            var dir = (r.Direction ?? string.Empty).Trim();
            if (dir.Equals("neutral", StringComparison.OrdinalIgnoreCase))
                continue;

            var ret = DirectionalReturnFraction(dir, r.RefClose, r.NextClose!.Value);
            if (ret is null)
                continue;

            totalPnl += leg * ret.Value;
        }

        return new DemoAutoTradeEodTotals(
            TotalSignals: totalSignals,
            PendingSignals: pending,
            CorrectOutcomes: correct,
            WrongOutcomes: wrong,
            SkippedNoNextClose: skippedNoPrice,
            ResolvedSignalsUsedForPnl: resolvedForPnl.Count,
            HypotheticalTotalPnlInr: decimal.Round(totalPnl, 2, MidpointRounding.AwayFromZero));
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
    int ResolvedSignalsUsedForPnl,
    decimal HypotheticalTotalPnlInr);
