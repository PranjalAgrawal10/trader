using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Application.Abstractions.Messaging;
using Trader.Application.Abstractions.Persistence;
using Trader.Application.Abstractions.Reporting;
using Trader.Application.Broker;
using Trader.Application.Configuration;
using Trader.Application.Reporting;
using Trader.Domain.Entities;

namespace Trader.Application.Prediction;

/// <summary>
/// Per background tick: resolve pending favorite ML rows, add at most one pending prediction per favorite bar
/// per registered prediction engine, and optionally email an EOD CSV + multipart HTML with inline PNG (<c>cid:</c>) outcome charts when SMTP is enabled.
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
    private readonly IMlOutcomePieChartPngGenerator _piePng;
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
        IMlOutcomePieChartPngGenerator piePng,
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
        _piePng = piePng;
        _logger = logger;
    }

    /// <summary>
    /// Sends a one-off multipart email with <strong>inline PNG</strong> outcome charts (<c>cid:</c>-embedded; combined + per engine) plus a CSV attachment
    /// whose <c>PredictedAtUtc</c> is in [<paramref name="rangeFromUtcInclusive"/>, <paramref name="rangeToUtcExclusive"/>).
    /// When both UTC bounds are <c>null</c>, uses today's full calendar day in <see cref="FavoriteMlAutomationOptions.ReportTimeZoneId"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">SMTP off, missing email, invalid range, or no matching rows.</exception>
    public async Task<FavoriteMlManualAutomationEmailReportResult> SendManualAutomationEmailReportAsync(
        Guid userId,
        DateTimeOffset? rangeFromUtcInclusive,
        DateTimeOffset? rangeToUtcExclusive,
        CancellationToken ct)
    {
        const int maxRangeDays = 93;

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
        DateTimeOffset startDto;
        DateTimeOffset endDto;
        string reportRangeSummary;
        string fileSuffix;

        switch (rangeFromUtcInclusive, rangeToUtcExclusive)
        {
            case (null, null):
            {
                var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
                var reportDay = DateOnly.FromDateTime(nowLocal.Date);
                var localMidnight = reportDay.ToDateTime(TimeOnly.MinValue);
                var startUtcDt = TimeZoneInfo.ConvertTimeToUtc(
                    DateTime.SpecifyKind(localMidnight, DateTimeKind.Unspecified),
                    tz);
                startDto = new DateTimeOffset(DateTime.SpecifyKind(startUtcDt, DateTimeKind.Utc));
                endDto = startDto.AddDays(1);
                reportRangeSummary = $"{reportDay:yyyy-MM-dd} (full local day {tz.Id})";
                fileSuffix = reportDay.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                break;
            }
            case ({ } fromU, { } toU):
            {
                var startUtc = fromU.UtcDateTime;
                var endUtcRaw = toU.UtcDateTime;
                if (endUtcRaw <= startUtc)
                {
                    throw new InvalidOperationException(
                        "Report range invalid: end must be after start. Rows use PredictedAtUtc in [start, end).");
                }

                var spanDays = (endUtcRaw - startUtc).TotalDays;
                if (spanDays <= 0 || spanDays > maxRangeDays)
                {
                    throw new InvalidOperationException(
                        $"Report range exceeds {maxRangeDays} days between start and end, or bounds are invalid.");
                }

                startDto = new DateTimeOffset(startUtc, TimeSpan.Zero);
                endDto = new DateTimeOffset(endUtcRaw, TimeSpan.Zero);

                var sLoc = TimeZoneInfo.ConvertTimeFromUtc(startDto.UtcDateTime, tz);
                var eLoc = TimeZoneInfo.ConvertTimeFromUtc(endDto.UtcDateTime, tz);
                reportRangeSummary =
                    $"{sLoc:yyyy-MM-dd HH:mm} – {eLoc:yyyy-MM-dd HH:mm} ({tz.Id}; PredictedAt in [start,end) UTC)";
                fileSuffix = SafeManualReportRangeFileSuffix(startDto, endDto);
                break;
            }
            default:
                throw new InvalidOperationException(
                    "Provide both fromUtc and toUtcExclusive, or omit both for today's calendar day (report timezone).");
        }

        var legacyAll =
            await _predictions.ListPredictedBetweenAsync(userId, startDto, endDto, ct).ConfigureAwait(false);
        var gbmAll =
            await _lightGbmPredictions.ListPredictedBetweenAsync(userId, startDto, endDto, ct).ConfigureAwait(false);

        var legacyFiltered = legacyAll.Where(IsAutomationSource).ToList();
        var gbmFiltered = gbmAll.Where(IsAutomationSource).ToList();
        var rows = CombineReportRows(legacyFiltered, gbmFiltered);
        if (rows.Count == 0)
        {
            throw new InvalidOperationException(
                $"No automation-source predictions for {reportRangeSummary}. Expand the range or verify automation ran in that interval.");
        }

        var favs = await _favorites.ListByUserAsync(userId, ct).ConfigureAwait(false);
        var symbolByToken = favs.ToDictionary(
            x => x.InstrumentToken.Trim(),
            x => (x.Tradingsymbol, x.Exchange),
            StringComparer.Ordinal);

        var correctAll = rows.Count(r => string.Equals(r.Outcome, "correct", StringComparison.OrdinalIgnoreCase));
        var wrongAll = rows.Count(r => string.Equals(r.Outcome, "wrong", StringComparison.OrdinalIgnoreCase));
        var pendingAll = rows.Count(r => string.Equals(r.Outcome, "pending", StringComparison.OrdinalIgnoreCase));

        var pieTitleSuffix = reportRangeSummary.Length > 86 ? reportRangeSummary[..83] + "…" : reportRangeSummary;
        var engineIdsForPies = OrderedRegistryEngineIdsForManualReport(rows);

        var embeddedPies = new List<EmbeddedEmailImage>(1 + engineIdsForPies.Count);
        var chartFragments = new List<string>(1 + engineIdsForPies.Count);

        AddPieCharts(
            embeddedPies,
            chartFragments,
            $"Automation — all engines ({pieTitleSuffix})",
            correctAll,
            wrongAll,
            pendingAll);

        var (manualVoteUp, manualVoteDown, manualVoteNeu) =
            PriceDirectionBestOfThree.SumVoteComponents(rows.Select(static r => r.Detail));
        AddDirectionVotePieIfAny(
            embeddedPies,
            chartFragments,
            $"Automation — best-of-3 direction votes ({pieTitleSuffix})",
            manualVoteUp,
            manualVoteDown,
            manualVoteNeu);

        foreach (var engineId in engineIdsForPies)
        {
            var scoped = rows.Where(r => EffectiveEngineKey(r).Equals(engineId, StringComparison.OrdinalIgnoreCase)).ToList();
            var c = scoped.Count(r => string.Equals(r.Outcome, "correct", StringComparison.OrdinalIgnoreCase));
            var w = scoped.Count(r => string.Equals(r.Outcome, "wrong", StringComparison.OrdinalIgnoreCase));
            var p = scoped.Count(r => string.Equals(r.Outcome, "pending", StringComparison.OrdinalIgnoreCase));
            AddPieCharts(embeddedPies, chartFragments, $"Automation — {engineId} ({pieTitleSuffix})", c, w, p);
        }

        var htmlBody = MlOutcomePieChartHtmlBuilder.WrapHtmlDocument(chartFragments);

        var csvBody = BuildCsv(rows, symbolByToken);
        var attachments = new[]
        {
            new EmailAttachment($"ml-automation-{fileSuffix}.csv", "text/csv", Encoding.UTF8.GetBytes(csvBody)),
        };

        var subject = $"Trader ML automation — manual ({reportRangeSummary}) rows={rows.Count}";
        var plainBody = string.Join(
            "\r\n",
            new[]
            {
                $"Manual automation report for {reportRangeSummary}.",
                $"Rows (Source=automation only): {rows.Count} (correct {correctAll}, wrong {wrongAll}, pending {pendingAll}).",
                $"The HTML part embeds {chartFragments.Count} PNG chart(s) via cid (outcome + optional best-of-3 direction vote + per engine). CSV is attached.",
                "This send is not throttled against the nightly EOD job.",
                string.Empty,
                "Prefer the graphical HTML part in Mail / Outlook / Gmail or use the spreadsheet attachment.",
            });

        await _email
            .SendEmailAsync(recipient, subject, plainBody, htmlBody, embeddedPies, attachments, ct)
            .ConfigureAwait(false);

        var pieCharts = chartFragments.Count;
        _logger.LogInformation(
            "Manual automation email sent user {UserId} range={Range}; rows={Count}; pieCharts={Pies}; attachments={Attachments}",
            userId,
            reportRangeSummary,
            rows.Count,
            pieCharts,
            attachments.Length);

        return new FavoriteMlManualAutomationEmailReportResult(
            rows.Count,
            reportRangeSummary,
            pieCharts,
            attachments.Length);
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

    private void AddPieCharts(
        List<EmbeddedEmailImage> embedded,
        List<string> fragments,
        string title,
        int correct,
        int wrong,
        int pending)
    {
        if (correct + wrong + pending <= 0)
        {
            fragments.Add(MlOutcomePieChartHtmlBuilder.BuildChartSection(title, "ml-pie-empty", correct, wrong, pending));
            return;
        }

        var cid = $"ml-pie-{fragments.Count}";
        fragments.Add(MlOutcomePieChartHtmlBuilder.BuildChartSection(title, cid, correct, wrong, pending));
        embedded.Add(new EmbeddedEmailImage(cid, "image/png", _piePng.RenderPng(correct, wrong, pending)));
    }

    private void AddDirectionVotePieIfAny(
        List<EmbeddedEmailImage> embedded,
        List<string> fragments,
        string title,
        int up,
        int down,
        int neutral)
    {
        if (up + down + neutral <= 0)
            return;

        var cid = $"ml-pie-b3dir-{fragments.Count}";
        fragments.Add(MlOutcomePieChartHtmlBuilder.BuildDirectionVoteChartSection(title, cid, up, down, neutral));
        embedded.Add(new EmbeddedEmailImage(cid, "image/png", _piePng.RenderDirectionVotePng(up, down, neutral)));
    }

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

    private static string SafeManualReportRangeFileSuffix(DateTimeOffset startUtc, DateTimeOffset endUtcExclusive) =>
        $"{startUtc.UtcDateTime:yyyyMMddTHHmmss}Z-{endUtcExclusive.UtcDateTime:yyyyMMddTHHmmss}Z";

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
                            _opts.BestOfThreeEnabled,
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
        Guid predictionId,
        CancellationToken ct)
    {
        var hist = await _broker
            .GetKiteHistoricalCandlesAsync(userId, instrumentToken, interval, null, null, ct)
            .ConfigureAwait(false);
        if (hist.Candles.Count == 0)
            return;
        await _predictionService
            .ResolvePredictionFromCandlesAsync(userId, predictionId, hist.Candles, hist.Interval, ct)
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
        var csv = BuildCsv(rows, symbolByToken);
        var csvBytes = Encoding.UTF8.GetBytes(csv);

        const string eodPieCid = "ml-pie-eod";
        var eodPie = _piePng.RenderPng(correct, wrong, pending);
        var chartFragment =
            MlOutcomePieChartHtmlBuilder.BuildChartSection($"Trader ML favorites — {ymd}", eodPieCid, correct, wrong, pending);

        var (eodVoteUp, eodVoteDown, eodVoteNeu) =
            PriceDirectionBestOfThree.SumVoteComponents(rows.Select(static r => r.Detail));
        var chartFragments = new List<string> { chartFragment };
        var embeddedList = new List<EmbeddedEmailImage> { new(eodPieCid, "image/png", eodPie) };
        AddDirectionVotePieIfAny(
            embeddedList,
            chartFragments,
            $"Trader ML favorites — best-of-3 direction votes — {ymd}",
            eodVoteUp,
            eodVoteDown,
            eodVoteNeu);

        var embeddedPies = embeddedList.ToArray();
        var htmlBody = MlOutcomePieChartHtmlBuilder.WrapHtmlDocument(chartFragments);

        var subject =
            $"Trader ML favorites — {ymd} (all engines; rows={rows.Count}; correct {correct}, wrong {wrong}, pending {pending})";
        var plainBody =
            $"Combined ML predictions (every registered automation engine; classic + LightGBM stores) on {ymd} ({tz.Id}).\r\n"
            + $"Totals: rows={rows.Count}, correct={correct}, wrong={wrong}, pending={pending}.\r\n"
            + "The HTML part embeds inline PNG chart(s): outcome pie"
            + (eodVoteUp + eodVoteDown + eodVoteNeu > 0 ? " plus best-of-3 direction vote pie." : ".")
            + " CSV spreadsheet is attached (engineModelId + model output id; best-of-3 columns when present).\r\n\r\n"
            + "Use a graphical mail client or the attachment for best results.";

        try
        {
            await _email
                .SendEmailAsync(
                    user.Email,
                    subject,
                    plainBody,
                    htmlBody,
                    embeddedPies,
                    new[] { new EmailAttachment($"ml-predictions-{ymd}.csv", "text/csv", csvBytes) },
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
            "predictedAtUtc,instrumentToken,tradingsymbol,exchange,interval,direction,confidence,outcome,refBarTimeUtc,refClose,nextBarTimeUtc,nextClose,modelId,engineModelId,b3Up,b3Down,b3Neutral,b3Votes,b3Majority,detail");
        foreach (var r in rows)
        {
            var tok = r.InstrumentToken.Trim();
            symbolByToken.TryGetValue(tok, out var sym);
            var hasB3 = PriceDirectionBestOfThree.TryParseDetailExtended(
                r.Detail,
                out var b3Up,
                out var b3Down,
                out var b3Neu,
                out var b3Votes,
                out var b3Maj);
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
                .Append(hasB3 ? b3Up.ToString(CultureInfo.InvariantCulture) : "")
                .Append(',')
                .Append(hasB3 ? b3Down.ToString(CultureInfo.InvariantCulture) : "")
                .Append(',')
                .Append(hasB3 ? b3Neu.ToString(CultureInfo.InvariantCulture) : "")
                .Append(',')
                .Append(Csv(hasB3 ? b3Votes : null))
                .Append(',')
                .Append(Csv(hasB3 ? b3Maj : null))
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
    string ReportRangeSummary,
    int PieChartsAttached,
    int TotalAttachmentsSent);
