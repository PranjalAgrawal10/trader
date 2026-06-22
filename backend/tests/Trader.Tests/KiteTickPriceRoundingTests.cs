using Trader.Application.Broker;

namespace Trader.Tests;

public sealed class KiteTickPriceRoundingTests
{
    [Theory]
    [InlineData(100.03, 0.05, 100.05)]
    [InlineData(100.02, 0.05, 100.00)]
    [InlineData(294.614, 0.05, 294.60)]
    [InlineData(702.37, 0.05, 702.35)]
    [InlineData(25000.12, 0.05, 25000.10)]
    [InlineData(123.456, 0.10, 123.50)]
    [InlineData(45.678, 0.01, 45.68)]
    public void RoundToTickSize_snaps_to_nearest_valid_tick(decimal price, decimal tickSize, decimal expected)
    {
        var rounded = KiteTickPriceRounding.RoundToTickSize(price, tickSize);
        Assert.Equal(expected, rounded);
        Assert.Equal(0m, rounded % tickSize);
    }
}
