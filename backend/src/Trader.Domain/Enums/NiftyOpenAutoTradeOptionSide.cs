namespace Trader.Domain.Enums;

/// <summary>Option leg for NIFTY open auto-trade (always a BUY).</summary>
public enum NiftyOpenAutoTradeOptionSide : byte
{
    Ce = 0,
    Pe = 1,
}
