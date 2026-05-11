using Trader.Application.Broker;

namespace Trader.Application.Configuration;

/// <summary>Host settings for hypothetical demo P&amp;L (no real orders).</summary>
public sealed class DemoAutoTradeOptions
{
    public const string SectionName = "DemoAutoTrade";

    /// <summary>Approximate F&amp;O-style round-trip costs per allocated leg.</summary>
    public DemoAutoTradeChargesOptions Charges { get; set; } = new();
}

/// <summary>
/// Simplified charge model: flat INR per directional leg that receives notional (e.g. buy+sell brokerage cap)
/// plus turnover bps on allocated notional (exchange/regulatory drag). Tuned via config, not per-instrument.
/// </summary>
public sealed class DemoAutoTradeChargesOptions
{
    /// <summary>When true, <see cref="DemoAutoTradeEodSummaryCalculator"/> subtracts fees from gross P&amp;L.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Fixed round-trip cost per leg that gets allocation (INR), e.g. discount-broker cap both sides.</summary>
    public decimal RoundTripFlatInrPerLeg { get; set; } = 40m;

    /// <summary>Additional cost as basis points on absolute allocated notional per leg (combined both sides).</summary>
    public decimal RoundTripTurnoverBps { get; set; } = 2m;
}

public static class DemoAutoTradeOptionsChargeResolver
{
    /// <summary>Builds calculator charge args from host options; <c>null</c> means gross = net.</summary>
    public static DemoAutoTradeChargeParameters? Resolve(DemoAutoTradeOptions? options)
    {
        if (options?.Charges is not { Enabled: true } c)
            return null;

        return new DemoAutoTradeChargeParameters(
            ApplyRoundTripCosts: true,
            RoundTripFlatInrPerLeg: Math.Max(0m, c.RoundTripFlatInrPerLeg),
            RoundTripTurnoverBps: Math.Max(0m, c.RoundTripTurnoverBps));
    }
}
