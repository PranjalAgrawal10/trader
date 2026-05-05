using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trader.Api.Extensions;
using Trader.Api.Models;
using Trader.Application.Auth;

namespace Trader.Api.Controllers.V1;

[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "v1")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly IEmailOtpService _emailOtp;

    public AuthController(IAuthService auth, IEmailOtpService emailOtp)
    {
        _auth = auth;
        _emailOtp = emailOtp;
    }

    [AllowAnonymous]
    [HttpPost("email-otp/send")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SendEmailOtp([FromBody] EmailOtpSendRequest request, CancellationToken ct)
    {
        await _emailOtp.SendAsync(request, ct);
        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost("email-otp/verify")]
    [ProducesResponseType(typeof(EmailOtpVerifyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<EmailOtpVerifyResponse>> VerifyEmailOtp(
        [FromBody] EmailOtpVerifyRequest request,
        CancellationToken ct) =>
        Ok(await _emailOtp.VerifyAsync(request, ct));

    [AllowAnonymous]
    [HttpPost("verify-email")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthResponse>> VerifyRegistrationEmail(
        [FromBody] VerifyEmailRequest request,
        CancellationToken ct) =>
        Ok(await _auth.VerifyRegistrationEmailAsync(request, ct));

    [AllowAnonymous]
    [HttpPost("forgot-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        await _auth.ForgotPasswordAsync(request, ct);
        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost("reset-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        await _auth.ResetPasswordAsync(request, ct);
        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost("resend-login-otp")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResendLoginOtp([FromBody] ResendLoginOtpRequest request, CancellationToken ct)
    {
        await _auth.ResendLoginSecondFactorOtpAsync(request, ct);
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<ProfileResponse>> Me(CancellationToken ct) =>
        Ok(await _auth.GetProfileAsync(User.GetUserId(), ct));

    [AllowAnonymous]
    [HttpPost("register")]
    [ProducesResponseType(typeof(RegisterAckResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        var result = await _auth.RegisterAsync(request, ct);
        if (result is not RegistrationPendingEmailVerification)
            throw new InvalidOperationException("Unexpected registration outcome.");

        return Ok(new RegisterAckResponse());
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await _auth.LoginAsync(request, ct);
        return result switch
        {
            LoginSucceeded s => Ok(s.Auth),
            LoginRequiresTwoFactor r => Ok(new LoginTwoFactorChallengeResponse
            {
                TempToken = r.TwoFactorToken,
                SecondFactor = r.SecondFactorKind,
            }),
            LoginRequiresEmailVerification => Ok(new LoginRequiresEmailVerificationResponse()),
            LoginRejected => Unauthorized(),
            _ => Unauthorized(),
        };
    }
}
