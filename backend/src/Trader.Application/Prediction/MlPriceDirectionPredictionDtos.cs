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
    string? EngineModelId = null,
    decimal? LabelThresholdFractionApplied = null,
    string? CensorReason = null,
    sbyte? LabelNextBar = null,
    sbyte? LabelN3 = null,
    sbyte? LabelN5 = null,
    DateTimeOffset? NextBarTimeN3 = null,
    decimal? NextCloseN3 = null,
    DateTimeOffset? NextBarTimeN5 = null,
    decimal? NextCloseN5 = null);

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
