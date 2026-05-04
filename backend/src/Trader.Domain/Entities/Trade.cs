using Trader.Domain.Enums;

namespace Trader.Domain.Entities;

public class Trade
{
    public Guid Id { get; set; }
    public Guid BotId { get; set; }
    public Bot Bot { get; set; } = null!;
    public string Symbol { get; set; } = string.Empty;
    public TradeSide Side { get; set; }
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal? RealizedPnl { get; set; }
    public DateTimeOffset ExecutedAt { get; set; }
}
