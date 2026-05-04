namespace Trader.Application.Abstractions.Security;

/// <summary>TOTP secret generation, protection at rest, and RFC 6238 verification (authenticator apps).</summary>
public interface ITwoFactorTotpHelper
{
    byte[] GenerateSecretKey();

    string ProtectSecret(byte[] secret);

    byte[] UnprotectSecret(string protectedBase64);

    bool VerifyCode(byte[] secretKey, string sixDigitCode);

    string BuildOtpAuthUri(string accountEmail, byte[] secretKey, string issuer);

    /// <summary>Base32 secret for manual entry in an authenticator app.</summary>
    string ToBase32Key(byte[] secretKey);
}
