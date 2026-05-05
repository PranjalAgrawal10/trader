namespace Trader.Application.Abstractions.Security;

/// <summary>Short-lived opaque ticket after password verification when TOTP is required.</summary>
public interface ITwoFactorLoginTicketService
{
    string CreatePendingLoginTicket(Guid userId);

    /// <summary>Validates payload signature, parse, and max age.</summary>
    TwoFactorPendingTicketStatus ValidatePendingLoginTicket(string ticket, out Guid userId);
}

public enum TwoFactorPendingTicketStatus
{
    Invalid,
    Expired,
    Valid,
}
