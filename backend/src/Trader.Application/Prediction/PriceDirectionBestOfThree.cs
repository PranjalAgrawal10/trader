using System.Text.RegularExpressions;
using Trader.Application.Broker;

namespace Trader.Application.Prediction;

/// <summary>
/// Favorite automation: three inferences on the same latest bar by shifting the oldest candle out of the window (0, 1, 2 drops).
/// Majority vote becomes stored <see cref="Domain.Entities.MlPriceDirectionPrediction.Direction"/>; counts are embedded in <c>detail</c> for reports.
/// </summary>
public static class PriceDirectionBestOfThree
{
    public const int VoteCount = 3;

    /// <summary>
    /// Prefix pattern: <c>[b3 u=2 d=1 n=0 v=up|up|down m=up] </c> then original engine detail.
    /// </summary>
    private static readonly Regex DetailCountsRegex = new(
        @"^\[b3 u=(\d+) d=(\d+) n=(\d+) v=(.+?) m=(up|down|neutral)\] ",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Requires <paramref name="candlesAsc"/>.Count &gt;= <paramref name="minCandlesRequired"/> + <see cref="VoteCount"/> - 1.
    /// </summary>
    public static bool TryCompute(
        IReadOnlyList<KiteHistoricalCandlePointDto> candlesAsc,
        IPriceDirectionPredictionEngine engine,
        int minCandlesRequired,
        out PriceDirectionResult merged,
        out string detailPrefix)
    {
        detailPrefix = string.Empty;
        merged = default!;

        if (candlesAsc.Count < minCandlesRequired + VoteCount - 1)
            return false;

        var votes = new PriceDirectionResult[VoteCount];
        for (var skip = 0; skip < VoteCount; skip++)
        {
            var slice = candlesAsc.Skip(skip).ToList();
            votes[skip] = engine.PredictNextDirection(slice);
        }

        merged = MergeVotes(votes);
        var (u, d, n, seq) = SummarizeVotes(votes);
        detailPrefix = FormatDetailPrefix(u, d, n, seq, MapDir(merged.Direction));
        return true;
    }

    /// <summary>Parses the leading <c>[b3 …] </c> tag from a stored prediction <c>detail</c>.</summary>
    public static bool TryParseDetailCounts(string? detail, out int up, out int down, out int neutral)
    {
        up = down = neutral = 0;
        if (string.IsNullOrEmpty(detail))
            return false;

        var m = DetailCountsRegex.Match(detail);
        if (!m.Success)
            return false;

        up = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        down = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        neutral = int.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>Returns vote sequence (comma-separated) and majority direction when <see cref="TryParseDetailCounts"/> succeeds.</summary>
    public static bool TryParseDetailExtended(
        string? detail,
        out int up,
        out int down,
        out int neutral,
        out string? votesCommaSeparated,
        out string? majority)
    {
        votesCommaSeparated = majority = null;
        if (!TryParseDetailCounts(detail, out up, out down, out neutral))
            return false;

        var m = DetailCountsRegex.Match(detail!);
        votesCommaSeparated = m.Groups[4].Value.Replace("|", ",", StringComparison.Ordinal);
        majority = m.Groups[5].Value;
        return true;
    }

    /// <summary>Sums component votes across report rows for direction-vote pie charts.</summary>
    public static (int Up, int Down, int Neutral) SumVoteComponents(IEnumerable<string?> details)
    {
        var u = 0;
        var d = 0;
        var n = 0;
        foreach (var det in details)
        {
            if (TryParseDetailCounts(det, out var up, out var down, out var neu))
            {
                u += up;
                d += down;
                n += neu;
            }
        }

        return (u, d, n);
    }

    private static PriceDirectionResult MergeVotes(PriceDirectionResult[] votes)
    {
        var best = votes
            .GroupBy(v => v.Direction)
            .Select(g => (Dir: g.Key, Count: g.Count(), SumConf: g.Sum(x => x.Confidence)))
            .OrderByDescending(x => x.Count)
            .ThenByDescending(x => x.SumConf)
            .ThenByDescending(x => DirPrecedence(x.Dir))
            .First();

        var avgConf = (int)Math.Round(votes.Average(v => v.Confidence));
        avgConf = Math.Clamp(avgConf, 0, 100);
        return new PriceDirectionResult(best.Dir, avgConf, votes[0].ModelId, votes[0].Detail);
    }

    private static int DirPrecedence(PriceDirectionLabel d) =>
        d switch
        {
            PriceDirectionLabel.Up => 2,
            PriceDirectionLabel.Down => 1,
            _ => 0,
        };

    private static (int U, int D, int N, string Seq) SummarizeVotes(PriceDirectionResult[] votes)
    {
        var u = 0;
        var d = 0;
        var n = 0;
        var parts = new string[votes.Length];
        for (var i = 0; i < votes.Length; i++)
        {
            switch (votes[i].Direction)
            {
                case PriceDirectionLabel.Up:
                    u++;
                    parts[i] = "up";
                    break;
                case PriceDirectionLabel.Down:
                    d++;
                    parts[i] = "down";
                    break;
                default:
                    n++;
                    parts[i] = "neutral";
                    break;
            }
        }

        return (u, d, n, string.Join('|', parts));
    }

    private static string FormatDetailPrefix(int u, int d, int n, string seq, string majorityDir) =>
        $"[b3 u={u.ToString(System.Globalization.CultureInfo.InvariantCulture)} d={d.ToString(System.Globalization.CultureInfo.InvariantCulture)} n={n.ToString(System.Globalization.CultureInfo.InvariantCulture)} v={seq} m={majorityDir}] ";

    private static string MapDir(PriceDirectionLabel d) =>
        d switch
        {
            PriceDirectionLabel.Up => "up",
            PriceDirectionLabel.Down => "down",
            _ => "neutral",
        };
}
