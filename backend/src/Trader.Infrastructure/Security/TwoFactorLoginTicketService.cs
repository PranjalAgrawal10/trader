using Microsoft.AspNetCore.DataProtection;
using Trader.Application.Abstractions.Security;

namespace Trader.Infrastructure.Security;

public sealed class TwoFactorLoginTicketService : ITwoFactorLoginTicketService
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);
    private readonly IDataProtector _protector;

    public TwoFactorLoginTicketService(IDataProtector protector)
    {
        _protector = protector;
    }

    public string CreatePendingLoginTicket(Guid userId)
    {
        var payload = $"{userId:N}|{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        return _protector.Protect(payload);
    }

    public bool TryValidatePendingLoginTicket(string ticket, out Guid userId)
    {
        userId = default;
        if (string.IsNullOrWhiteSpace(ticket))
            return false;

        try
        {
            var payload = _protector.Unprotect(ticket);
            var parts = payload.Split('|');
            if (parts.Length != 2)
                return false;
            if (!Guid.TryParse(parts[0], out userId))
                return false;
            if (!long.TryParse(parts[1], out var unix))
                return false;

            var created = DateTimeOffset.FromUnixTimeSeconds(unix);
            if (DateTimeOffset.UtcNow - created > MaxAge)
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }
}
