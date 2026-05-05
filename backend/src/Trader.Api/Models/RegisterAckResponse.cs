using System.Text.Json.Serialization;

namespace Trader.Api.Models;

/// <summary>Registration accepted; JWT is issued only after the user verifies email.</summary>
public sealed class RegisterAckResponse
{
    [JsonPropertyName("email_verification_required")]
    public bool EmailVerificationRequired { get; init; } = true;
}
