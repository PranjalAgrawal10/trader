using Trader.Domain.Entities;
using Trader.Domain.Enums;

namespace Trader.Application.Abstractions.Persistence;

public interface IEmailOtpRepository
{
    Task InvalidatePendingForEmailAsync(
        string normalizedEmail,
        EmailOtpPurpose purpose,
        CancellationToken ct = default);

    Task AddAsync(EmailOtpChallenge challenge, CancellationToken ct = default);

    /// <summary>Removes one row by id (e.g. rollback after SMTP failure).</summary>
    Task DeleteByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Latest non-consumed row for this email and purpose, if any.</summary>
    Task<EmailOtpChallenge?> GetLatestForEmailAsync(
        string normalizedEmail,
        EmailOtpPurpose purpose,
        CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
