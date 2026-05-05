using System.Text.Json.Serialization;

namespace Trader.Application.Auth;

public sealed record RegisterRequest(string Email, string Password);

public sealed record LoginRequest(string Email, string Password);

public sealed record AuthResponse(string Token, Guid UserId, string Email, string Role);

public sealed record ProfileResponse(
    [property: JsonPropertyName("user_id")] Guid UserId,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

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
    [property: JsonPropertyName("enrollment_pending")] bool EnrollmentPending);
