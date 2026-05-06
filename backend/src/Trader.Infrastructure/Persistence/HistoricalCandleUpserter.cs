using Microsoft.EntityFrameworkCore;
using Trader.Application.Abstractions.Persistence;

namespace Trader.Infrastructure.Persistence;

public sealed class HistoricalCandleUpserter : IHistoricalCandleUpserter
{
    private readonly TraderDbContext _db;

    public HistoricalCandleUpserter(TraderDbContext db)
    {
        _db = db;
    }

    public async Task UpsertAsync(
        string instrumentToken,
        string timeframe,
        DateTimeOffset timestampUtc,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        long volume,
        CancellationToken ct = default)
    {
        var id = Guid.NewGuid();
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO `HistoricalCandles` (`Id`, `InstrumentToken`, `Timeframe`, `TimestampUtc`, `Open`, `High`, `Low`, `Close`, `Volume`)
            VALUES ({id}, {instrumentToken}, {timeframe}, {timestampUtc}, {open}, {high}, {low}, {close}, {volume})
            ON DUPLICATE KEY UPDATE
            `Open` = VALUES(`Open`),
            `High` = VALUES(`High`),
            `Low` = VALUES(`Low`),
            `Close` = VALUES(`Close`),
            `Volume` = VALUES(`Volume`);
            """,
            ct);
    }
}
