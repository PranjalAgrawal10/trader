namespace Trader.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = Trader.Domain.Enums.UserRole.Trader;
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When set, the user completed broker onboarding.</summary>
    public DateTimeOffset? BrokerConnectedAt { get; set; }

    /// <summary>e.g. "Zerodha" when linked via Kite Connect.</summary>
    public string? BrokerProvider { get; set; }

    /// <summary>Data-protection encrypted Kite access_token.</summary>
    public string? KiteAccessTokenProtected { get; set; }

    /// <summary>Data-protection encrypted Kite refresh_token.</summary>
    public string? KiteRefreshTokenProtected { get; set; }

    /// <summary>Zerodha user id from Kite session.</summary>
    public string? KiteUserId { get; set; }

    /// <summary>Authenticator (TOTP) enabled for this account.</summary>
    public bool TwoFactorEnabled { get; set; }

    /// <summary>Data-protection encrypted TOTP secret (Base64 payload) when <see cref="TwoFactorEnabled"/>.</summary>
    public string? TotpSecretProtected { get; set; }

    /// <summary>Pending enrollment: encrypted secret until the user confirms the first code.</summary>
    public string? TotpPendingSecretProtected { get; set; }

    public void MarkBrokerConnected(DateTimeOffset connectedAt) =>
        BrokerConnectedAt = connectedAt;

    public ICollection<Strategy> Strategies { get; set; } = new List<Strategy>();
    public ICollection<Bot> Bots { get; set; } = new List<Bot>();
}
