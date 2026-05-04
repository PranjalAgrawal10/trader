namespace Trader.Application.Broker;

public interface IKiteSessionExchange
{
    Task<KiteSessionExchangeResult> ExchangeAsync(string requestToken, CancellationToken ct);
}

public sealed record KiteSessionExchangeResult(
    bool Success,
    string? ErrorMessage,
    string? AccessToken,
    string? RefreshToken,
    string? KiteUserId);
