using Trader.Domain.Enums;

namespace Trader.Domain.Entities;

/// <summary>Holds one email-delivered OTP challenge (BCrypt-hashed code, expiry, verification attempts).</summary>
public sealed class EmailOtpChallenge
{
    public Guid Id { get; set; }
    public string NormalizedEmail { get; set; } = string.Empty;
    public EmailOtpPurpose Purpose { get; set; }
    public string OtpHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public bool IsConsumed { get; set; }
    public int FailedVerifyAttempts { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
