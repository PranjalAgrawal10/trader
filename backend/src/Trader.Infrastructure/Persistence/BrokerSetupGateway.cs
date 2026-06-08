using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trader.Application.Broker;
using Trader.Domain.Entities;

namespace Trader.Infrastructure.Persistence;

public sealed class BrokerSetupGateway : IBrokerSetupGateway
{
    private const string ZerodhaBrokerName = "Zerodha";

    private readonly TraderDbContext _db;
    private readonly IDataProtector _tokens;
    private readonly ILogger<BrokerSetupGateway> _logger;

    public BrokerSetupGateway(
        TraderDbContext db,
        IDataProtectionProvider protectionProvider,
        ILogger<BrokerSetupGateway> logger)
    {
        _db = db;
        _tokens = protectionProvider.CreateProtector("Trader.Broker.Kite.Tokens");
        _logger = logger;
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

        try
        {
            var access = _tokens.Unprotect(account.AccessTokenProtected);
            if (string.IsNullOrEmpty(access))
            {
                await RemoveStaleZerodhaAccountsAsync(userId, ct).ConfigureAwait(false);
                _logger.LogWarning(
                    "Removed Zerodha broker row for user {UserId}: access token decrypted to empty.",
                    userId);
                return new BrokerSetupSnapshot(userId, null, null);
            }
        }
        catch (CryptographicException ex)
        {
            await RemoveStaleZerodhaAccountsAsync(userId, ct).ConfigureAwait(false);
            _logger.LogWarning(
                ex,
                "Removed Zerodha broker credentials for user {UserId}: cannot decrypt tokens. " +
                "Typical causes: redeploy without a persisted DataProtection__KeyRingPath, or rotated key ring.",
                userId);
            return new BrokerSetupSnapshot(userId, null, null);
        }

        return new BrokerSetupSnapshot(userId, account.ConnectedAt, ZerodhaBrokerName);
    }

    /// <summary>Deletes encrypted broker rows when ciphertext no longer decrypts so status and UX match API reality.</summary>
    private async Task RemoveStaleZerodhaAccountsAsync(Guid userId, CancellationToken ct)
    {
        var rows = await _db.BrokerAccounts
            .Where(a => a.UserId == userId && a.BrokerName == ZerodhaBrokerName)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (rows.Count == 0)
            return;

        _db.BrokerAccounts.RemoveRange(rows);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
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
        return await GetBrokerAccessTokenAsync(userId, ZerodhaBrokerName, ct).ConfigureAwait(false);
    }

    public async Task<string?> GetBrokerAccessTokenAsync(Guid userId, string brokerName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(brokerName))
            return null;

        var broker = brokerName.Trim();
        var blob = await _db.BrokerAccounts.AsNoTracking()
            .Where(a => a.UserId == userId && a.BrokerName == broker)
            .Select(a => a.AccessTokenProtected)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

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

    public async Task<IReadOnlyList<string>> GetConnectedBrokerProvidersAsync(Guid userId, CancellationToken ct)
    {
        var rows = await _db.BrokerAccounts.AsNoTracking()
            .Where(a => a.UserId == userId && !string.IsNullOrEmpty(a.AccessTokenProtected))
            .Select(a => new { a.BrokerName, a.AccessTokenProtected })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var list = new List<string>();
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.BrokerName) || string.IsNullOrWhiteSpace(row.AccessTokenProtected))
                continue;
            try
            {
                var token = _tokens.Unprotect(row.AccessTokenProtected);
                if (!string.IsNullOrWhiteSpace(token))
                    list.Add(row.BrokerName.Trim());
            }
            catch (CryptographicException)
            {
                // ignore invalid rows for provider listing
            }
        }

        return list
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
