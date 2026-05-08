namespace Trader.Application.Configuration;

public sealed class EmailOtpOptions
{
    public const string SectionName = "EmailOtp";

    /// <summary>OTP validity window (minutes).</summary>
    public int ExpiryMinutes { get; set; } = 5;

    /// <summary>After this many wrong OTP entries, the challenge is rejected until a new OTP is requested.</summary>
    public int MaxFailedVerifyAttemptsPerChallenge { get; set; } = 5;
}
