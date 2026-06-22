namespace Trader.Application.Broker;

/// <summary>
/// Kite rejects order/GTT prices that are not multiples of the instrument <c>tick_size</c> (instruments master CSV).
/// </summary>
public static class KiteTickPriceRounding
{
    /// <summary><c>price = tick_size × round(price / tick_size)</c></summary>
    public static decimal RoundToTickSize(decimal price, decimal tickSize)
    {
        if (price <= 0)
            return price;
        if (tickSize <= 0)
            return Math.Round(price, 2, MidpointRounding.AwayFromZero);

        var ticks = Math.Round(price / tickSize, 0, MidpointRounding.AwayFromZero);
        return ticks * tickSize;
    }
}
