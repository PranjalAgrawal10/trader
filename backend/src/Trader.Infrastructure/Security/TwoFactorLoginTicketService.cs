using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Trader.Application.Abstractions.Security;
using Trader.Application.Configuration;

namespace Trader.Infrastructure.Security;

public sealed class TwoFactorLoginTicketService : ITwoFactorLoginTicketService
{
    private readonly TimeSpan _maxAge;
    private readonly IDataProtector _protector;

    public TwoFactorLoginTicketService(
        IDataProtector protector,
        IOptions<AuthOptions> authOptions)
    {
        _protector = protector;
        var minutes = authOptions.Value.TwoFactorLoginTicketLifetimeMinutes;
        minutes = Math.Clamp(minutes, 1, 120);
        _maxAge = TimeSpan.FromMinutes(minutes);
    }

    public string CreatePendingLoginTicket(Guid userId)
    {
        var payload = $"{userId:N}|{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        return _protector.Protect(payload);
    }

    public TwoFactorPendingTicketStatus ValidatePendingLoginTicket(string ticket, out Guid userId)
    {
        userId = default;
        if (string.IsNullOrWhiteSpace(ticket))
            return TwoFactorPendingTicketStatus.Invalid;

        string payload;
        try
        {
            payload = _protector.Unprotect(ticket);
        }
        catch
        {
            return TwoFactorPendingTicketStatus.Invalid;
        }

        var parts = payload.Split('|');
        if (parts.Length != 2)
            return TwoFactorPendingTicketStatus.Invalid;
        if (!Guid.TryParse(parts[0], out userId))
            return TwoFactorPendingTicketStatus.Invalid;
        if (!long.TryParse(parts[1], out var unix))
            return TwoFactorPendingTicketStatus.Invalid;

        var created = DateTimeOffset.FromUnixTimeSeconds(unix);
        if (DateTimeOffset.UtcNow - created > _maxAge)
            return TwoFactorPendingTicketStatus.Expired;

        return TwoFactorPendingTicketStatus.Valid;
    }
}
