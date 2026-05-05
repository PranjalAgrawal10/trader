namespace Trader.Application.Configuration;

/// <summary>Application auth behaviour (not JWT — see <see cref="JwtOptions"/>).</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>How long the user may stay on the authenticator step after password login (bounded 1–120 minutes).</summary>
    public int TwoFactorLoginTicketLifetimeMinutes { get; set; } = 30;

    /// <summary>Failed TOTP or recovery verification attempts per scope before a lockout fires (clamped 1–50).</summary>
    public int MaxFailedTotpAttemptsPerScope { get; set; } = 5;

    /// <summary>Duration of lockout after too many OTP failures at a scope (minutes, clamped 1–1440).</summary>
    public int TotpAttemptLockoutMinutes { get; set; } = 15;
}
