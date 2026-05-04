using Trader.Domain.Enums;

namespace Trader.Domain.Entities;

public class Bot
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid? StrategyId { get; set; }
    public Strategy? Strategy { get; set; }
    public BotStatus Status { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? StoppedAt { get; set; }

    public ICollection<Trade> Trades { get; set; } = new List<Trade>();
    public ICollection<TradingOrder> Orders { get; set; } = new List<TradingOrder>();
}
