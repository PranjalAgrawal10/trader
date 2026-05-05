using System.Text.Json.Serialization;

namespace Trader.Api.Models;

/// <summary>Returned from password login when the account requires a second factor (opaque ticket).</summary>
public sealed class LoginTwoFactorChallengeResponse
{
    [JsonPropertyName("requires_2fa")]
    public bool RequiresTwoFactor { get; init; } = true;

    [JsonPropertyName("temp_token")]
    public required string TempToken { get; init; }
}
