namespace Trader.Application.Auth;

public sealed record RegisterRequest(string Email, string Password);

public sealed record LoginRequest(string Email, string Password);

public sealed record AuthResponse(string Token, Guid UserId, string Email, string Role);

public sealed record TwoFactorLoginRequest(string TwoFactorToken, string Code);

public sealed record TwoFactorEnrollmentResponse(string ManualEntryKey, string OtpAuthUri);

public sealed record TwoFactorConfirmRequest(string Code);

public sealed record TwoFactorDisableRequest(string Password, string Code);

public sealed record TwoFactorStatusResponse(bool TwoFactorEnabled, bool EnrollmentPending);
