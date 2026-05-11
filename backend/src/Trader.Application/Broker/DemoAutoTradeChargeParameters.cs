namespace Trader.Application.Broker;

/// <summary>Optional round-trip fee parameters passed into <see cref="DemoAutoTradeEodSummaryCalculator.Compute"/>.</summary>
public readonly record struct DemoAutoTradeChargeParameters(
    bool ApplyRoundTripCosts,
    decimal RoundTripFlatInrPerLeg,
    decimal RoundTripTurnoverBps);
