using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Trader.Application.Broker;

namespace Trader.Infrastructure.Persistence;

public sealed class BrokerSetupGateway : IBrokerSetupGateway
{
    private readonly TraderDbContext _db;
    private readonly IDataProtector _tokens;

    public BrokerSetupGateway(TraderDbContext db, IDataProtectionProvider protectionProvider)
    {
        _db = db;
        _tokens = protectionProvider.CreateProtector("Trader.Broker.Kite.Tokens");
    }

    public async Task<BrokerSetupSnapshot?> GetSnapshotAsync(Guid userId, CancellationToken ct)
    {
        return await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new BrokerSetupSnapshot(u.Id, u.BrokerConnectedAt, u.BrokerProvider))
            .FirstOrDefaultAsync(ct);
    }

    public async Task CompleteBrokerSetupAsync(Guid userId, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new InvalidOperationException("User not found.");

        user.MarkBrokerConnected(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(ct);
    }

    public async Task PersistKiteSessionAsync(Guid userId, BrokerKitePersistRequest session, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new InvalidOperationException("User not found.");

        user.KiteAccessTokenProtected = _tokens.Protect(session.AccessToken);
        user.KiteRefreshTokenProtected = string.IsNullOrEmpty(session.RefreshToken)
            ? null
            : _tokens.Protect(session.RefreshToken);
        user.KiteUserId = session.KiteUserId;
        user.BrokerProvider = "Zerodha";
        user.MarkBrokerConnected(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DisconnectBrokerAsync(Guid userId, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new InvalidOperationException("User not found.");

        user.BrokerConnectedAt = null;
        user.BrokerProvider = null;
        user.KiteAccessTokenProtected = null;
        user.KiteRefreshTokenProtected = null;
        user.KiteUserId = null;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<string?> GetKiteAccessTokenAsync(Guid userId, CancellationToken ct)
    {
        var blob = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.KiteAccessTokenProtected)
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
