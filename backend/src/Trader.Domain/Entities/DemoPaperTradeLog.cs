namespace Trader.Domain.Entities;

/// <summary>
/// Append-only log row for each manual demo paper buy/sell (wallet + contracts at Kite LTP).
/// </summary>
public class DemoPaperTradeLog
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public string InstrumentToken { get; set; } = string.Empty;
    public string Tradingsymbol { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;

    /// <summary><c>buy</c> or <c>sell</c>.</summary>
    public string Side { get; set; } = string.Empty;

    public int Contracts { get; set; }

    public decimal LastPrice { get; set; }

    public int LotSize { get; set; }

    /// <summary>Negative on buy (cost), positive on sell (credit).</summary>
    public decimal CashFlowInr { get; set; }

    public decimal WalletBalanceAfter { get; set; }

    public int OpenContractsAfter { get; set; }

    public DateTimeOffset ExecutedAtUtc { get; set; }
}
