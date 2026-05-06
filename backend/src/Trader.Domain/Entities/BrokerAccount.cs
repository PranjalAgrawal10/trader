namespace Trader.Domain.Entities;

/// <summary>Linked broker credentials per user (e.g. Zerodha Kite session tokens).</summary>
public class BrokerAccount
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    /// <summary>Broker integration name (e.g. <c>Zerodha</c>).</summary>
    public string BrokerName { get; set; } = string.Empty;

    /// <summary>Optional API key when a broker supports bring-your-own-key; Kite uses the app-level key when null.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Data-protection encrypted access token (Kite <c>access_token</c>).</summary>
    public string? AccessTokenProtected { get; set; }

    /// <summary>Data-protection encrypted refresh token when provided.</summary>
    public string? RefreshTokenProtected { get; set; }

    /// <summary>Access-token expiry when the broker exposes it (nullable for Kite session response today).</summary>
    public DateTimeOffset? TokenExpiresAt { get; set; }

    /// <summary>Broker-side account id (e.g. Kite <c>user_id</c>).</summary>
    public string? ExternalUserId { get; set; }

    /// <summary>When the user last completed broker linking for this row.</summary>
    public DateTimeOffset? ConnectedAt { get; set; }
}
