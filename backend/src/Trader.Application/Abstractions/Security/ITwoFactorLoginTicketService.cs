namespace Trader.Application.Abstractions.Security;

/// <summary>Short-lived opaque ticket after password verification when TOTP is required.</summary>
public interface ITwoFactorLoginTicketService
{
    string CreatePendingLoginTicket(Guid userId);

    bool TryValidatePendingLoginTicket(string ticket, out Guid userId);
}
