namespace Trader.Application.Broker;

public sealed record BrokerGrowwPersistRequest(
    string AccessToken,
    DateTimeOffset? TokenExpiresAt,
    string? ApiKey = null);
