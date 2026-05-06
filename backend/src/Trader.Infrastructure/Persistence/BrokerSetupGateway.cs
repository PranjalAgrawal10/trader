using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Trader.Application.Broker;
using Trader.Domain.Entities;

namespace Trader.Infrastructure.Persistence;

public sealed class BrokerSetupGateway : IBrokerSetupGateway
{
    private const string ZerodhaBrokerName = "Zerodha";

    private readonly TraderDbContext _db;
    private readonly IDataProtector _tokens;

    public BrokerSetupGateway(TraderDbContext db, IDataProtectionProvider protectionProvider)
    {
        _db = db;
        _tokens = protectionProvider.CreateProtector("Trader.Broker.Kite.Tokens");
    }

    public async Task<BrokerSetupSnapshot?> GetSnapshotAsync(Guid userId, CancellationToken ct)
    {
        var exists = await _db.Users.AsNoTracking().AnyAsync(u => u.Id == userId, ct);
        if (!exists)
            return null;

        var account = await _db.BrokerAccounts.AsNoTracking()
            .Where(a => a.UserId == userId && a.BrokerName == ZerodhaBrokerName)
            .FirstOrDefaultAsync(ct);

        if (account is null || string.IsNullOrEmpty(account.AccessTokenProtected))
            return new BrokerSetupSnapshot(userId, null, null);

        return new BrokerSetupSnapshot(userId, account.ConnectedAt, ZerodhaBrokerName);
    }

    public async Task CompleteBrokerSetupAsync(Guid userId, CancellationToken ct)
    {
        var account = await _db.BrokerAccounts
            .FirstOrDefaultAsync(a => a.UserId == userId && a.BrokerName == ZerodhaBrokerName, ct);

        if (account is null || string.IsNullOrEmpty(account.AccessTokenProtected))
            return;

        account.ConnectedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task PersistKiteSessionAsync(Guid userId, BrokerKitePersistRequest session, CancellationToken ct)
    {
        _ = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new InvalidOperationException("User not found.");

        var account = await _db.BrokerAccounts
            .FirstOrDefaultAsync(a => a.UserId == userId && a.BrokerName == ZerodhaBrokerName, ct);

        var now = DateTimeOffset.UtcNow;
        if (account is null)
        {
            account = new BrokerAccount
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                BrokerName = ZerodhaBrokerName,
                ConnectedAt = now,
            };
            _db.BrokerAccounts.Add(account);
        }

        account.AccessTokenProtected = _tokens.Protect(session.AccessToken);
        account.RefreshTokenProtected = string.IsNullOrEmpty(session.RefreshToken)
            ? null
            : _tokens.Protect(session.RefreshToken);
        account.ExternalUserId = session.KiteUserId;
        account.ConnectedAt = now;

        await _db.SaveChangesAsync(ct);
    }

    public async Task DisconnectBrokerAsync(Guid userId, CancellationToken ct)
    {
        var rows = await _db.BrokerAccounts
            .Where(a => a.UserId == userId && a.BrokerName == ZerodhaBrokerName)
            .ToListAsync(ct);

        if (rows.Count > 0)
        {
            _db.BrokerAccounts.RemoveRange(rows);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<string?> GetKiteAccessTokenAsync(Guid userId, CancellationToken ct)
    {
        var blob = await _db.BrokerAccounts.AsNoTracking()
            .Where(a => a.UserId == userId && a.BrokerName == ZerodhaBrokerName)
            .Select(a => a.AccessTokenProtected)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrEmpty(blob))
            return null;

        try
        {
            return _tokens.Unprotect(blob);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
