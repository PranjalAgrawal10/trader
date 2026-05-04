namespace Trader.Application.Strategies;

public interface IStrategyService
{
    Task<IReadOnlyList<StrategyResponse>> ListAsync(Guid userId, CancellationToken ct = default);
    Task<StrategyResponse?> GetAsync(Guid userId, Guid strategyId, CancellationToken ct = default);
    Task<StrategyResponse> CreateAsync(Guid userId, CreateStrategyRequest request, CancellationToken ct = default);
    Task<StrategyResponse?> UpdateAsync(Guid userId, Guid strategyId, UpdateStrategyRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid userId, Guid strategyId, CancellationToken ct = default);
}
