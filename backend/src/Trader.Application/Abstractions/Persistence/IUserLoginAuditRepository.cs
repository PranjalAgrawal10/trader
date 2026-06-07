using Trader.Domain.Entities;

namespace Trader.Application.Abstractions.Persistence;

public interface IUserLoginAuditRepository
{
    Task AddAsync(UserLoginAudit audit, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
