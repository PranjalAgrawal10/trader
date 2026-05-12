namespace Trader.Domain.Entities;

/// <summary>
/// One manual demo paper BUY lot (whole contracts); remaining size is consumed FIFO when the user SELLS.
/// Used to annotate charts with vertical lines until the corresponding contracts are flat.
/// </summary>
public class DemoPaperBuyLeg
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public string InstrumentToken { get; set; } = string.Empty;

    /// <summary>Contracts left from this buy; reduced on sell (FIFO).</summary>
    public int ContractsRemaining { get; set; }

    public DateTimeOffset BoughtAtUtc { get; set; }
}
