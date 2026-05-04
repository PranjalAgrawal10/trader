using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trader.Api.Extensions;
using Trader.Application.Auth;

namespace Trader.Api.Controllers.V1;

[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "v1")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth)
    {
        _auth = auth;
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        var result = await _auth.RegisterAsync(request, ct);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await _auth.LoginAsync(request, ct);
        return result switch
        {
            LoginSucceeded s => Ok(s.Auth),
            LoginRequiresTwoFactor r => Ok(new { requiresTwoFactor = true, twoFactorToken = r.TwoFactorToken }),
            LoginRejected => Unauthorized(),
            _ => Unauthorized(),
        };
    }

    [AllowAnonymous]
    [HttpPost("login/2fa")]
    public async Task<ActionResult<AuthResponse>> CompleteTwoFactorLogin([FromBody] TwoFactorLoginRequest request, CancellationToken ct)
    {
        var auth = await _auth.CompleteTwoFactorLoginAsync(request, ct);
        return auth is null ? Unauthorized() : Ok(auth);
    }

    [Authorize]
    [HttpGet("2fa/status")]
    public async Task<ActionResult<TwoFactorStatusResponse>> TwoFactorStatus(CancellationToken ct) =>
        Ok(await _auth.GetTwoFactorStatusAsync(User.GetUserId(), ct));

    [Authorize]
    [HttpPost("2fa/enrollment/begin")]
    public async Task<ActionResult<TwoFactorEnrollmentResponse>> BeginTwoFactorEnrollment(CancellationToken ct) =>
        Ok(await _auth.BeginTwoFactorEnrollmentAsync(User.GetUserId(), ct));

    [Authorize]
    [HttpPost("2fa/enrollment/confirm")]
    public async Task<IActionResult> ConfirmTwoFactorEnrollment([FromBody] TwoFactorConfirmRequest request, CancellationToken ct)
    {
        await _auth.ConfirmTwoFactorEnrollmentAsync(User.GetUserId(), request, ct);
        return NoContent();
    }

    [Authorize]
    [HttpPost("2fa/enrollment/cancel")]
    public async Task<IActionResult> CancelTwoFactorEnrollment(CancellationToken ct)
    {
        await _auth.CancelTwoFactorEnrollmentAsync(User.GetUserId(), ct);
        return NoContent();
    }

    [Authorize]
    [HttpPost("2fa/disable")]
    public async Task<IActionResult> DisableTwoFactor([FromBody] TwoFactorDisableRequest request, CancellationToken ct)
    {
        await _auth.DisableTwoFactorAsync(User.GetUserId(), request, ct);
        return NoContent();
    }
}
