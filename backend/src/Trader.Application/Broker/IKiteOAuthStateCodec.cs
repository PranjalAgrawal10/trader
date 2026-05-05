namespace Trader.Application.Broker;

/// <summary>HMAC-signed user id passed as Kite OAuth <c>state</c> (stable across DP key rotation; uses <c>Jwt:Key</c>).</summary>
public interface IKiteOAuthStateCodec
{
    string Encode(Guid userId);

    /// <summary>Returns null if tampered, expired, or malformed.</summary>
    Guid? TryDecode(string state);
}
