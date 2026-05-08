namespace Trader.Application.Configuration;

/// <summary>Application auth behaviour (not JWT — see <see cref="JwtOptions"/>).</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>How long the user may stay on the authenticator step after password login (bounded 1–120 minutes).</summary>
    public int TwoFactorLoginTicketLifetimeMinutes { get; set; } = 30;

    /// <summary>Registration email verification link TTL (hours, clamped 1–168).</summary>
    public int EmailVerificationExpiryHours { get; set; } = 48;

    /// <summary>Forgotten-password reset link TTL (hours, clamped 1–168).</summary>
    public int PasswordResetExpiryHours { get; set; } = 48;
}
