namespace Trader.Application.Broker;

/// <summary>Encodes the signed user id passed as Kite OAuth <c>state</c>.</summary>
public interface IKiteOAuthStateCodec
{
    string Encode(Guid userId);

    /// <summary>Returns null if tampered, expired, or malformed.</summary>
    Guid? TryDecode(string state);
}
