using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Trader.Application.Abstractions.Security;

namespace Trader.Infrastructure.Security;

public sealed class TwoFactorRecoveryCodesHelper : ITwoFactorRecoveryCodesHelper
{
    private const int CodeCount = 10;
    private readonly IDataProtector _protector;

    public TwoFactorRecoveryCodesHelper(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("Trader.Security.TotpRecoveryCodes");
    }

    public (IReadOnlyList<string> PlaintextCodes, string ProtectedPayload) IssueNewProtectedPayload()
    {
        var hashes = new List<string>(CodeCount);
        var plaintext = new List<string>(CodeCount);
        Span<byte> bytes = stackalloc byte[6];
        for (var i = 0; i < CodeCount; i++)
        {
            RandomNumberGenerator.Fill(bytes);
            var raw = Convert.ToHexString(bytes).ToUpperInvariant();
            var display = $"{raw[..4]}-{raw[4..]}";
            plaintext.Add(display);
            hashes.Add(ToStorageHash(NormalizeCode(display)));
        }

        var json = JsonSerializer.Serialize(hashes);
        return (plaintext, _protector.Protect(json));
    }

    public bool TryConsumeOne(string? storedProtectedPayload, string enteredCode, out string? updatedProtectedPayloadOrNullWhenCleared)
    {
        updatedProtectedPayloadOrNullWhenCleared = null;

        if (string.IsNullOrWhiteSpace(storedProtectedPayload))
            return false;

        var normalizedEntered = NormalizeCode(enteredCode);
        if (normalizedEntered.Length == 0)
            return false;

        string json;
        try
        {
            json = _protector.Unprotect(storedProtectedPayload);
        }
        catch
        {
            return false;
        }

        List<string>? hashes;
        try
        {
            hashes = JsonSerializer.Deserialize<List<string>>(json);
        }
        catch
        {
            return false;
        }

        if (hashes is null || hashes.Count == 0)
            return false;

        var needle = ToStorageHash(normalizedEntered);
        var removed = hashes.RemoveAll(h => string.Equals(h, needle, StringComparison.Ordinal)) > 0;
        if (!removed)
            return false;

        updatedProtectedPayloadOrNullWhenCleared = hashes.Count == 0 ? null : _protector.Protect(JsonSerializer.Serialize(hashes));
        return true;
    }

    private static string NormalizeCode(string raw)
    {
        Span<char> buffer = stackalloc char[Math.Min(raw.Length * 2, 96)];
        var n = 0;
        foreach (var c in raw.Trim())
        {
            if (c is '-' or ' ' or '\t')
                continue;
            if (!char.IsAsciiLetterOrDigit(c))
                continue;
            buffer[n++] = char.ToUpperInvariant(c);
        }

        return n == 0 ? string.Empty : new string(buffer[..n]);
    }

    private static string ToStorageHash(string normalizedCode)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedCode));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
