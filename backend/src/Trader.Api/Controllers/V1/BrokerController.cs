using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
    private readonly IKiteOAuthStateCodec _stateCodec;

    public BrokerController(
        IBrokerService broker,
        IOptions<ZerodhaKiteOptions> kiteOptions,
        IKiteOAuthStateCodec stateCodec)
    {
        _broker = broker;
        _kiteOptions = kiteOptions;
        _stateCodec = stateCodec;
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

    [Authorize]
    [HttpGet("kite/login-url")]
    public async Task<ActionResult<KiteLoginUrlDto>> KiteLoginUrl(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var dto = await _broker.GetKiteLoginUrlAsync(userId, ct);
        // Zerodha sometimes omits `state` on the callback query string; keep a signed fallback on the API origin.
        Response.Cookies.Append(KiteOAuthStateCookie, _stateCodec.Encode(userId), BuildKiteOAuthCookieOptions());
        return Ok(dto);
    }

    /// <summary>Kite redirect target — register this exact URL on the Kite developer console.</summary>
    [AllowAnonymous]
    [HttpGet("kite/callback")]
    public async Task<IActionResult> KiteCallback(
        [FromQuery] string? request_token,
        [FromQuery] string? status,
        [FromQuery] string? state,
        CancellationToken ct)
    {
        var redirectBase = _kiteOptions.Value.PostLoginRedirectUrl.TrimEnd('/');
        var cookieOptions = BuildKiteOAuthCookieOptions();
        var effectiveState = string.IsNullOrWhiteSpace(state)
            ? Request.Cookies[KiteOAuthStateCookie]
            : state;

        Response.Cookies.Delete(KiteOAuthStateCookie, new CookieOptions
        {
            Path = cookieOptions.Path,
            HttpOnly = true,
            Secure = cookieOptions.Secure,
            SameSite = cookieOptions.SameSite,
        });

        if (!string.Equals(status, "success", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(request_token)
            || string.IsNullOrWhiteSpace(effectiveState))
        {
            return Redirect($"{redirectBase}?kite=error&message={Uri.EscapeDataString("Login was cancelled or failed.")}");
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
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/api/v1/broker",
            MaxAge = TimeSpan.FromMinutes(20),
        };
    }
}
