using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Trader.Application.Configuration;
using Trader.Application.Prediction;

namespace Trader.Infrastructure.Prediction;

/// <summary>
/// Piecewise-linear map for P(up) from an isotonic (or manually tuned) JSON artifact.
/// </summary>
/// <remarks>
/// File shape: <c>{"xs":[...],"ys":[...]}</c> with xs strictly ascending in [0,1], ys monotone in [0,1].
/// Path may be absolute, or relative to <see cref="IHostEnvironment.ContentRootPath"/>.
/// </remarks>
public sealed class JsonPiecewiseProbabilityCalibrator : IPriceDirectionScoreCalibrator
{
    private readonly IHostEnvironment _env;
    private readonly IOptionsMonitor<PriceDirectionPredictionOptions> _options;
    private readonly object _gate = new();
    private CalibrationMapDto? _map;
    private string? _configuredPathStamp;
    private readonly Dictionary<string, CalibrationMapDto?> _mapByProfile = new(StringComparer.OrdinalIgnoreCase);

    public JsonPiecewiseProbabilityCalibrator(
        IHostEnvironment env,
        IOptionsMonitor<PriceDirectionPredictionOptions> options)
    {
        _env = env;
        _options = options;
        _options.OnChange(_ => InvalidateCache());
    }

    private void InvalidateCache()
    {
        lock (_gate)
        {
            _map = null;
            _configuredPathStamp = null;
            _mapByProfile.Clear();
        }
    }

    public float CalibratePUp(float rawPUp)
    {
        return CalibratePUp(rawPUp, profileKey: null);
    }

    public float CalibratePUp(float rawPUp, string? profileKey)
    {
        var map = LoadMapCached(profileKey);
        if (map?.Xs.Length is not > 1 || map.Xs.Length != map.Ys.Length)
            return Clamp01(rawPUp);

        var xs = map.Xs;
        var ys = map.Ys;
        var x = (double)Clamp01(rawPUp);
        if (x <= xs[0])
            return (float)ys[0];
        if (x >= xs[^1])
            return (float)ys[^1];

        for (var i = 0; i < xs.Length - 1; i++)
        {
            if (x < xs[i + 1])
            {
                var denom = xs[i + 1] - xs[i];
                if (denom <= 1e-12)
                    return (float)ys[i];
                var t = (x - xs[i]) / denom;
                return (float)(ys[i] + t * (ys[i + 1] - ys[i]));
            }
        }

        return (float)ys[^1];
    }

    private CalibrationMapDto? LoadMapCached(string? profileKey)
    {
        var configured = ResolveConfiguredPath(profileKey);
        if (string.IsNullOrEmpty(configured))
            return null;

        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(profileKey))
            {
                if (_mapByProfile.TryGetValue(profileKey, out var profileMap))
                    return profileMap;
            }
            else if (_map is not null && string.Equals(_configuredPathStamp, configured, StringComparison.Ordinal))
            {
                return _map;
            }

            var path = Path.IsPathRooted(configured)
                ? configured
                : Path.GetFullPath(Path.Combine(_env.ContentRootPath, configured));

            if (!File.Exists(path))
            {
                return StoreCached(profileKey, configured, value: null);
            }

            try
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<CalibrationMapDto>(File.ReadAllText(path));
                if (parsed?.Xs is not { Length: > 1 } || parsed.Ys.Length != parsed.Xs.Length)
                {
                    return StoreCached(profileKey, configured, value: null);
                }

                return StoreCached(profileKey, configured, parsed);
            }
            catch
            {
                return StoreCached(profileKey, configured, value: null);
            }
        }
    }

    private string ResolveConfiguredPath(string? profileKey)
    {
        if (!string.IsNullOrWhiteSpace(profileKey) &&
            _options.CurrentValue.ScoreCalibrationJsonPathByInterval.TryGetValue(profileKey, out var profilePath) &&
            !string.IsNullOrWhiteSpace(profilePath))
        {
            return profilePath.Trim();
        }

        return (_options.CurrentValue.ScoreCalibrationJsonPath ?? "").Trim();
    }

    private CalibrationMapDto? StoreCached(string? profileKey, string configuredPath, CalibrationMapDto? value)
    {
        if (!string.IsNullOrWhiteSpace(profileKey))
        {
            _mapByProfile[profileKey] = value;
            return value;
        }

        _configuredPathStamp = configuredPath;
        _map = value;
        return _map;
    }

    private static float Clamp01(float v) => v <= 0f ? 0f : v >= 1f ? 1f : v;

    private sealed class CalibrationMapDto
    {
        [JsonPropertyName("xs")]
        public double[] Xs { get; set; } = Array.Empty<double>();

        [JsonPropertyName("ys")]
        public double[] Ys { get; set; } = Array.Empty<double>();
    }
}
