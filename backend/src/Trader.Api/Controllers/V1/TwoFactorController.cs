using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trader.Api.Extensions;
using Trader.Application.Auth;

namespace Trader.Api.Controllers.V1;

/// <summary>Spec-aligned 2FA routes (snake_case JSON). Password login stays on <see cref="AuthController"/>.</summary>
[ApiController]
[Route("api/v{version:apiVersion}/2fa")]
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "v1")]
public sealed class TwoFactorController : ControllerBase
{
    private readonly IAuthService _auth;

    public TwoFactorController(IAuthService auth)
    {
        _auth = auth;
    }

    [Authorize]
    [HttpPost("setup")]
    public async Task<ActionResult<TwoFactorEnrollmentResponse>> Setup(CancellationToken ct) =>
        Ok(await _auth.BeginTwoFactorEnrollmentAsync(User.GetUserId(), ct));

    [Authorize]
    [HttpPost("verify-setup")]
    public async Task<ActionResult<TwoFactorEnrollmentConfirmResult>> VerifySetup(
        [FromBody] TwoFactorConfirmRequest request,
        CancellationToken ct) =>
        Ok(await _auth.ConfirmTwoFactorEnrollmentAsync(User.GetUserId(), request, ct));

    [Authorize]
    [HttpPost("cancel-setup")]
    public async Task<IActionResult> CancelSetup(CancellationToken ct)
    {
        await _auth.CancelTwoFactorEnrollmentAsync(User.GetUserId(), ct);
        return NoContent();
    }

    [Authorize]
    [HttpGet("status")]
    public async Task<ActionResult<TwoFactorStatusResponse>> Status(CancellationToken ct) =>
        Ok(await _auth.GetTwoFactorStatusAsync(User.GetUserId(), ct));

    [AllowAnonymous]
    [HttpPost("verify-login")]
    public async Task<ActionResult<AuthResponse>> VerifyLogin(
        [FromBody] TwoFactorVerifyLoginRequest request,
        CancellationToken ct) =>
        Ok(await _auth.CompleteTwoFactorLoginAsync(request, ct));

    [Authorize]
    [HttpPost("disable")]
    public async Task<IActionResult> Disable([FromBody] TwoFactorDisableRequest request, CancellationToken ct)
    {
        await _auth.DisableTwoFactorAsync(User.GetUserId(), request, ct);
        return NoContent();
    }
}
