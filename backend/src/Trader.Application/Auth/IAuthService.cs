namespace Trader.Application.Auth;

public interface IAuthService
{
    Task<RegistrationResult> RegisterAsync(RegisterRequest request, CancellationToken ct = default);

    Task<AuthResponse> VerifyRegistrationEmailAsync(VerifyEmailRequest request, CancellationToken ct = default);

    Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default);

    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default);

    Task ResendLoginSecondFactorOtpAsync(ResendLoginOtpRequest request, CancellationToken ct = default);

    Task EnableEmailSignInSecondFactorAsync(Guid userId, CancellationToken ct = default);

    Task<ProfileResponse> GetProfileAsync(Guid userId, CancellationToken ct = default);

    Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken ct = default);

    Task<AuthResponse> CompleteTwoFactorLoginAsync(TwoFactorVerifyLoginRequest request, CancellationToken ct = default);

    Task<TwoFactorEnrollmentResponse> BeginTwoFactorEnrollmentAsync(Guid userId, CancellationToken ct = default);

    Task<TwoFactorEnrollmentConfirmResult> ConfirmTwoFactorEnrollmentAsync(
        Guid userId,
        TwoFactorConfirmRequest request,
        CancellationToken ct = default);

    Task CancelTwoFactorEnrollmentAsync(Guid userId, CancellationToken ct = default);

    Task DisableTwoFactorAsync(Guid userId, TwoFactorDisableRequest request, CancellationToken ct = default);

    Task<TwoFactorStatusResponse> GetTwoFactorStatusAsync(Guid userId, CancellationToken ct = default);
}
