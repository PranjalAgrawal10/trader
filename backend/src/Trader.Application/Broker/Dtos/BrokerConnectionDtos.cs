namespace Trader.Application.Broker;

public sealed record BrokerStatusDto(bool Connected, DateTimeOffset? ConnectedAt, string? Provider);

public sealed record BrokerProviderAvailabilityDto(string Key, string Label, bool Connected);

public sealed record KiteLoginUrlDto(string LoginUrl);

public sealed class GrowwConnectRequestDto
{
    public string? AccessToken { get; set; }
    public string? ApiKey { get; set; }
    public string? ApiSecret { get; set; }
    public string? Totp { get; set; }
}

public sealed class BrokerSelectionPutDto
{
    public string? Broker { get; set; }
}

/// <summary>
/// <see cref="LoginUrl"/> goes to the client; <see cref="PendingOAuthStateKey"/> mirrors the OAuth <c>state</c> query (short server-side key) for the HttpOnly cookie fallback.
/// </summary>
public sealed record KiteLoginUrlBuildResult(string LoginUrl, string PendingOAuthStateKey);
