namespace Trader.Application.Bots;

public interface IBotService
{
    Task<IReadOnlyList<BotResponse>> ListAsync(Guid userId, CancellationToken ct = default);
    Task<BotResponse?> GetAsync(Guid userId, Guid botId, CancellationToken ct = default);
    Task<BotResponse> CreateAsync(Guid userId, CreateBotRequest request, CancellationToken ct = default);
    Task<BotResponse?> AssignStrategyAsync(Guid userId, Guid botId, Guid strategyId, CancellationToken ct = default);
    Task<BotResponse?> StartAsync(Guid userId, Guid botId, CancellationToken ct = default);
    Task<BotResponse?> StopAsync(Guid userId, Guid botId, CancellationToken ct = default);
}
