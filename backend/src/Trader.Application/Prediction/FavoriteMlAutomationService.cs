using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Application.Abstractions.Messaging;
using Trader.Application.Abstractions.Persistence;
using Trader.Application.Broker;
using Trader.Application.Configuration;
using Trader.Application.Abstractions.Reporting;
using Trader.Domain.Entities;

namespace Trader.Application.Prediction;

/// <summary>
/// Per background tick: resolve pending ML rows using fresh Kite candles, add at most one pending prediction per favorite bar,
/// and optionally email an EOD CSV + pie attachment when SMTP is enabled.
/// </summary>
public sealed class FavoriteMlAutomationService
{
    private readonly FavoriteMlAutomationOptions _opts;
    private readonly SmtpOptions _smtp;
    private readonly IKiteFavoriteInstrumentRepository _favorites;
    private readonly IMlPriceDirectionPredictionRepository _predictions;
    private readonly IMlFavoriteEodReportSentRepository _eodSent;
    private readonly IPriceDirectionPredictionService _predictionService;
    private readonly IBrokerService _broker;
    private readonly IKiteInstrumentsChartSettingsGateway _chartSettings;
    private readonly IUserRepository _users;
    private readonly IPlainTextEmailSender _email;
    private readonly IMlOutcomePieChartPngRenderer _pieRenderer;
    private readonly ILogger<FavoriteMlAutomationService> _logger;

    public FavoriteMlAutomationService(
        IOptions<FavoriteMlAutomationOptions> opts,
        IOptions<SmtpOptions> smtp,
        IKiteFavoriteInstrumentRepository favorites,
        IMlPriceDirectionPredictionRepository predictions,
        IMlFavoriteEodReportSentRepository eodSent,
        IPriceDirectionPredictionService predictionService,
        IBrokerService broker,
        IKiteInstrumentsChartSettingsGateway chartSettings,
        IUserRepository users,
        IPlainTextEmailSender email,
        IMlOutcomePieChartPngRenderer pieRenderer,
        ILogger<FavoriteMlAutomationService> logger)
    {
        _opts = opts.Value;
        _smtp = smtp.Value;
        _favorites = favorites;
        _predictions = predictions;
        _eodSent = eodSent;
        _predictionService = predictionService;
        _broker = broker;
        _chartSettings = chartSettings;
        _users = users;
        _email = email;
        _pieRenderer = pieRenderer;
        _logger = logger;
    }

    public async Task RunCycleAsync(CancellationToken ct)
    {
        var userIds = await _favorites.ListDistinctUserIdsWithFavoritesAsync(ct).ConfigureAwait(false);
        foreach (var userId in userIds)
        {
            ct.ThrowIfCancellationRequested();
            var user = await _users.GetByIdAsync(userId, ct).ConfigureAwait(false);
            if (user is null)
                continue;

            try
            {
                await ProcessUserPredictionsAsync(user, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Favorite ML automation skipped for user {UserId}", userId);
            }

            try
            {
                await MaybeSendEodReportAsync(user, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "EOD ML report failed for user {UserId}", userId);
            }
        }
    }

    private async Task ProcessUserPredictionsAsync(User user, CancellationToken ct)
    {
        var userId = user.Id;
        if (!user.FavoriteMlAutomationEnabled)
            return;

        var chartState = await _chartSettings.GetAsync(userId, ct).ConfigureAwait(false);
        var globalInterval = SafeNormalizeInterval(chartState?.Interval, _opts.DefaultChartInterval.Trim());
        var intervalOverrides = ParseIntervalOverrides(chartState?.ChartIntervalByInstrumentTokenJson);
        var favorites = await _favorites.ListByUserAsync(userId, ct).ConfigureAwait(false);
        if (favorites.Count == 0)
            return;

        var pending = await _predictions
            .ListPendingAsync(userId, _opts.MaxPendingResolutionBatch, ct)
            .ConfigureAwait(false);
        foreach (var row in pending)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await TryResolvePendingAsync(userId, row, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "Could not resolve prediction {Id} for user {UserId}",
                    row.Id,
                    userId);
            }
        }

