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
        }
    }

    public float CalibratePUp(float rawPUp)
    {
        var map = LoadMapCached();
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

    private CalibrationMapDto? LoadMapCached()
    {
        var configured = (_options.CurrentValue.ScoreCalibrationJsonPath ?? "").Trim();
        if (string.IsNullOrEmpty(configured))
            return null;

        lock (_gate)
        {
            if (_map is not null
                && string.Equals(_configuredPathStamp, configured, StringComparison.Ordinal))
                return _map;

            var path = Path.IsPathRooted(configured)
                ? configured
                : Path.GetFullPath(Path.Combine(_env.ContentRootPath, configured));

            _configuredPathStamp = configured;
            if (!File.Exists(path))
            {
                _map = null;
                return null;
            }

            try
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<CalibrationMapDto>(File.ReadAllText(path));
                if (parsed?.Xs is not { Length: > 1 } || parsed.Ys.Length != parsed.Xs.Length)
                {
                    _map = null;
                    return null;
                }

                _map = parsed;
                return _map;
            }
            catch
            {
                _map = null;
                return null;
            }
        }
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
