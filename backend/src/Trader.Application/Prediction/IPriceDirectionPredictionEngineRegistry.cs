namespace Trader.Application.Prediction;

/// <summary>
/// Resolves a concrete <see cref="IPriceDirectionPredictionEngine"/> by model id and lists available models for API/UI.
/// </summary>
public interface IPriceDirectionPredictionEngineRegistry
{
    /// <summary>Value used when the caller passes null, empty, or whitespace for model id.</summary>
    string DefaultModelId { get; }

    /// <exception cref="InvalidOperationException">Unknown model id.</exception>
    IPriceDirectionPredictionEngine Resolve(string? modelId);

    IReadOnlyList<PriceDirectionModelInfo> ListModels();
}
