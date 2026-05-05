using Microsoft.AspNetCore.DataProtection;
using OtpNet;
using Trader.Application.Abstractions.Security;

namespace Trader.Infrastructure.Security;

public sealed class TwoFactorTotpHelper : ITwoFactorTotpHelper
{
    private readonly IDataProtector _secretProtector;

    public TwoFactorTotpHelper(IDataProtector secretProtector)
    {
        _secretProtector = secretProtector;
    }

    public byte[] GenerateSecretKey() => KeyGeneration.GenerateRandomKey(20);

    public string ProtectSecret(byte[] secret)
    {
        var b64 = Convert.ToBase64String(secret);
        return _secretProtector.Protect(b64);
    }

    public byte[] UnprotectSecret(string protectedBase64)
    {
        var b64 = _secretProtector.Unprotect(protectedBase64);
        return Convert.FromBase64String(b64);
    }

    public bool VerifyCode(byte[] secretKey, string sixDigitCode)
    {
        if (string.IsNullOrWhiteSpace(sixDigitCode))
            return false;

        var normalized = sixDigitCode.Trim().Replace(" ", "", StringComparison.Ordinal);
        if (normalized.Length != 6 || !normalized.All(char.IsDigit))
            return false;

        var totp = new Totp(secretKey);
        // ±1 step (~30s) per TOTP best practice; small clock drift only.
        return totp.VerifyTotp(normalized, out _, new VerificationWindow(previous: 1, future: 1));
    }

    public string BuildOtpAuthUri(string accountEmail, byte[] secretKey, string issuer)
    {
        var label = $"{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(accountEmail)}";
        var secret = Base32Encoding.ToString(secretKey);
        return
            $"otpauth://totp/{label}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits=6&period=30";
    }

    public string ToBase32Key(byte[] secretKey) => Base32Encoding.ToString(secretKey);
}
