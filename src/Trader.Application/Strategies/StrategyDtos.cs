namespace Trader.Application.Strategies;

public sealed record StrategyResponse(Guid Id, string Name, string ParametersJson, DateTimeOffset CreatedAt);

public sealed record CreateStrategyRequest(string Name, string ParametersJson);

public sealed record UpdateStrategyRequest(string Name, string ParametersJson);
