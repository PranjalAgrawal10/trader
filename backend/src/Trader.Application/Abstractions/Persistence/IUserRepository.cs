using Trader.Domain.Entities;

namespace Trader.Application.Abstractions.Persistence;

public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsByEmailAsync(string email, CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
    Task<User?> GetByEmailVerificationTokenHashAsync(string tokenHash, CancellationToken ct = default);

    Task<User?> GetByPasswordResetTokenHashAsync(string tokenHash, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
