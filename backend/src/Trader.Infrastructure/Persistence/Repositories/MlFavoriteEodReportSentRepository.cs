using Microsoft.EntityFrameworkCore;
using Trader.Application.Abstractions.Persistence;
using Trader.Domain.Entities;

namespace Trader.Infrastructure.Persistence.Repositories;

public sealed class MlFavoriteEodReportSentRepository : IMlFavoriteEodReportSentRepository
{
    private readonly TraderDbContext _db;

    public MlFavoriteEodReportSentRepository(TraderDbContext db)
    {
        _db = db;
    }

    public Task<bool> ExistsAsync(Guid userId, string reportDayYmd, CancellationToken ct = default) =>
        _db.MlFavoriteEodReportsSent.AnyAsync(x => x.UserId == userId && x.ReportDayYmd == reportDayYmd, ct);

    public async Task AddAsync(Guid userId, string reportDayYmd, DateTimeOffset sentAtUtc, CancellationToken ct = default)
    {
        await _db.MlFavoriteEodReportsSent.AddAsync(
            new MlFavoriteEodReportSent
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ReportDayYmd = reportDayYmd,
                SentAtUtc = sentAtUtc,
            },
            ct);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
