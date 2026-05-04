using Trader.Application.Abstractions.Persistence;
using Trader.Domain.Entities;
using Trader.Domain.Enums;

namespace Trader.Application.Bots;

public sealed class BotService : IBotService
{
    private readonly IBotRepository _bots;
    private readonly IStrategyRepository _strategies;

    public BotService(IBotRepository bots, IStrategyRepository strategies)
    {
        _bots = bots;
        _strategies = strategies;
    }

    public async Task<IReadOnlyList<BotResponse>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        var list = await _bots.ListByUserAsync(userId, ct);
        return list.Select(Map).ToList();
    }

    public async Task<BotResponse?> GetAsync(Guid userId, Guid botId, CancellationToken ct = default)
    {
        var bot = await _bots.GetAsync(botId, ct);
        if (bot is null || bot.UserId != userId)
            return null;
        return Map(bot);
    }

    public async Task<BotResponse> CreateAsync(Guid userId, CreateBotRequest request, CancellationToken ct = default)
    {
        if (request.StrategyId is Guid sid)
        {
            var strat = await _strategies.GetAsync(sid, ct);
            if (strat is null || strat.UserId != userId)
                throw new InvalidOperationException("Strategy not found.");
        }

        var bot = new Bot
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            StrategyId = request.StrategyId,
            Status = BotStatus.Stopped,
            StartedAt = null,
            StoppedAt = null
        };

        await _bots.AddAsync(bot, ct);
        await _bots.SaveChangesAsync(ct);
        return Map(bot);
    }

    public async Task<BotResponse?> AssignStrategyAsync(Guid userId, Guid botId, Guid strategyId, CancellationToken ct = default)
    {
        var bot = await _bots.GetAsync(botId, ct);
        if (bot is null || bot.UserId != userId)
            return null;

        var strat = await _strategies.GetAsync(strategyId, ct);
        if (strat is null || strat.UserId != userId)
            throw new InvalidOperationException("Strategy not found.");

        bot.StrategyId = strategyId;
        await _bots.SaveChangesAsync(ct);
        return Map(bot);
    }

    public async Task<BotResponse?> StartAsync(Guid userId, Guid botId, CancellationToken ct = default)
    {
        var bot = await _bots.GetAsync(botId, ct);
        if (bot is null || bot.UserId != userId)
            return null;

        if (bot.StrategyId is null)
            throw new InvalidOperationException("Assign a strategy before starting the bot.");

        bot.Status = BotStatus.Running;
        bot.StartedAt = DateTimeOffset.UtcNow;
        bot.StoppedAt = null;
        await _bots.SaveChangesAsync(ct);
        return Map(bot);
    }

    public async Task<BotResponse?> StopAsync(Guid userId, Guid botId, CancellationToken ct = default)
    {
        var bot = await _bots.GetAsync(botId, ct);
        if (bot is null || bot.UserId != userId)
            return null;

        bot.Status = BotStatus.Stopped;
        bot.StoppedAt = DateTimeOffset.UtcNow;
        await _bots.SaveChangesAsync(ct);
        return Map(bot);
    }

    private static BotResponse Map(Bot b) =>
        new(b.Id, b.StrategyId, b.Status, b.StartedAt, b.StoppedAt);
}
