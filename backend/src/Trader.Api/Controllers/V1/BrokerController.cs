using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using Trader.Api.Extensions;
using Trader.Application.Broker;
using Trader.Application.Configuration;

namespace Trader.Api.Controllers.V1;

[ApiController]
[Route("api/v{version:apiVersion}/broker")]
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "v1")]
public sealed class BrokerController : ControllerBase
{
    private const string KiteOAuthStateCookie = "Trader.KiteOAuth.State";

    private readonly IBrokerService _broker;
    private readonly IOptions<ZerodhaKiteOptions> _kiteOptions;
    private readonly ILogger<BrokerController> _logger;

    public BrokerController(
        IBrokerService broker,
        IOptions<ZerodhaKiteOptions> kiteOptions,
        ILogger<BrokerController> logger)
    {
        _broker = broker;
        _kiteOptions = kiteOptions;
        _logger = logger;
    }

    [Authorize]
    [HttpGet("status")]
    public async Task<ActionResult<BrokerStatusDto>> Status(CancellationToken ct)
    {
        var dto = await _broker.GetStatusAsync(User.GetUserId(), ct);
        return Ok(dto);
    }

    [Authorize]
    [HttpPost("complete-setup")]
    public async Task<ActionResult<BrokerStatusDto>> CompleteSetup(CancellationToken ct)
    {
        await _broker.CompleteSetupAsync(User.GetUserId(), ct);
        var dto = await _broker.GetStatusAsync(User.GetUserId(), ct);
        return Ok(dto);
    }

    [Authorize]
    [HttpPost("disconnect")]
    public async Task<ActionResult<BrokerStatusDto>> Disconnect(CancellationToken ct)
    {
        var dto = await _broker.DisconnectAsync(User.GetUserId(), ct);
        return Ok(dto);
    }

    [Authorize]
    [HttpGet("kite/instruments/fno-commodities")]
    public async Task<ActionResult<KiteFnoCommodityListsDto>> KiteFnoCommodityInstruments(CancellationToken ct)
    {
        var dto = await _broker.GetKiteFnoCommodityInstrumentsAsync(User.GetUserId(), ct);
        return Ok(dto);
    }

