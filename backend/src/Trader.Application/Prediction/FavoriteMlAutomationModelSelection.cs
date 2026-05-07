namespace Trader.Application.Prediction;

/// <summary>
/// Builds the ordered engine id list for favorite automation ticks.
/// </summary>
public static class FavoriteMlAutomationModelSelection
{
    /// <summary>
    /// When <paramref name="predictionModelIdsCsv"/> is null/empty → all engines from the registry (chart order).
    /// Otherwise comma-separated subset; unknown ids ignored; empty after filtering falls back to all.
    /// </summary>
    public static IReadOnlyList<string> ResolveEngineIdsToRun(
        IPriceDirectionPredictionEngineRegistry registry,
        string? predictionModelIdsCsv)
    {
        var all = registry
            .ListModels()
            .Select(m => m.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var raw = predictionModelIdsCsv?.Trim();
        if (string.IsNullOrEmpty(raw))
            return all;

        var known = registry.ListModels().Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var picked = new List<string>();
        foreach (var part in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!known.Contains(part))
                continue;

            var id = registry.Resolve(part).ModelId;
            if (!picked.Contains(id, StringComparer.OrdinalIgnoreCase))
                picked.Add(id);
        }

        return picked.Count > 0 ? picked : all;
    }
}
