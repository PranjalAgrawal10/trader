namespace Trader.Application.Prediction;

public sealed record MlPriceDirectionPredictionItemDto(
    Guid Id,
    DateTimeOffset PredictedAt,
    DateTimeOffset RefBarTime,
    decimal RefClose,
    string Direction,
    int Confidence,
    string ModelId,
    string Detail,
    string Outcome,
    DateTimeOffset? NextBarTime,
    decimal? NextClose);

public sealed record PriceDirectionPredictionEnvelope(
    PriceDirectionResult Result,
    Guid? StoredId,
    DateTimeOffset? RefBarTimeUtc,
    decimal? RefClose,
    DateTimeOffset? PredictedAtUtc);
