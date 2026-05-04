using Trader.Application.Abstractions.Persistence;
using Trader.Domain.Entities;

namespace Trader.Application.Strategies;

public sealed class StrategyService : IStrategyService
{
    private readonly IStrategyRepository _repository;

    public StrategyService(IStrategyRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<StrategyResponse>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        var items = await _repository.ListByUserAsync(userId, ct);
        return items.Select(Map).ToList();
    }

    public async Task<StrategyResponse?> GetAsync(Guid userId, Guid strategyId, CancellationToken ct = default)
    {
        var entity = await _repository.GetAsync(strategyId, ct);
        if (entity is null || entity.UserId != userId)
            return null;
        return Map(entity);
    }

    public async Task<StrategyResponse> CreateAsync(Guid userId, CreateStrategyRequest request, CancellationToken ct = default)
    {
        var entity = new Strategy
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = request.Name.Trim(),
            ParametersJson = string.IsNullOrWhiteSpace(request.ParametersJson) ? "{}" : request.ParametersJson.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _repository.AddAsync(entity, ct);
        await _repository.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task<StrategyResponse?> UpdateAsync(Guid userId, Guid strategyId, UpdateStrategyRequest request, CancellationToken ct = default)
    {
        var entity = await _repository.GetAsync(strategyId, ct);
        if (entity is null || entity.UserId != userId)
            return null;

        entity.Name = request.Name.Trim();
        entity.ParametersJson = string.IsNullOrWhiteSpace(request.ParametersJson) ? "{}" : request.ParametersJson.Trim();
        await _repository.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid strategyId, CancellationToken ct = default)
    {
        var entity = await _repository.GetAsync(strategyId, ct);
        if (entity is null || entity.UserId != userId)
            return false;

        _repository.Remove(entity);
        await _repository.SaveChangesAsync(ct);
        return true;
    }

    private static StrategyResponse Map(Strategy s) =>
        new(s.Id, s.Name, s.ParametersJson, s.CreatedAt);
}
