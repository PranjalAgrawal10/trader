using System.Text.Json.Serialization;

namespace Trader.Application.Auth;

public sealed record RegisterRequest(string Email, string Password);

public sealed record LoginRequest(string Email, string Password);

public sealed record AuthRequestContext(
    string? IpAddress,
    string? ForwardedFor,
    string? UserAgent,
    string? IpInfoJson);

public sealed record AuthResponse(string Token, Guid UserId, string Email, string Role);

public sealed record ProfileResponse(
    [property: JsonPropertyName("user_id")] Guid UserId,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("email_verified")] bool EmailVerified);

public sealed class VerifyEmailRequest
{
    [JsonPropertyName("token")]
    public string? Token { get; set; }
}

public sealed class ForgotPasswordRequest
{
    [JsonPropertyName("email")]
    public string? Email { get; set; }
}

public sealed class ResetPasswordRequest
{
    /// <summary>Legacy link-based reset (optional when <see cref="Otp"/> is used).</summary>
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("otp")]
    public string? Otp { get; set; }

    [JsonPropertyName("new_password")]
    public string? NewPassword { get; set; }
}

public sealed class ResendLoginOtpRequest
{
    [JsonPropertyName("temp_token")]
    public string? TempToken { get; set; }
}

public sealed class TwoFactorVerifyLoginRequest
{
    [JsonPropertyName("temp_token")]
    public string? TempToken { get; set; }

    public string? TwoFactorToken { get; set; }

    [JsonPropertyName("otp")]
    public string? Otp { get; set; }

    public string? Code { get; set; }

    public string ResolveTicket() =>
        (!string.IsNullOrWhiteSpace(TempToken) ? TempToken : TwoFactorToken)?.Trim() ?? "";

    public string ResolveCode() => (!string.IsNullOrWhiteSpace(Otp) ? Otp : Code)?.Trim() ?? "";
}

public sealed record TwoFactorEnrollmentResponse(
    [property: JsonPropertyName("manual_entry_key")] string ManualEntryKey,
    [property: JsonPropertyName("otp_auth_uri")] string OtpAuthUri);

public sealed class TwoFactorConfirmRequest
{
    [JsonPropertyName("otp")]
    public string? Otp { get; set; }

    public string? Code { get; set; }

    public string ResolveCode() => (!string.IsNullOrWhiteSpace(Otp) ? Otp : Code)?.Trim() ?? "";
}

public sealed class TwoFactorDisableRequest
{
    public string? Password { get; set; }

    [JsonPropertyName("otp")]
    public string? Otp { get; set; }

    public string? Code { get; set; }

    public string? ResolveOtpOrCode() => (!string.IsNullOrWhiteSpace(Otp) ? Otp : Code)?.Trim();
}

public sealed record TwoFactorEnrollmentConfirmResult(
    [property: JsonPropertyName("recovery_codes")] IReadOnlyList<string> RecoveryCodes);

public sealed record TwoFactorStatusResponse(
    [property: JsonPropertyName("two_factor_enabled")] bool TwoFactorEnabled,
    [property: JsonPropertyName("enrollment_pending")] bool EnrollmentPending,
    [property: JsonPropertyName("second_factor_method")] string SecondFactorMethod);
