namespace Trader.Application.Prediction;

/// <summary>Stable <see cref="IPriceDirectionPredictionEngine.ModelId"/> values used for branching (storage, UI).</summary>
public static class PriceDirectionModelIds
{
    public const string MlNetLightGbmTripleBarrierV1 = "mlnet-lightgbm-triple-barrier-v1";
    public const string MlNetFnoMultiHorizonV1 = "mlnet-fno-multi-horizon-v1";
}
