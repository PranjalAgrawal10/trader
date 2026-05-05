using Microsoft.EntityFrameworkCore;
using Trader.Application.Abstractions.Persistence;
using Trader.Domain.Entities;

namespace Trader.Infrastructure.Persistence.Repositories;

public sealed class EmailOtpRepository : IEmailOtpRepository
{
    private readonly TraderDbContext _db;

    public EmailOtpRepository(TraderDbContext db)
    {
        _db = db;
    }

    public async Task InvalidatePendingForEmailAsync(string normalizedEmail, CancellationToken ct = default)
    {
        await _db.EmailOtpChallenges.AsNoTracking()
            .Where(x => x.NormalizedEmail == normalizedEmail && !x.IsConsumed)
            .ExecuteDeleteAsync(ct);
    }

    public async Task AddAsync(EmailOtpChallenge challenge, CancellationToken ct = default) =>
        await _db.EmailOtpChallenges.AddAsync(challenge, ct);

    public Task DeleteByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.EmailOtpChallenges.Where(x => x.Id == id).ExecuteDeleteAsync(ct);

    public async Task<EmailOtpChallenge?> GetLatestForEmailAsync(string normalizedEmail, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        return await _db.EmailOtpChallenges.AsTracking()
            .Where(x => x.NormalizedEmail == normalizedEmail && !x.IsConsumed && x.ExpiresAtUtc > now)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
