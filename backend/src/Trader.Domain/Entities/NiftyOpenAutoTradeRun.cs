namespace Trader.Domain.Entities;

/// <summary>Audit row for one NIFTY market-open auto-trade attempt (at most one meaningful run per user per IST session day).</summary>
public class NiftyOpenAutoTradeRun
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Calendar date in Asia/Kolkata when the 09:15 fire window applied.</summary>
    public DateOnly SessionDateIst { get; set; }

    /// <summary><c>success</c>, <c>skipped</c>, or <c>failed</c>.</summary>
    public string Status { get; set; } = "failed";

    /// <summary><c>CE</c> or <c>PE</c>.</summary>
    public string OptionSide { get; set; } = "CE";

    public string? Exchange { get; set; }
    public string? Tradingsymbol { get; set; }
    public decimal? Strike { get; set; }
    public string? Expiry { get; set; }
    public int Lots { get; set; }
    public int Quantity { get; set; }
    public decimal? OptionLtp { get; set; }
    public decimal? SpotLtp { get; set; }
    public decimal? AvailableBalanceInr { get; set; }
    public string? OrderId { get; set; }
    public string? GttTriggerId { get; set; }
    public string? Message { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