        foreach (var fav in favorites)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var interval = IntervalForAutomation(globalInterval, intervalOverrides, fav.InstrumentToken);
                var hist = await _broker
                    .GetKiteHistoricalCandlesAsync(userId, fav.InstrumentToken, interval, null, null, ct)
                    .ConfigureAwait(false);
                if (hist.Candles.Count == 0)
                    continue;
                var last = hist.Candles[^1];
                var hasPending = await _predictions
                    .HasPendingForRefBarAsync(userId, fav.InstrumentToken.Trim(), hist.Interval, last.Time, ct)
                    .ConfigureAwait(false);
                if (hasPending)
                    continue;
                await _predictionService
                    .PredictForInstrumentAsync(
                        userId,
                        fav.InstrumentToken,
                        hist.Interval,
                        PriceDirectionPredictionService.SourceAutomation,
                        string.IsNullOrWhiteSpace(_opts.PredictionModelId) ? null : _opts.PredictionModelId.Trim(),
                        ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "Favorite ML predict skipped for user {UserId} token {Token}",
                    userId,
                    fav.InstrumentToken);
            }
        }
    }

    private async Task TryResolvePendingAsync(Guid userId, MlPriceDirectionPrediction row, CancellationToken ct)
    {
        var hist = await _broker
            .GetKiteHistoricalCandlesAsync(userId, row.InstrumentToken, row.Interval, null, null, ct)
            .ConfigureAwait(false);
        var idx = FindRefBarIndex(hist.Candles, row.RefBarTimeUtc);
        if (idx < 0 || idx + 1 >= hist.Candles.Count)
            return;
        var next = hist.Candles[idx + 1];
        await _predictionService
            .ResolvePredictionAsync(userId, row.Id, next.Time, next.Close, ct)
            .ConfigureAwait(false);
    }

    private static Dictionary<string, string> ParseIntervalOverrides(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            var d = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return d is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(d, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static string SafeNormalizeInterval(string? raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;
        try
        {
            return ChartUiIntervals.Normalize(raw);
        }
        catch
        {
            return fallback;
        }
    }

    private string IntervalForAutomation(
        string globalInterval,
        IReadOnlyDictionary<string, string> overrides,
        string instrumentToken)
    {
        var raw = _opts.PredictionIntervalOverride?.Trim();
        if (!string.IsNullOrEmpty(raw))
            return SafeNormalizeInterval(raw, "1m");

        return EffectiveInterval(globalInterval, overrides, instrumentToken);
    }

    private static string EffectiveInterval(
        string globalInterval,
        IReadOnlyDictionary<string, string> overrides,
        string instrumentToken)
    {
        var key = instrumentToken.Trim();
        if (overrides.TryGetValue(key, out var o) && !string.IsNullOrWhiteSpace(o))
        {
            try
            {
                return ChartUiIntervals.Normalize(o);
            }
            catch
            {
                // ignore bad override
            }
        }

        return globalInterval;
    }

    private async Task MaybeSendEodReportAsync(User user, CancellationToken ct)
    {
        if (!_smtp.IsEnabled)
            return;

        var userId = user.Id;
        if (string.IsNullOrWhiteSpace(user.Email) || !user.FavoriteMlAutomationEnabled)
            return;

        var tz = ResolveReportTimeZone(_opts.ReportTimeZoneId);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var ymd = DateOnly.FromDateTime(nowLocal.Date).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var triggerLocal = new DateTime(
            nowLocal.Year,
            nowLocal.Month,
            nowLocal.Day,
            _opts.ReportLocalHour,
            _opts.ReportLocalMinute,
            0,
            DateTimeKind.Unspecified);
        var triggerUtc = TimeZoneInfo.ConvertTimeToUtc(triggerLocal, tz);
        var windowEnd = triggerUtc.AddHours(2);
        var nowUtc = DateTimeOffset.UtcNow;
        if (nowUtc < triggerUtc || nowUtc >= windowEnd)
            return;

        if (await _eodSent.ExistsAsync(userId, ymd, ct).ConfigureAwait(false))
            return;

        var startLocal = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var endLocal = startLocal.AddDays(1);
        var startUtcDay = TimeZoneInfo.ConvertTimeToUtc(startLocal, tz);
        var endUtcDay = TimeZoneInfo.ConvertTimeToUtc(endLocal, tz);

        var rows = await _predictions
            .ListPredictedBetweenAsync(userId, startUtcDay, endUtcDay, ct)
            .ConfigureAwait(false);
        if (rows.Count == 0)
            return;

        var favs = await _favorites.ListByUserAsync(userId, ct).ConfigureAwait(false);
        var symbolByToken = favs.ToDictionary(
            x => x.InstrumentToken.Trim(),
            x => (x.Tradingsymbol, x.Exchange),
            StringComparer.Ordinal);

        var correct = rows.Count(r => r.Outcome == "correct");
        var wrong = rows.Count(r => r.Outcome == "wrong");
        var pending = rows.Count(r => r.Outcome == "pending");
        var pieBytes = _pieRenderer.Render(correct, wrong, pending);
        var csv = BuildCsv(rows, symbolByToken);
        var csvBytes = Encoding.UTF8.GetBytes(csv);

        var subject = $"Trader ML favorites — {ymd} (correct {correct}, wrong {wrong}, pending {pending})";
        var body =
            $"ML price-direction predictions for your favorite instruments on {ymd} ({tz.Id}).\r\n"
            + $"Totals: correct={correct}, wrong={wrong}, pending={pending}.\r\n"
            + "See attachments: outcome pie chart (PNG) and full list (CSV).";

        try
        {
            await _email
                .SendPlainTextAsync(
                    user.Email,
                    subject,
                    body,
                    new[]
                    {
                        new EmailAttachment($"ml-outcomes-{ymd}.png", "image/png", pieBytes),
                        new EmailAttachment($"ml-predictions-{ymd}.csv", "text/csv", csvBytes),
                    },
                    ct)
                .ConfigureAwait(false);
            await _eodSent.AddAsync(userId, ymd, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
            await _eodSent.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SMTP send failed for EOD ML report user {UserId}; will retry in window", userId);
            throw;
        }
    }

    private static string BuildCsv(
        IReadOnlyList<MlPriceDirectionPrediction> rows,
        IReadOnlyDictionary<string, (string Tradingsymbol, string Exchange)> symbolByToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "predictedAtUtc,instrumentToken,tradingsymbol,exchange,interval,direction,confidence,outcome,refBarTimeUtc,refClose,nextBarTimeUtc,nextClose,modelId,detail");
        foreach (var r in rows)
        {
            var tok = r.InstrumentToken.Trim();
            symbolByToken.TryGetValue(tok, out var sym);
            sb.Append(Csv(r.PredictedAtUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)))
                .Append(',')
                .Append(Csv(tok))
                .Append(',')
                .Append(Csv(sym.Tradingsymbol))
                .Append(',')
                .Append(Csv(sym.Exchange))
                .Append(',')
                .Append(Csv(r.Interval))
                .Append(',')
                .Append(Csv(r.Direction))
                .Append(',')
                .Append(r.Confidence.ToString(CultureInfo.InvariantCulture))
                .Append(',')
                .Append(Csv(r.Outcome))
                .Append(',')
                .Append(Csv(r.RefBarTimeUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)))
                .Append(',')
                .Append(Csv(r.RefClose.ToString(CultureInfo.InvariantCulture)))
                .Append(',')
                .Append(Csv(r.NextBarTimeUtc?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)))
                .Append(',')
                .Append(Csv(r.NextClose?.ToString(CultureInfo.InvariantCulture)))
                .Append(',')
                .Append(Csv(r.ModelId))
                .Append(',')
                .Append(Csv(r.Detail))
                .AppendLine();
        }

        return sb.ToString();
    }

    private static string Csv(string? value)
    {
        var s = value ?? string.Empty;
        if (s.Contains('"', StringComparison.Ordinal))
            s = s.Replace("\"", "\"\"", StringComparison.Ordinal);
        if (s.Contains(',', StringComparison.Ordinal) || s.Contains('\r') || s.Contains('\n'))
            return $"\"{s}\"";
        return string.IsNullOrEmpty(s) ? "\"\"" : s;
    }

    private static int FindRefBarIndex(
        IReadOnlyList<KiteHistoricalCandlePointDto> candles,
        DateTimeOffset refBar)
    {
        for (var i = 0; i < candles.Count; i++)
        {
            if (Math.Abs((candles[i].Time - refBar).TotalSeconds) < 1.5)
                return i;
        }

        return -1;
    }

    private static TimeZoneInfo ResolveReportTimeZone(string? id)
    {
        var tid = string.IsNullOrWhiteSpace(id) ? "Asia/Kolkata" : id.Trim();
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(tid);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
