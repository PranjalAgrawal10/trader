using System.Text.Json.Serialization;

namespace Trader.Api.Models;

/// <summary>Password was correct but the account email is still unconfirmed.</summary>
public sealed class LoginRequiresEmailVerificationResponse
{
    [JsonPropertyName("requires_email_verification")]
    public bool RequiresEmailVerification { get; init; } = true;
}
