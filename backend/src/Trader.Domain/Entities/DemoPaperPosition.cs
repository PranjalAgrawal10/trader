namespace Trader.Domain.Entities;

/// <summary>
/// Simulated open long size (whole contracts) for manual demo buy/sell on a locked instrument (no Kite orders).
/// </summary>
public class DemoPaperPosition
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public string InstrumentToken { get; set; } = string.Empty;

    /// <summary>Open long contracts (each contract = lot size × underlying unit at trade price).</summary>
    public int OpenContracts { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
