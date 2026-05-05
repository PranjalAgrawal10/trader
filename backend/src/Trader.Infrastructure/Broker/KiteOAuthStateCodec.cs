using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Trader.Application.Broker;
using Trader.Application.Configuration;

namespace Trader.Infrastructure.Broker;

/// <summary>
/// HMAC-signed OAuth state so Kite login survives ASP.NET Data Protection key rotation (e.g. ephemeral DP in Production).
/// Uses the same secret material as JWT (<see cref="JwtOptions.Key"/>); encrypted Kite session blobs in the DB still require a stable DP key ring.
/// </summary>
public sealed class KiteOAuthStateCodec : IKiteOAuthStateCodec
{
    private const int MaxStateChars = 512;
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(20);
    private readonly byte[] _hmacKey;

    public KiteOAuthStateCodec(IOptions<JwtOptions> jwtOptions)
    {
        var key = jwtOptions.Value.Key ?? string.Empty;
        if (string.IsNullOrEmpty(key))
            throw new InvalidOperationException("Jwt:Key is required for Kite OAuth state signing.");

        _hmacKey = DeriveHmacKey(key);
    }

    /// <summary>Use SHA-256 when the configured key is shorter than 32 UTF-8 bytes (local dev convenience).</summary>
    private static byte[] DeriveHmacKey(string jwtKeyUtf8)
    {
        var bytes = Encoding.UTF8.GetBytes(jwtKeyUtf8);
        return bytes.Length >= 32 ? bytes : SHA256.HashData(bytes);
    }

    public string Encode(Guid userId)
    {
        var payload = $"{userId:N}|{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var sig = Sign(payloadBytes);
        return $"{ToUrlBase64(payloadBytes)}.{ToUrlBase64(sig)}";
    }

    public Guid? TryDecode(string state)
    {
        if (string.IsNullOrWhiteSpace(state))
            return null;

        var trimmed = state.Trim();
        if (trimmed.Length > MaxStateChars)
            return null;

        var dot = trimmed.IndexOf('.');
        if (dot <= 0 || dot == trimmed.Length - 1)
            return null;

        byte[] payloadBytes;
        byte[] sigBytes;
        try
        {
            payloadBytes = FromUrlBase64(trimmed[..dot]);
            sigBytes = FromUrlBase64(trimmed[(dot + 1)..]);
        }
        catch
        {
            return null;
        }

        if (sigBytes.Length != 32)
            return null;

        var expectedSig = Sign(payloadBytes);
        if (!CryptographicOperations.FixedTimeEquals(expectedSig, sigBytes))
            return null;

        var payload = Encoding.UTF8.GetString(payloadBytes);
        var parts = payload.Split('|');
        if (parts.Length != 2)
            return null;
        if (!Guid.TryParse(parts[0], out var userId))
            return null;
        if (!long.TryParse(parts[1], out var unix))
            return null;

        var created = DateTimeOffset.FromUnixTimeSeconds(unix);
        if (DateTimeOffset.UtcNow - created > MaxAge)
            return null;

        return userId;
    }

    private byte[] Sign(byte[] payloadBytes)
    {
        using var hmac = new HMACSHA256(_hmacKey);
        return hmac.ComputeHash(payloadBytes);
    }

    private static string ToUrlBase64(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromUrlBase64(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }

        return Convert.FromBase64String(s);
    }
}
