using Trader.Domain.Enums;

namespace Trader.Application.Auth;

public interface IEmailOtpService
{
    Task SendAsync(EmailOtpSendRequest request, CancellationToken ct = default);

    /// <summary>Sends a 6-digit OTP for the given purpose (shared HTML + plain pipeline).</summary>
    Task SendOtpAsync(string normalizedEmail, EmailOtpPurpose purpose, CancellationToken ct = default);

    Task<EmailOtpVerifyResponse> VerifyAsync(
        EmailOtpVerifyRequest request,
        EmailOtpPurpose purpose = EmailOtpPurpose.Generic,
        CancellationToken ct = default);
}
