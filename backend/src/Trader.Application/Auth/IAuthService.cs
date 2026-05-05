namespace Trader.Application.Auth;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default);

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
