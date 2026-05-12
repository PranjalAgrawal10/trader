using System.Text.Json.Serialization;

namespace Trader.Application.Wallet;

public sealed record WalletBalanceResponse(
    [property: JsonPropertyName("balance")] decimal Balance);

public sealed class WalletLoadRequest
{
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }
}
