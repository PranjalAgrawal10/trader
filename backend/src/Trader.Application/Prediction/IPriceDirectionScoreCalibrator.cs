namespace Trader.Application.Prediction;

/// <summary>Maps raw classifier probabilities (e.g. ML.NET) to calibrated values in [0,1]; identity when unconfigured.</summary>
public interface IPriceDirectionScoreCalibrator
{
    float CalibratePUp(float rawPUp);
}
