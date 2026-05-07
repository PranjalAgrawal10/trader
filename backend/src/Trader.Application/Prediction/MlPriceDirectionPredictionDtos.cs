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
    decimal? NextClose,
    string? Source = null,
    string? EngineModelId = null);

public sealed record MlAutomationPredictionListItemDto(
    Guid Id,
    DateTimeOffset PredictedAt,
    string InstrumentToken,
    string? Tradingsymbol,
    string? Exchange,
    string Interval,
    DateTimeOffset RefBarTime,
    decimal RefClose,
    string Direction,
    int Confidence,
    string Outcome,
    DateTimeOffset? NextBarTime,
    decimal? NextClose,
    string EngineModelId);

public sealed record PriceDirectionPredictionEnvelope(
    PriceDirectionResult Result,
    Guid? StoredId,
    DateTimeOffset? RefBarTimeUtc,
    decimal? RefClose,
    DateTimeOffset? PredictedAtUtc,
    MlPredictionPersistenceKind PersistenceKind = MlPredictionPersistenceKind.None);
