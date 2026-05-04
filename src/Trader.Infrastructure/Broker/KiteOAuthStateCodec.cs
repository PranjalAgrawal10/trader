using Microsoft.AspNetCore.DataProtection;
using Trader.Application.Broker;

namespace Trader.Infrastructure.Broker;

public sealed class KiteOAuthStateCodec : IKiteOAuthStateCodec
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(20);
    private readonly IDataProtector _protector;

    public KiteOAuthStateCodec(IDataProtector protector)
    {
        _protector = protector;
    }

    public string Encode(Guid userId)
    {
        var payload = $"{userId:N}|{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        return _protector.Protect(payload);
    }

    public Guid? TryDecode(string state)
    {
        if (string.IsNullOrWhiteSpace(state))
            return null;

        try
        {
            var payload = _protector.Unprotect(state);
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
        catch
        {
            return null;
        }
    }
}
