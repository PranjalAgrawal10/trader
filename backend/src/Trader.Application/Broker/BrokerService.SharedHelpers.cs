using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Trader.Application.Abstractions.Persistence;
using Trader.Application.Configuration;
using Trader.Application.Wallet;
using Trader.Domain.Entities;


namespace Trader.Application.Broker;

public sealed partial class BrokerService
{
    private async Task RequireUserExistsAsync(Guid userId, CancellationToken ct)
    {
        var snapshot = await _brokerSetup.GetSnapshotAsync(userId, ct).ConfigureAwait(false);
        if (snapshot is null)
            throw new InvalidOperationException("User not found.");
    }

    private static KiteInstrumentListItemDto MapTradingLockToDto(KiteTradingLockInstrument x) =>
        new(
            x.InstrumentToken,
            x.Tradingsymbol,
            x.Exchange,
            x.Name,
            x.InstrumentType,
            x.Segment,
            x.Expiry,
            x.Strike,
            x.LotSize);

    private static KiteInstrumentListItemDto MapFavoriteToDto(KiteFavoriteInstrument x) =>
        new(
            x.InstrumentToken,
            x.Tradingsymbol,
            x.Exchange,
            x.Name,
            x.InstrumentType,
            x.Segment,
            x.Expiry,
            x.Strike,
            x.LotSize);

    /// <summary>Whole minutes for API/SPA; legacy DB values use ceiling so e.g. 90s → 2.</summary>
    private static int? ThrottleSecondsToApiMinutes(int? seconds)
    {
        if (seconds is null or <= 0)
            return null;
        return (int)Math.Ceiling(seconds.Value / 60.0);
    }

    private static string? NullableNorm(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var t = value.Trim();
        return t.Length == 0 ? null : t;
    }

    private static string NormalizeRequired(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{fieldName} is required.");
        return value.Trim();
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static decimal? NormalizeNullablePrice(decimal? value)
    {
        if (!value.HasValue || value.Value <= 0m)
            return null;
        return value.Value;
    }

    private static string NormalizeValidity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "DAY";
        return value.Trim().ToUpperInvariant();
    }
    private static void ValidateOrderTypePayload(string orderTypeRaw, decimal? price, decimal? triggerPrice)
    {
        var orderType = orderTypeRaw.Trim().ToUpperInvariant();
        if (orderType == "LIMIT" && price is null)
            throw new InvalidOperationException("LIMIT order requires price.");
        if (orderType == "SL" && (price is null || triggerPrice is null))
            throw new InvalidOperationException("SL order requires both price and triggerPrice.");
        if (orderType == "SL-M" && triggerPrice is null)
            throw new InvalidOperationException("SL-M order requires triggerPrice.");
    }

    private static string NormalizeScalperIntervalOrDefault(string? intervalRaw)
    {
        var x = string.IsNullOrWhiteSpace(intervalRaw) ? "" : intervalRaw.Trim().ToLowerInvariant();
        return x switch
        {
            "1m" or "3m" or "5m" => x,
            _ => "1m",
        };
    }

    private static string NormalizeScalperRangePresetOrDefault(string? presetRaw)
    {
        var x = string.IsNullOrWhiteSpace(presetRaw) ? "" : presetRaw.Trim().ToLowerInvariant();
        return x switch
        {
            "last15m" or "last30m" or "last1h" or "last5h" or "last1d" or "last3d" => x,
            _ => "last3d",
        };
    }

    private static string NormalizeScalperGraphTypeOrDefault(string? graphTypeRaw)
    {
        var x = string.IsNullOrWhiteSpace(graphTypeRaw) ? "" : graphTypeRaw.Trim().ToLowerInvariant();
        return x switch
        {
            "candlestick" or "line" or "bar" => x,
            _ => "candlestick",
        };
    }

    private static decimal NormalizeScalperPointsOrDefault(decimal? raw, decimal fallback)
    {
        var v = raw ?? fallback;
        if (v <= 0m)
            return fallback;
        if (v > 1_000_000m)
            return 1_000_000m;
        return decimal.Round(v, 2, MidpointRounding.AwayFromZero);
    }

    private static string NormalizeUiChartInterval(string interval) => ChartUiIntervals.Normalize(interval);

    private static string NormalizeChartRangePreset(string preset)
    {
        var t = preset.Trim().ToLowerInvariant();
        return t switch
        {
            "auto" or "last5m" or "last10m" or "last15m" or "last30m" or "last1h" or "last5h" or "last10h"
                or "last1d" or "last2d" or "last5d" or "last1mo" => t,
            _ => throw new InvalidOperationException(
                "rangePreset must be one of: auto, last5m, last10m, last15m, last30m, last1h, last5h, last10h, last1d, last2d, last5d, last1mo."),
        };
    }
    private async Task<(string ApiKey, string AccessToken)> RequireKiteInstrumentSessionAsync(
        Guid userId,
        CancellationToken ct)
    {
        var userExists = await _brokerSetup.GetSnapshotAsync(userId, ct).ConfigureAwait(false);
        if (userExists is null)
            throw new InvalidOperationException("User not found.");

        var accessToken = await _brokerSetup.GetKiteAccessTokenAsync(userId, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(accessToken))
            throw new InvalidOperationException("No valid Kite session. Reconnect Zerodha.");

        var apiKey = _kiteOptions.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "Zerodha Kite is not configured. Set environment variable ZerodhaKite__ApiKey (see README).");

        return (apiKey, accessToken);
    }
}
