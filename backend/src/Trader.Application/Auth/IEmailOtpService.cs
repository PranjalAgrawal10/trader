namespace Trader.Application.Auth;

public interface IEmailOtpService
{
    Task SendAsync(EmailOtpSendRequest request, CancellationToken ct = default);

    /// <summary>OTP for password + second-factor sign-in when the account uses email codes (distinct send throttle).</summary>
    Task SendLoginSecondFactorAsync(string normalizedEmail, CancellationToken ct = default);

    Task<EmailOtpVerifyResponse> VerifyAsync(EmailOtpVerifyRequest request, CancellationToken ct = default);
}
