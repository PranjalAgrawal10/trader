using Trader.Domain.Enums;

namespace Trader.Domain.Entities;

public class TradingOrder
{
    public Guid Id { get; set; }
    public Guid BotId { get; set; }
    public Bot Bot { get; set; } = null!;
    public string ExternalId { get; set; } = string.Empty;
    public OrderStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
