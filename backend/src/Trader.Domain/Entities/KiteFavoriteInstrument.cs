namespace Trader.Domain.Entities;

/// <summary>Saved Kite instrument row for quick access (F&amp;O / MCX contracts).</summary>
public class KiteFavoriteInstrument
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
