namespace Trader.Application.Prediction;

/// <summary>Where a persisted price-direction prediction row was stored (separate EF tables).</summary>
public enum MlPredictionPersistenceKind
{
    None,

    ClassicPriceDirection,

    LightGbmTripleBarrier,
}
