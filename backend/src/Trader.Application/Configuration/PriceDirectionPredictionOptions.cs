namespace Trader.Application.Configuration;

/// <summary>
/// Pick which registered price-direction engine runs when the client omits an explicit model id.
/// </summary>
public sealed class PriceDirectionPredictionOptions
{
    public const string SectionName = "PriceDirectionPrediction";

    /// <summary>
    /// Must match a registered engine <c>ModelId</c> (see Infrastructure DI registration).
    /// </summary>
    public string DefaultModelId { get; set; } = "mlnet-sdca-logistic-v1";
}
