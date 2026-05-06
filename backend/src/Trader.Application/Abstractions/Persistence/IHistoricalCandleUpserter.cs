namespace Trader.Application.Abstractions.Persistence;

/// <summary>Idempotent insert/update for a single bar (MySQL unique key on instrument + timeframe + open time).</summary>
public interface IHistoricalCandleUpserter
{
    Task UpsertAsync(
        string instrumentToken,
        string timeframe,
        DateTimeOffset timestampUtc,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        long volume,
        CancellationToken ct = default);
}
