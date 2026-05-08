namespace Trader.Domain.Entities;

/// <summary>
/// Kite instruments the user marked locked for trading (persisted watchlist separate from favorites).
/// </summary>
public class KiteTradingLockInstrument
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public string InstrumentToken { get; set; } = string.Empty;
    public string Tradingsymbol { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? InstrumentType { get; set; }
    public string? Segment { get; set; }
    public string? Expiry { get; set; }
    public decimal? Strike { get; set; }
    public int? LotSize { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
