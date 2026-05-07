using Microsoft.Extensions.Options;
using Trader.Application.Configuration;
using Trader.Application.Prediction;

namespace Trader.Infrastructure.Prediction;

public sealed class PriceDirectionPredictionEngineRegistry : IPriceDirectionPredictionEngineRegistry
{
    private readonly IReadOnlyDictionary<string, IPriceDirectionPredictionEngine> _byId;
    private readonly string _defaultModelId;

    public PriceDirectionPredictionEngineRegistry(
        IEnumerable<IPriceDirectionPredictionEngine> engines,
        IOptions<PriceDirectionPredictionOptions> options)
    {
        var list = engines.ToList();
        var dup = list
            .GroupBy(e => e.ModelId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (dup is not null)
        {
            throw new InvalidOperationException(
                $"Duplicate price-direction engine {nameof(IPriceDirectionPredictionEngine.ModelId)}: {dup.Key}");
        }

        _byId = list.ToDictionary(e => e.ModelId, e => e, StringComparer.OrdinalIgnoreCase);

        var configured = options.Value.DefaultModelId?.Trim();
        if (string.IsNullOrEmpty(configured))
        {
            throw new InvalidOperationException(
                $"{PriceDirectionPredictionOptions.SectionName}:DefaultModelId is missing or empty.");
        }

        _defaultModelId = configured;
        if (!_byId.ContainsKey(_defaultModelId))
        {
            var known = string.Join(", ", _byId.Keys.Order(StringComparer.Ordinal));
            throw new InvalidOperationException(
                $"{PriceDirectionPredictionOptions.SectionName}:DefaultModelId '{_defaultModelId}' is not registered. " +
                $"Registered ids: {known}.");
        }
    }

    public string DefaultModelId => _defaultModelId;

    public IPriceDirectionPredictionEngine Resolve(string? modelId)
    {
        var id = string.IsNullOrWhiteSpace(modelId) ? _defaultModelId : modelId.Trim();
        if (_byId.TryGetValue(id, out var engine))
            return engine;

        var known = string.Join(", ", _byId.Keys.Order(StringComparer.Ordinal));
        throw new InvalidOperationException(
            $"Unknown price-direction model '{id}'. Known: {known}.");
    }

    public IReadOnlyList<PriceDirectionModelInfo> ListModels() =>
        _byId.Values
            .OrderBy(e => e.ModelId, StringComparer.Ordinal)
            .Select(e => new PriceDirectionModelInfo(e.ModelId, e.Description))
            .ToList();
}
