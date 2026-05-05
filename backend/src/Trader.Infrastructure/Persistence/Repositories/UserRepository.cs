using Microsoft.EntityFrameworkCore;
using Trader.Application.Abstractions.Persistence;
using Trader.Domain.Entities;

namespace Trader.Infrastructure.Persistence.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly TraderDbContext _db;

    public UserRepository(TraderDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(User user, CancellationToken ct = default) =>
        await _db.Users.AddAsync(user, ct);

    public Task<bool> ExistsByEmailAsync(string email, CancellationToken ct = default) =>
        _db.Users.AnyAsync(u => u.Email == email, ct);

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

    public Task<User?> GetByEmailVerificationTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
        _db.Users.FirstOrDefaultAsync(u => u.EmailVerificationTokenHash == tokenHash, ct);

    public Task<User?> GetByPasswordResetTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
        _db.Users.FirstOrDefaultAsync(u => u.PasswordResetTokenHash == tokenHash, ct);

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
