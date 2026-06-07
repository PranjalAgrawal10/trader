using Trader.Application.Abstractions.Persistence;
using Trader.Domain.Entities;

namespace Trader.Infrastructure.Persistence.Repositories;

public sealed class UserLoginAuditRepository : IUserLoginAuditRepository
{
    private readonly TraderDbContext _db;

    public UserLoginAuditRepository(TraderDbContext db)
    {
        _db = db;
    }

    public Task AddAsync(UserLoginAudit audit, CancellationToken ct = default) =>
        _db.UserLoginAudits.AddAsync(audit, ct).AsTask();

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
