namespace Trader.Application.Abstractions.Persistence;

public interface IMlFavoriteEodReportSentRepository
{
    Task<bool> ExistsAsync(Guid userId, string reportDayYmd, CancellationToken ct = default);

    Task AddAsync(Guid userId, string reportDayYmd, DateTimeOffset sentAtUtc, CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
