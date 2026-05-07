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
/// Per background tick: resolve pending favorite ML rows, add at most one pending prediction per favorite bar
/// per registered prediction engine, and optionally email an EOD CSV + pie for all engines when SMTP is enabled.
/// </summary>
public sealed class FavoriteMlAutomationService
{
    private readonly FavoriteMlAutomationOptions _opts;
    private readonly SmtpOptions _smtp;
    private readonly IKiteFavoriteInstrumentRepository _favorites;
    private readonly IMlPriceDirectionPredictionRepository _predictions;
    private readonly IMlLightGbmTripleBarrierPredictionRepository _lightGbmPredictions;
    private readonly IMlFavoriteEodReportSentRepository _eodSent;
    private readonly IPriceDirectionPredictionService _predictionService;
    private readonly IPriceDirectionPredictionEngineRegistry _engineRegistry;
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
        IMlLightGbmTripleBarrierPredictionRepository lightGbmPredictions,
        IMlFavoriteEodReportSentRepository eodSent,
        IPriceDirectionPredictionService predictionService,
        IPriceDirectionPredictionEngineRegistry engineRegistry,
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
        _lightGbmPredictions = lightGbmPredictions;
        _eodSent = eodSent;
        _predictionService = predictionService;
        _engineRegistry = engineRegistry;
        _broker = broker;
        _chartSettings = chartSettings;
        _users = users;
        _email = email;
        _pieRenderer = pieRenderer;
        _logger = logger;
    }

    /// <summary>
    /// Sends a one-off email with one outcome pie PNG per registered engine (automation rows only for the local calendar day)
    /// plus one combined-engine pie and a CSV. Does not gate on <see cref="User.FavoriteMlAutomationEnabled"/> or log to <c>MlFavoriteEodReportsSent</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">SMTP off, missing email, or no automation rows for the day.</exception>
    public async Task<FavoriteMlManualAutomationEmailReportResult> SendManualAutomationEmailReportAsync(
        Guid userId,
        DateOnly? reportLocalDayOptional,
        CancellationToken ct)
    {
        if (!_smtp.IsEnabled)
        {
            throw new InvalidOperationException(
                "Server email is not enabled. Set SMTP options (Smtp:IsEnabled and host/credentials).");
        }

        var user = await _users.GetByIdAsync(userId, ct).ConfigureAwait(false);
        if (user is null)
            throw new InvalidOperationException("Account not found.");

        var recipient = user.Email?.Trim();
        if (string.IsNullOrWhiteSpace(recipient))
        {
            throw new InvalidOperationException(
                "Your account has no primary email. Add one under Profile before sending a report.");
        }

        var tz = ResolveReportTimeZone(_opts.ReportTimeZoneId);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var todayLocal = DateOnly.FromDateTime(nowLocal.Date);
        var reportDay = reportLocalDayOptional ?? todayLocal;

        var localMidnight = reportDay.ToDateTime(TimeOnly.MinValue);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(localMidnight, DateTimeKind.Unspecified),
            tz);
        var startDto = new DateTimeOffset(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc));
        var endDto = startDto.AddDays(1);

        var legacyAll = await _predictions.ListPredictedBetweenAsync(userId, startDto, endDto, ct).ConfigureAwait(false);
        var gbmAll = await _lightGbmPredictions.ListPredictedBetweenAsync(userId, startDto, endDto, ct)
            .ConfigureAwait(false);

        var legacyFiltered = legacyAll.Where(IsAutomationSource).ToList();
        var gbmFiltered = gbmAll.Where(IsAutomationSource).ToList();
        var rows = CombineReportRows(legacyFiltered, gbmFiltered);
        if (rows.Count == 0)
        {
            throw new InvalidOperationException(
                $"No automation-source predictions stored for local date {reportDay:yyyy-MM-dd} ({tz.Id}). Pick another calendar day or run automation first.");
        }

        var favs = await _favorites.ListByUserAsync(userId, ct).ConfigureAwait(false);
        var symbolByToken = favs.ToDictionary(
            x => x.InstrumentToken.Trim(),
            x => (x.Tradingsymbol, x.Exchange),
            StringComparer.Ordinal);

        var correctAll = rows.Count(r => string.Equals(r.Outcome, "correct", StringComparison.OrdinalIgnoreCase));
        var wrongAll = rows.Count(r => string.Equals(r.Outcome, "wrong", StringComparison.OrdinalIgnoreCase));
        var pendingAll = rows.Count(r => string.Equals(r.Outcome, "pending", StringComparison.OrdinalIgnoreCase));

        var ymd = reportDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var engineIdsForPies = OrderedRegistryEngineIdsForManualReport(rows);
        var attachments = new List<EmailAttachment>
        {
            new(
                $"ml-automation-all-engines-{ymd}.png",
                "image/png",
                _pieRenderer.Render(correctAll, wrongAll, pendingAll, $"Automation outcomes — all engines ({ymd})")),
        };

        foreach (var engineId in engineIdsForPies)
        {
            var scoped = rows.Where(r => EffectiveEngineKey(r).Equals(engineId, StringComparison.OrdinalIgnoreCase)).ToList();
            var c = scoped.Count(r => string.Equals(r.Outcome, "correct", StringComparison.OrdinalIgnoreCase));
            var w = scoped.Count(r => string.Equals(r.Outcome, "wrong", StringComparison.OrdinalIgnoreCase));
            var p = scoped.Count(r => string.Equals(r.Outcome, "pending", StringComparison.OrdinalIgnoreCase));
            attachments.Add(
                new EmailAttachment(
                    $"ml-auto-{SanitizeFilenameToken(engineId)}-{ymd}.png",
                    "image/png",
                    _pieRenderer.Render(c, w, p, $"Automation outcomes — {engineId} ({ymd})")));
        }

        var csvBody = BuildCsv(rows, symbolByToken);
        attachments.Add(
            new EmailAttachment($"ml-automation-{ymd}.csv", "text/csv", Encoding.UTF8.GetBytes(csvBody)));

        var subject =
            $"Trader ML automation — manual report {ymd} (rows={rows.Count}; SMTP; all engines)";
        var bodyLines = new[]
        {
            $"Manual automation report for local calendar day {ymd} ({tz.Id}).",
            $"Rows (Source=automation only): {rows.Count} (correct {correctAll}, wrong {wrongAll}, pending {pendingAll}).",
            "Attachments: one PNG for all engines combined, one PNG per registered engine (may show empty slices), and CSV.",
            "This send is not throttled against the nightly EOD job.",
        };
        await _email
            .SendPlainTextAsync(recipient, subject, string.Join("\r\n", bodyLines), attachments, ct)
            .ConfigureAwait(false);

        var pieCharts = 1 + engineIdsForPies.Count;
        _logger.LogInformation(
            "Manual automation email report sent to user {UserId} for local {Day}; rows={Count}; pieCharts={Pies}; totalAttachments={Attachments}",
            userId,
            ymd,
            rows.Count,
            pieCharts,
            attachments.Count);

        return new FavoriteMlManualAutomationEmailReportResult(rows.Count, ymd, pieCharts, attachments.Count);
    }

    public async Task RunCycleAsync(CancellationToken ct)
    {
        var suppressNewAutomationPredictions =
            FavoriteMlAutomationQuietHours.IsAutomationPaused(_opts, DateTime.UtcNow);

        var userIds = await _favorites.ListDistinctUserIdsWithFavoritesAsync(ct).ConfigureAwait(false);
        foreach (var uid in userIds)
        {
            ct.ThrowIfCancellationRequested();
            var user = await _users.GetByIdAsync(uid, ct).ConfigureAwait(false);
            if (user is null)
                continue;

            try
            {
                await ProcessUserPredictionsAsync(user, suppressNewAutomationPredictions, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Favorite ML automation skipped for user {UserId}", uid);
            }

            try
            {
                await MaybeSendEodReportAsync(user, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "EOD ML report failed for user {UserId}", uid);
            }
        }
    }

    /// <summary>Registry order then any extra <see cref="MlEodReportRow.EngineModelId"/> / ModelId keys present in automation rows.</summary>
    private IReadOnlyList<string> OrderedRegistryEngineIdsForManualReport(IReadOnlyList<MlEodReportRow> rowsFromDay)
    {
        var fromRegistry = _engineRegistry
            .ListModels()
            .Select(static m => m.Id.Trim())
            .Where(static s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var seen = new HashSet<string>(fromRegistry, StringComparer.OrdinalIgnoreCase);
        var extras = new List<string>();
        foreach (var r in rowsFromDay)
        {
            var k = EffectiveEngineKey(r);
            if (k.Length > 0 && seen.Add(k))
                extras.Add(k);
        }

        extras.Sort(StringComparer.OrdinalIgnoreCase);
        return fromRegistry.Concat(extras).ToList();
    }

    private static string EffectiveEngineKey(MlEodReportRow r) =>
        (string.IsNullOrWhiteSpace(r.EngineModelId) ? r.ModelId : r.EngineModelId).Trim();

    private static bool IsAutomationSource(MlPriceDirectionPrediction entity) =>
        string.Equals(entity.Source?.Trim(), PriceDirectionPredictionService.SourceAutomation, StringComparison.OrdinalIgnoreCase);

    private static bool IsAutomationSource(MlLightGbmTripleBarrierPrediction entity) =>
        string.Equals(entity.Source?.Trim(), PriceDirectionPredictionService.SourceAutomation, StringComparison.OrdinalIgnoreCase);

    private static string SanitizeFilenameToken(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "engine";

        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw.Trim())
            sb.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_');

        return sb.Length > 0 ? sb.ToString() : "engine";
    }

    /// <summary>Resolve pending favorite-ML rows; optionally run the per-favorite new prediction pass for automation.</summary>
    /// <remarks>
    /// When <paramref name="suppressNewAutomationPredictions"/> is true (favorite-ML quiet hours), pending rows are still
    /// resolved from candles; only the scheduled <strong>new</strong> prediction pass per favorite/engine is skipped.
    /// Interactive <c>GET /predictions/price-direction</c> and other API features are not gated here.
    /// </remarks>
    private async Task ProcessUserPredictionsAsync(
        User user,
        bool suppressNewAutomationPredictions,
        CancellationToken ct)
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
                await TryResolvePendingRowAsync(
                    userId,
                    row.InstrumentToken,
                    row.Interval,
                    row.RefBarTimeUtc,
                    row.Id,
                    ct)
                    .ConfigureAwait(false);
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

        var pendingGbm = await _lightGbmPredictions
            .ListPendingAsync(userId, _opts.MaxPendingResolutionBatch, ct)
            .ConfigureAwait(false);
        foreach (var row in pendingGbm)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await TryResolvePendingRowAsync(
                    userId,
                    row.InstrumentToken,
                    row.Interval,
                    row.RefBarTimeUtc,
                    row.Id,
                    ct)
                    .ConfigureAwait(false);
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

        if (suppressNewAutomationPredictions)
            return;

        var engineIds = FavoriteMlAutomationModelSelection.ResolveEngineIdsToRun(
            _engineRegistry,
            _opts.PredictionModelId);

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
                var token = fav.InstrumentToken.Trim();

                foreach (var engineModelId in engineIds)
                {
                    ct.ThrowIfCancellationRequested();
                    var hasPending = await _predictionService
                        .HasPendingForEngineAndRefBarAsync(userId, token, hist.Interval, last.Time, engineModelId, ct)
                        .ConfigureAwait(false);
                    if (hasPending)
                        continue;

                    await _predictionService
                        .PredictForInstrumentAsync(
                            userId,
                            fav.InstrumentToken,
                            hist.Interval,
                            PriceDirectionPredictionService.SourceAutomation,
                            engineModelId,
                            ct)
                        .ConfigureAwait(false);
                }
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

    private async Task TryResolvePendingRowAsync(
        Guid userId,
        string instrumentToken,
        string interval,
        DateTimeOffset refBarTimeUtc,
        Guid predictionId,
        CancellationToken ct)
    {
        var hist = await _broker
            .GetKiteHistoricalCandlesAsync(userId, instrumentToken, interval, null, null, ct)
            .ConfigureAwait(false);
        var idx = FindRefBarIndex(hist.Candles, refBarTimeUtc);
        if (idx < 0 || idx + 1 >= hist.Candles.Count)
            return;
        var next = hist.Candles[idx + 1];
        await _predictionService
            .ResolvePredictionAsync(userId, predictionId, next.Time, next.Close, ct)
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

        var legacyRows = await _predictions
            .ListPredictedBetweenAsync(userId, startUtcDay, endUtcDay, ct)
            .ConfigureAwait(false);
        var gbmRows = await _lightGbmPredictions
            .ListPredictedBetweenAsync(userId, startUtcDay, endUtcDay, ct)
            .ConfigureAwait(false);
        var rows = CombineReportRows(legacyRows, gbmRows);
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

        var subject =
            $"Trader ML favorites — {ymd} (all engines; rows={rows.Count}; correct {correct}, wrong {wrong}, pending {pending})";
        var body =
            $"Combined ML predictions (every registered automation engine; classic + LightGBM stores) on {ymd} ({tz.Id}).\r\n"
            + $"Totals: rows={rows.Count}, correct={correct}, wrong={wrong}, pending={pending}.\r\n"
            + "See attachments: outcome pie chart (PNG) and full CSV (includes engineModelId + model output id).";

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

    private static IReadOnlyList<MlEodReportRow> CombineReportRows(
        IReadOnlyList<MlPriceDirectionPrediction> legacy,
        IReadOnlyList<MlLightGbmTripleBarrierPrediction> gbm)
    {
        if (legacy.Count == 0 && gbm.Count == 0)
            return Array.Empty<MlEodReportRow>();

        var list = new List<MlEodReportRow>(legacy.Count + gbm.Count);
        foreach (var r in legacy)
        {
            list.Add(
                new MlEodReportRow(
                    r.PredictedAtUtc,
                    r.InstrumentToken,
                    r.Interval,
                    r.Direction,
                    r.Confidence,
                    r.Outcome,
                    r.RefBarTimeUtc,
                    r.RefClose,
                    r.NextBarTimeUtc,
                    r.NextClose,
                    r.ModelId,
                    r.EngineModelId ?? r.ModelId,
                    r.Detail));
        }

        foreach (var r in gbm)
        {
            list.Add(
                new MlEodReportRow(
                    r.PredictedAtUtc,
                    r.InstrumentToken,
                    r.Interval,
                    r.Direction,
                    r.Confidence,
                    r.Outcome,
                    r.RefBarTimeUtc,
                    r.RefClose,
                    r.NextBarTimeUtc,
                    r.NextClose,
                    r.ModelId,
                    r.EngineModelId ?? r.ModelId,
                    r.Detail));
        }

        list.Sort(static (a, b) => DateTimeOffset.Compare(a.PredictedAtUtc, b.PredictedAtUtc));
        return list;
    }

    private sealed record MlEodReportRow(
        DateTimeOffset PredictedAtUtc,
        string InstrumentToken,
        string Interval,
        string Direction,
        int Confidence,
        string Outcome,
        DateTimeOffset RefBarTimeUtc,
        decimal RefClose,
        DateTimeOffset? NextBarTimeUtc,
        decimal? NextClose,
        string ModelId,
        string EngineModelId,
        string Detail);

    private static string BuildCsv(
        IReadOnlyList<MlEodReportRow> rows,
        IReadOnlyDictionary<string, (string Tradingsymbol, string Exchange)> symbolByToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "predictedAtUtc,instrumentToken,tradingsymbol,exchange,interval,direction,confidence,outcome,refBarTimeUtc,refClose,nextBarTimeUtc,nextClose,modelId,engineModelId,detail");
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
                .Append(Csv(r.EngineModelId))
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

public sealed record FavoriteMlManualAutomationEmailReportResult(
    int RowCount,
    string LocalCalendarDateIso,
    int PieChartsAttached,
    int TotalAttachmentsSent);