    /// <summary>Contracts with largest % gain vs prior close among the capped Browse universe (quotes via Kite /quote/ohlc).</summary>
    [Authorize]
    [HttpGet("kite/instruments/today-top-performers")]
    public async Task<ActionResult<KiteTodayTopPerformersDto>> KiteTodayTopPerformers([FromQuery] int take, CancellationToken ct)
    {
        var clamped = take <= 0 ? 15 : take;
        try
        {
            var dto = await _broker.GetKiteTodayTopPerformersAsync(User.GetUserId(), clamped, ct);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Kite today-top-performers failed.");
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>Streaming substring search across Kite instrument CSVs for F&amp;O (NFO+BFO), MCX, or NSE/BSE spot (cash equities + index listings such as SENSEX — <c>segment=Spot</c>).</summary>
    [Authorize]
    [HttpGet("kite/instruments/search")]
    public async Task<ActionResult<KiteInstrumentSearchDto>> SearchKiteInstruments(
        [FromQuery(Name = "q")] string? q,
        [FromQuery] KiteInstrumentSearchSegment segment,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length > 128)
        {
            return Problem(
                title: "Invalid query",
                detail: "Provide non-empty q (max 128 characters).",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var dto = await _broker.SearchKiteInstrumentsAsync(User.GetUserId(), q, segment, ct);
        return Ok(dto);
    }

    /// <summary>Kite historical OHLCV. <c>interval</c> values: <c>1m</c> … <c>1w</c> (includes <c>4h</c> / <c>1w</c> derived server-side from Kite 60m/d candles; see README). Optional ISO <c>from</c>/<c>to</c> (UTC); otherwise defaults apply. Responses include <c>sma20</c>, <c>ema9</c>, <c>ema21</c>, <c>srSupport</c>, <c>srResistance</c> (nullable); the server requests extra history before <c>from</c> so overlays are warmed for the visible window.</summary>
    [Authorize]
    [HttpGet("kite/historical-candles")]
    public async Task<ActionResult<KiteHistoricalCandlesDto>> KiteHistoricalCandles(
        [FromQuery(Name = "instrumentToken")] string? instrumentToken,
        [FromQuery] string? interval,
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(instrumentToken))
        {
            return Problem(
                title: "Invalid instrument",
                detail: "Provide instrumentToken from the Kite instrument list.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(interval))
        {
            return Problem(
                title: "Invalid interval",
                detail: "Provide interval (e.g. 5m, 1d).",
                statusCode: StatusCodes.Status400BadRequest);
        }

        DateTimeOffset? fromUtc = null;
        DateTimeOffset? toUtc = null;
        if (!string.IsNullOrWhiteSpace(from))
        {
            if (!DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var f))
            {
                return Problem(
                    title: "Invalid from",
                    detail: "Use an ISO 8601 instant for from.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            fromUtc = f;
        }

        if (!string.IsNullOrWhiteSpace(to))
        {
            if (!DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t))
            {
                return Problem(
                    title: "Invalid to",
                    detail: "Use an ISO 8601 instant for to.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            toUtc = t;
        }

        try
        {
            var dto = await _broker.GetKiteHistoricalCandlesAsync(
                User.GetUserId(),
                instrumentToken,
                interval,
                fromUtc,
                toUtc,
                ct);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>Saved favorite Kite instruments for the current user (persisted in the database).</summary>
    [Authorize]
    [HttpGet("kite/favorites")]
    public async Task<ActionResult<KiteFavoriteInstrumentsListDto>> KiteFavorites(CancellationToken ct)
    {
        try
        {
            var dto = await _broker.GetKiteFavoriteInstrumentsAsync(User.GetUserId(), ct);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [Authorize]
    [HttpPost("kite/favorites")]
    public async Task<IActionResult> AddKiteFavorite([FromBody] KiteInstrumentListItemDto? body, CancellationToken ct)
    {
        if (body is null)
        {
            return Problem(
                title: "Invalid body",
                detail: "Send a JSON instrument row (instrumentToken, tradingsymbol, exchange, …).",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            await _broker.AddKiteFavoriteInstrumentAsync(User.GetUserId(), body, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [Authorize]
    [HttpDelete("kite/favorites")]
    public async Task<IActionResult> RemoveKiteFavorite(
        [FromQuery(Name = "instrumentToken")] string? instrumentToken,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(instrumentToken))
        {
            return Problem(
                title: "Invalid instrument",
                detail: "Provide instrumentToken.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            await _broker.RemoveKiteFavoriteInstrumentAsync(User.GetUserId(), instrumentToken, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>Persisted Kite instruments page chart toolbar (interval, range, line/bar) and optional per-instrument zoom map. Requires Bearer auth.</summary>
    [Authorize]
    [HttpGet("kite/instruments/chart-settings")]
    public async Task<ActionResult<KiteInstrumentsChartSettingsDto>> GetKiteInstrumentsChartSettings(CancellationToken ct)
    {
        try
        {
            var dto = await _broker.GetKiteInstrumentsChartSettingsAsync(User.GetUserId(), ct);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [Authorize]
    [HttpPut("kite/instruments/chart-settings")]
    public async Task<IActionResult> PutKiteInstrumentsChartSettings(
        [FromBody] KiteInstrumentsChartSettingsDto? body,
        CancellationToken ct)
    {
        if (body is null)
        {
            return Problem(
                title: "Invalid body",
                detail: "Send JSON with interval, rangePreset, and graphType.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            await _broker.SaveKiteInstrumentsChartSettingsAsync(User.GetUserId(), body, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [Authorize]
    [HttpPut("kite/instruments/favorite-ml-automation")]
    public async Task<IActionResult> PutFavoriteMlAutomation([FromBody] FavoriteMlAutomationPutDto? body, CancellationToken ct)
    {
        if (body is null)
        {
            return Problem(
                title: "Invalid body",
                detail: "Send JSON { \"enabled\": true|false }.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            await _broker.SetFavoriteMlAutomationEnabledAsync(User.GetUserId(), body.Enabled, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [Authorize]
    [HttpPut("kite/instruments/chart-zoom")]
    public async Task<IActionResult> PutKiteInstrumentsChartZoom(
        [FromBody] KiteInstrumentsChartZoomPutDto? body,
        CancellationToken ct)
    {
        if (body is null)
        {
            return Problem(
                title: "Invalid body",
                detail: "Send JSON with instrumentToken and optional visibleBars (null to clear zoom for that token).",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            await _broker.SaveKiteInstrumentsChartZoomAsync(User.GetUserId(), body, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [Authorize]
    [HttpPut("kite/instruments/chart-interval")]
    public async Task<IActionResult> PutKiteInstrumentsChartInterval(
        [FromBody] KiteInstrumentsChartIntervalPutDto? body,
        CancellationToken ct)
    {
        if (body is null)
        {
            return Problem(
                title: "Invalid body",
                detail: "Send JSON with instrumentToken and interval (UI code 1m…1d, or null to use the page default for that token).",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            await _broker.SaveKiteInstrumentsChartIntervalOverrideAsync(User.GetUserId(), body, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [Authorize]
    [HttpGet("kite/login-url")]
    public async Task<ActionResult<KiteLoginUrlDto>> KiteLoginUrl(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var r = await _broker.GetKiteLoginUrlAsync(userId, ct);
        Response.Cookies.Append(KiteOAuthStateCookie, r.PendingOAuthStateKey, BuildKiteOAuthCookieOptions());
        return Ok(new KiteLoginUrlDto(r.LoginUrl));
    }

    /// <summary>Kite redirect target — register this exact URL on the Kite developer console.</summary>
    [AllowAnonymous]
    [HttpGet("kite/callback")]
    public async Task<IActionResult> KiteCallback(
        [FromQuery] string? request_token,
        [FromQuery] string? status,
        [FromQuery] string? state,
        [FromQuery(Name = "trader_oauth")] string? traderOauth,
        CancellationToken ct)
    {
        var redirectBase = _kiteOptions.Value.PostLoginRedirectUrl.TrimEnd('/');
        var cookieOptions = BuildKiteOAuthCookieOptions();

        var traderFromQuery = FirstNonEmpty(
            traderOauth,
            Request.Query["trader_oauth"].FirstOrDefault());
        var effectiveState = FirstNonEmpty(state, traderFromQuery, Request.Cookies[KiteOAuthStateCookie]);

        Response.Cookies.Delete(KiteOAuthStateCookie, new CookieOptions
        {
            Path = cookieOptions.Path,
            HttpOnly = true,
            Secure = cookieOptions.Secure,
            SameSite = cookieOptions.SameSite,
        });

        // Kite v3 docs: redirect includes request_token; `status` query is not always sent. Only fail if status is present and not success.
        var statusPresentAndFailed = !string.IsNullOrEmpty(status)
            && !string.Equals(status, "success", StringComparison.OrdinalIgnoreCase);

        if (statusPresentAndFailed
            || string.IsNullOrEmpty(request_token)
            || string.IsNullOrWhiteSpace(effectiveState))
        {
            var cookieHadState = Request.Cookies.ContainsKey(KiteOAuthStateCookie);
            var queryKeys = string.Join(',', Request.Query.Keys);
            _logger.LogWarning(
                "Kite OAuth callback rejected: StatusPresentAndFailed={StatusFailed}, HasRequestToken={HasToken}, HasStateQuery={HasStateQ}, HasTraderOauth={HasTraderOauth}, CookieHadFallback={CookieFallback}, CallbackStatus={KiteStatus}, QueryKeys={QueryKeys}",
                statusPresentAndFailed,
                !string.IsNullOrEmpty(request_token),
                !string.IsNullOrWhiteSpace(state),
                !string.IsNullOrWhiteSpace(traderFromQuery),
                cookieHadState,
                status ?? "(null)",
                queryKeys);

            string userMessage = statusPresentAndFailed
                ? "Login was cancelled or failed at Zerodha."
                : string.IsNullOrEmpty(request_token)
                    ? "Missing request token from Kite. Check the redirect URL in the developer console."
                    : string.IsNullOrWhiteSpace(effectiveState)
                        ? "Missing OAuth state. Use the same browser session, or ensure the Kite redirect URL matches this API (see README)."
                        : "Login was cancelled or failed.";

            return Redirect($"{redirectBase}?kite=error&message={Uri.EscapeDataString(userMessage)}");
        }

        try
        {
            await _broker.CompleteKiteOAuthAsync(request_token, effectiveState, ct);
            return Redirect($"{redirectBase}?kite=success");
        }
        catch (InvalidOperationException ex)
        {
            return Redirect($"{redirectBase}?kite=error&message={Uri.EscapeDataString(ex.Message)}");
        }
    }

    private CookieOptions BuildKiteOAuthCookieOptions()
    {
        // SPA (other origin) calls /broker/kite/login-url with credentials: SameSite=None + Secure helps the fallback cookie stick in production HTTPS.
        var https = Request.IsHttps;
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = https,
            SameSite = https ? SameSiteMode.None : SameSiteMode.Lax,
            Path = "/",
            MaxAge = TimeSpan.FromMinutes(20),
        };
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }

        return null;
    }
}
