using System.Security.Cryptography;
using System.Text;

namespace Trader.Application.Security;

/// <summary>High-entropy tokens in email links; store only SHA-256 hex in the database.</summary>
public static class OpaqueTokenHasher
{
    /// <summary>64 hex chars (256 bits of randomness).</summary>
    public static string CreateUrlToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToHexString(bytes);
    }

    public static string HashToken(string rawToken)
    {
        var bytes = Encoding.UTF8.GetBytes(rawToken.Trim());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
