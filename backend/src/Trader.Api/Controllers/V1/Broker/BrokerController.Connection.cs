using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using Trader.Api.Extensions;
using Trader.Api.Routing;
using Trader.Application.Broker;
using Trader.Application.Configuration;


namespace Trader.Api.Controllers.V1;

public sealed partial class BrokerController
{
    [Authorize]
    [HttpGet("status")]
    public async Task<ActionResult<BrokerStatusDto>> Status(CancellationToken ct)
    {
        var dto = await _broker.GetStatusAsync(User.GetUserId(), ct);
        return Ok(dto);
    }

    [Authorize]
    [HttpGet("providers")]
    public async Task<ActionResult<IReadOnlyList<BrokerProviderAvailabilityDto>>> Providers(CancellationToken ct)
    {
        var providers = await _broker.GetOrderBrokerProvidersAsync(User.GetUserId(), ct);
        return Ok(providers);
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
    public async Task<ActionResult<BrokerStatusDto>> Disconnect([FromQuery] string? broker, CancellationToken ct)
    {
        var dto = await _broker.DisconnectAsync(User.GetUserId(), broker, ct);
        return Ok(dto);
    }

    [Authorize]
    [HttpPost("groww/connect")]
    public async Task<ActionResult<BrokerStatusDto>> ConnectGroww([FromBody] GrowwConnectRequestDto? body, CancellationToken ct)
    {
        if (body is null)
        {
            return Problem(
                title: "Invalid body",
                detail: "Send accessToken, or apiKey with apiSecret/totp to create a Groww token.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var dto = await _broker.ConnectGrowwAsync(User.GetUserId(), body, ct);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [Authorize]
    [HttpPut("active-provider")]
    public async Task<ActionResult<BrokerStatusDto>> SetActiveProvider([FromBody] BrokerSelectionPutDto? body, CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Broker))
        {
            return Problem(
                title: "Invalid body",
                detail: "Send JSON with broker key (e.g. zerodha or groww).",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var dto = await _broker.SetActiveBrokerAsync(User.GetUserId(), body.Broker, ct);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

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
