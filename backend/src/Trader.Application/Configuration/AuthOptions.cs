namespace Trader.Application.Configuration;

/// <summary>Application auth behaviour (not JWT — see <see cref="JwtOptions"/>).</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>How long the user may stay on the authenticator step after password login (bounded 1–120 minutes).</summary>
    public int TwoFactorLoginTicketLifetimeMinutes { get; set; } = 30;
}
