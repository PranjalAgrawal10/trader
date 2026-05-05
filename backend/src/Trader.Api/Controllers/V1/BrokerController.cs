using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

    /// <summary>Streaming substring search across Kite instrument CSVs for F&amp;O (NFO+BFO) or MCX. Use when the preview list has no match.</summary>
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
