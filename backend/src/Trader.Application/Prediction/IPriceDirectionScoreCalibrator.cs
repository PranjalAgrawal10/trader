namespace Trader.Application.Prediction;

/// <summary>Maps raw classifier probabilities (e.g. ML.NET) to calibrated values in [0,1]; identity when unconfigured.</summary>
public interface IPriceDirectionScoreCalibrator
{
    float CalibratePUp(float rawPUp);

    /// <summary>
    /// Calibrates with an optional profile key (for example interval profile <c>1m</c>/<c>5m</c>/<c>15m</c>).
    /// Implementations may fall back to global calibration when key-specific maps are absent.
    /// </summary>
    float CalibratePUp(float rawPUp, string? profileKey);
}
