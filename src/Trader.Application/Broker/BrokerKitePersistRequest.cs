namespace Trader.Application.Broker;

public sealed record BrokerKitePersistRequest(string AccessToken, string? RefreshToken, string KiteUserId);
