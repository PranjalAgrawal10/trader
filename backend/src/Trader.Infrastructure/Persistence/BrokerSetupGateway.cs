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
    private const string GrowwBrokerName = "Groww";

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

        var accounts = await _db.BrokerAccounts.AsNoTracking()
            .Where(a => a.UserId == userId && !string.IsNullOrEmpty(a.AccessTokenProtected))
            .OrderByDescending(a => a.ConnectedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (accounts.Count == 0)
            return new BrokerSetupSnapshot(userId, null, null);

        var staleIds = new List<Guid>();
        foreach (var account in accounts)
        {
            if (string.IsNullOrWhiteSpace(account.AccessTokenProtected) || string.IsNullOrWhiteSpace(account.BrokerName))
            {
                staleIds.Add(account.Id);
                continue;
            }

            try
            {
                var access = _tokens.Unprotect(account.AccessTokenProtected);
                if (!string.IsNullOrWhiteSpace(access))
                    return new BrokerSetupSnapshot(userId, account.ConnectedAt, account.BrokerName.Trim());
            }
            catch (CryptographicException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Broker token decrypt failed for user {UserId} provider {Provider}; removing stale row.",
                    userId,
                    account.BrokerName);
            }

            staleIds.Add(account.Id);
        }

        if (staleIds.Count > 0)
            await RemoveStaleAccountsByIdsAsync(staleIds, ct).ConfigureAwait(false);

        return new BrokerSetupSnapshot(userId, null, null);
    }

    /// <summary>Deletes encrypted broker rows when ciphertext no longer decrypts so status and UX match API reality.</summary>
    private async Task RemoveStaleAccountsByIdsAsync(IReadOnlyList<Guid> accountIds, CancellationToken ct)
    {
        var rows = await _db.BrokerAccounts
            .Where(a => accountIds.Contains(a.Id))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (rows.Count == 0)
            return;

        _db.BrokerAccounts.RemoveRange(rows);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task CompleteBrokerSetupAsync(Guid userId, CancellationToken ct)
    {
        var accounts = await _db.BrokerAccounts
            .Where(a => a.UserId == userId && !string.IsNullOrEmpty(a.AccessTokenProtected))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (accounts.Count == 0)
            return;

        var now = DateTimeOffset.UtcNow;
        foreach (var account in accounts)
            account.ConnectedAt = now;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
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

    public async Task PersistGrowwSessionAsync(Guid userId, BrokerGrowwPersistRequest session, CancellationToken ct)
    {
        _ = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new InvalidOperationException("User not found.");

        var account = await _db.BrokerAccounts
            .FirstOrDefaultAsync(a => a.UserId == userId && a.BrokerName == GrowwBrokerName, ct);

        var now = DateTimeOffset.UtcNow;
        if (account is null)
        {
            account = new BrokerAccount
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                BrokerName = GrowwBrokerName,
                ConnectedAt = now,
            };
            _db.BrokerAccounts.Add(account);
        }

        account.ApiKey = string.IsNullOrWhiteSpace(session.ApiKey) ? null : session.ApiKey.Trim();
        account.AccessTokenProtected = _tokens.Protect(session.AccessToken);
        account.RefreshTokenProtected = null;
        account.TokenExpiresAt = session.TokenExpiresAt;
        account.ConnectedAt = now;

        await _db.SaveChangesAsync(ct);
    }

    public async Task DisconnectBrokerAsync(Guid userId, string? brokerName, CancellationToken ct)
    {
        var broker = string.IsNullOrWhiteSpace(brokerName) ? null : brokerName.Trim().ToLowerInvariant();
        var rows = await _db.BrokerAccounts
            .Where(a => a.UserId == userId && (broker == null || a.BrokerName.ToLower() == broker))
            .ToListAsync(ct);

        if (rows.Count > 0)
        {
            _db.BrokerAccounts.RemoveRange(rows);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<bool> SetActiveBrokerAsync(Guid userId, string brokerName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(brokerName))
            return false;

        var broker = brokerName.Trim().ToLowerInvariant();
        var rows = await _db.BrokerAccounts
            .Where(a => a.UserId == userId && !string.IsNullOrEmpty(a.AccessTokenProtected))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (rows.Count == 0)
            return false;

        BrokerAccount? selected = null;
        foreach (var row in rows)
        {
            try
            {
                var token = _tokens.Unprotect(row.AccessTokenProtected!);
                if (string.IsNullOrWhiteSpace(token))
                    continue;
                if (row.BrokerName.Trim().ToLowerInvariant() == broker)
                    selected = row;
            }
            catch (CryptographicException)
            {
                // ignore stale encrypted rows here; status cleanup handles them.
            }
        }

        if (selected is null)
            return false;

        selected.ConnectedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task<string?> GetKiteAccessTokenAsync(Guid userId, CancellationToken ct)
    {
        return await GetBrokerAccessTokenAsync(userId, ZerodhaBrokerName, ct).ConfigureAwait(false);
    }

    public async Task<string?> GetBrokerAccessTokenAsync(Guid userId, string brokerName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(brokerName))
            return null;

        var broker = brokerName.Trim().ToLowerInvariant();
        var blob = await _db.BrokerAccounts.AsNoTracking()
            .Where(a => a.UserId == userId && a.BrokerName.ToLower() == broker)
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
