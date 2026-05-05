using System.Text.Json.Serialization;

namespace Trader.Application.Auth;

public sealed class EmailOtpSendRequest
{
    [JsonPropertyName("email")]
    public string? Email { get; set; }
}

public sealed class EmailOtpVerifyRequest
{
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("otp")]
    public string? Otp { get; set; }
}

public sealed record EmailOtpVerifyResponse(
    [property: JsonPropertyName("verified")] bool Verified);
