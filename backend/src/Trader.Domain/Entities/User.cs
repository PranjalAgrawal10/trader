using Trader.Domain.Enums;

namespace Trader.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = Trader.Domain.Enums.UserRole.Trader;
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When non-null, the user verified their email (registration flow).</summary>
    public DateTimeOffset? EmailVerifiedAtUtc { get; set; }

    /// <summary>SHA-256 (64 hex, upper) over the raw verification token; nullable when not pending.</summary>
    public string? EmailVerificationTokenHash { get; set; }

    public DateTimeOffset? EmailVerificationExpiresAtUtc { get; set; }

    /// <summary>SHA-256 hex over raw password-reset token; nullable when not pending.</summary>
    public string? PasswordResetTokenHash { get; set; }

    public DateTimeOffset? PasswordResetExpiresAtUtc { get; set; }

    /// <summary>Saved Kite instruments page chart candle interval (e.g. <c>5m</c>).</summary>
    public string? KiteInstrumentsChartInterval { get; set; }

    /// <summary>Saved chart range preset (e.g. <c>auto</c>, <c>last1d</c>).</summary>
    public string? KiteInstrumentsChartRangePreset { get; set; }

    /// <summary>Saved chart style: <c>line</c> or <c>bar</c>.</summary>
    public string? KiteInstrumentsChartGraphType { get; set; }

    /// <summary>JSON object: instrument token → visible bar count when chart zoom is applied (same keys as Kite token strings).</summary>
    public string? KiteInstrumentsChartZoomJson { get; set; }

    /// <summary>Authenticator (TOTP) enabled for this account.</summary>
    public bool TwoFactorEnabled { get; set; }

    /// <summary>Authenticator or email sign-in OTP when <see cref="TwoFactorEnabled"/>.</summary>
    public SecondFactorMethod SecondFactorMethod { get; set; }

    /// <summary>Data-protection encrypted TOTP secret (Base64 payload) when <see cref="TwoFactorEnabled"/> and method is authenticator.</summary>
    public string? TotpSecretProtected { get; set; }

    /// <summary>Pending enrollment: encrypted secret until the user confirms the first code.</summary>
    public string? TotpPendingSecretProtected { get; set; }

    /// <summary>Data-protection encrypted JSON of hashed one-time recovery codes when <see cref="TwoFactorEnabled"/>.</summary>
    public string? TotpRecoveryCodesProtected { get; set; }

    public ICollection<BrokerAccount> BrokerAccounts { get; set; } = new List<BrokerAccount>();

    public ICollection<Strategy> Strategies { get; set; } = new List<Strategy>();
    public ICollection<Bot> Bots { get; set; } = new List<Bot>();
    public ICollection<KiteFavoriteInstrument> KiteFavoriteInstruments { get; set; } = new List<KiteFavoriteInstrument>();

    public ICollection<MlPriceDirectionPrediction> MlPriceDirectionPredictions { get; set; } = new List<MlPriceDirectionPrediction>();
}
