using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Trader.Application.Broker;
using Trader.Application.Configuration;

namespace Trader.Infrastructure.Broker;

public sealed class KiteSessionExchange : IKiteSessionExchange
{
    private readonly HttpClient _http;
    private readonly IOptions<ZerodhaKiteOptions> _options;

    public KiteSessionExchange(HttpClient http, IOptions<ZerodhaKiteOptions> options)
    {
        _http = http;
        _options = options;
    }

    public async Task<KiteSessionExchangeResult> ExchangeAsync(string requestToken, CancellationToken ct)
    {
        var opt = _options.Value;
        if (string.IsNullOrWhiteSpace(opt.ApiKey) || string.IsNullOrWhiteSpace(opt.ApiSecret))
        {
            return new KiteSessionExchangeResult(
                false,
                "Kite API credentials are not configured. Set ZerodhaKite__ApiKey and ZerodhaKite__ApiSecret (see README).",
                null,
                null,
                null);
        }

        var checksum = ComputeChecksum(opt.ApiKey, requestToken, opt.ApiSecret);
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["api_key"] = opt.ApiKey,
            ["request_token"] = requestToken,
            ["checksum"] = checksum,
        });

        using var response = await _http.PostAsync("session/token", content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var status = root.GetProperty("status").GetString();
            if (!string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
            {
                var msg = root.TryGetProperty("message", out var m) ? m.GetString() : "Kite token exchange failed.";
                return new KiteSessionExchangeResult(false, msg, null, null, null);
            }

            var data = root.GetProperty("data");
            var access = data.GetProperty("access_token").GetString();
            data.TryGetProperty("refresh_token", out var rtEl);
            var refresh = rtEl.ValueKind == JsonValueKind.String ? rtEl.GetString() : null;

            string? kiteUserId = null;
            if (data.TryGetProperty("user_id", out var uidEl))
            {
                kiteUserId = uidEl.ValueKind switch
                {
                    JsonValueKind.String => uidEl.GetString(),
                    JsonValueKind.Number => uidEl.GetInt64().ToString(),
                    _ => uidEl.GetRawText().Trim('"'),
                };
            }

            if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(kiteUserId))
            {
                return new KiteSessionExchangeResult(false, "Unexpected response from Kite.", null, null, null);
            }

            return new KiteSessionExchangeResult(true, null, access, refresh, kiteUserId);
        }
        catch (JsonException)
        {
            return new KiteSessionExchangeResult(false, "Could not parse Kite response.", null, null, null);
        }
    }

    private static string ComputeChecksum(string apiKey, string requestToken, string apiSecret)
    {
        var input = $"{apiKey}{requestToken}{apiSecret}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
