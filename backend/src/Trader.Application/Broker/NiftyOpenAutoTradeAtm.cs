using System.Globalization;

namespace Trader.Application.Broker;

/// <summary>Pure helpers for NIFTY spot/option ATM selection and lot sizing.</summary>
public static class NiftyOpenAutoTradeAtm
{
    public sealed record OptionCandidate(
        string InstrumentToken,
        string Tradingsymbol,
        string Exchange,
        decimal Strike,
        string? Expiry,
        int LotSize,
        string InstrumentType);

    public static KiteInstrumentListItemDto? ChooseNiftySpotRow(
        IReadOnlyList<KiteInstrumentListItemDto> rows,
        string preferredSpotExchange)
    {
        static string Norm(string? s) => (s ?? string.Empty).Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();

        const string target = "NIFTY50";
        var exact = rows.FirstOrDefault(r => Norm(r.Tradingsymbol) == target)
            ?? rows.FirstOrDefault(r => Norm(r.Name) == target);
        if (exact is not null)
            return exact;

        var preferred = preferredSpotExchange.Trim().ToUpperInvariant();
        if (preferred.Length > 0)
        {
            var byEx = rows.FirstOrDefault(r =>
                string.Equals(r.Exchange.Trim(), preferred, StringComparison.OrdinalIgnoreCase));
            if (byEx is not null)
                return byEx;
        }

        return rows.FirstOrDefault();
    }

    public static IReadOnlyList<KiteInstrumentListItemDto> FilterNiftyOptions(
        IReadOnlyList<KiteInstrumentListItemDto> rows)
    {
        return rows
            .Where(r =>
            {
                if (r.Strike is null || r.LotSize is null or < 1)
                    return false;
                if (!TryParseExpiry(r.Expiry, out _))
                    return false;
                var type = (r.InstrumentType ?? string.Empty).Trim().ToUpperInvariant();
                var ts = r.Tradingsymbol.Trim().ToUpperInvariant();
                var isCePe = type is "CE" or "PE" || ts.EndsWith("CE", StringComparison.Ordinal) || ts.EndsWith("PE", StringComparison.Ordinal);
                if (!isCePe)
                    return false;
                var hay = (ts + " " + (r.Name ?? string.Empty)).Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
                // Exclude BANKNIFTY / FINNIFTY / MIDCPNIFTY while keeping NIFTY.
                if (hay.Contains("BANKNIFTY", StringComparison.Ordinal)
                    || hay.Contains("FINNIFTY", StringComparison.Ordinal)
                    || hay.Contains("MIDCPNIFTY", StringComparison.Ordinal))
                    return false;
                return hay.Contains("NIFTY", StringComparison.Ordinal);
            })
            .ToList();
    }

    public static DateTimeOffset? PickNearestFutureExpiryUtc(
        IReadOnlyList<KiteInstrumentListItemDto> options,
        DateTimeOffset utcNow)
    {
        var future = options
            .Select(o => TryParseExpiry(o.Expiry, out var dto) ? dto : (DateTimeOffset?)null)
            .Where(x => x is not null && x.Value >= utcNow.Date)
            .Select(x => x!.Value)
            .OrderBy(x => x)
            .ToList();
        if (future.Count > 0)
            return future[0];

        return options
            .Select(o => TryParseExpiry(o.Expiry, out var dto) ? dto : (DateTimeOffset?)null)
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .OrderBy(x => x)
            .Cast<DateTimeOffset?>()
            .FirstOrDefault();
    }

    /// <summary>
    /// Uses <paramref name="preferredExpiry"/> when that calendar date exists in <paramref name="options"/>;
    /// otherwise nearest future expiry.
    /// </summary>
    public static DateTimeOffset? ResolveExpiryUtc(
        IReadOnlyList<KiteInstrumentListItemDto> options,
        DateOnly? preferredExpiry,
        DateTimeOffset utcNow)
    {
        if (preferredExpiry is DateOnly preferred)
        {
            var match = options
                .Select(o => TryParseExpiry(o.Expiry, out var dto) ? dto : (DateTimeOffset?)null)
                .Where(x => x is not null && DateOnly.FromDateTime(x.Value.UtcDateTime) == preferred)
                .Select(x => x!.Value)
                .OrderBy(x => x)
                .Cast<DateTimeOffset?>()
                .FirstOrDefault();
            if (match is not null)
                return match;
        }

        return PickNearestFutureExpiryUtc(options, utcNow);
    }

    /// <summary>Distinct NIFTY option expiry dates as <c>yyyy-MM-dd</c>, ascending.</summary>
    public static IReadOnlyList<string> ListDistinctExpiryDates(IReadOnlyList<KiteInstrumentListItemDto> options)
    {
        return options
            .Select(o => TryParseExpiry(o.Expiry, out var dto) ? DateOnly.FromDateTime(dto.UtcDateTime) : (DateOnly?)null)
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .Distinct()
            .OrderBy(x => x)
            .Select(x => x.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .ToList();
    }

    public static IReadOnlyList<OptionCandidate> BuildStrikeCandidates(
        IReadOnlyList<KiteInstrumentListItemDto> options,
        DateTimeOffset expiryUtc,
        decimal spotLtp,
        string optionSide,
        int maxStepsAwayFromAtm)
    {
        var side = optionSide.Trim().ToUpperInvariant();
        if (side is not ("CE" or "PE"))
            side = "CE";

        var bucket = options
            .Where(o =>
            {
                if (!TryParseExpiry(o.Expiry, out var exp))
                    return false;
                return exp.UtcDateTime.Date == expiryUtc.UtcDateTime.Date && o.Strike is not null;
            })
            .ToList();

        var strikes = bucket
            .Select(o => o.Strike!.Value)
            .Distinct()
            .OrderBy(s => s)
            .ToList();
        if (strikes.Count == 0)
            return Array.Empty<OptionCandidate>();

        var atm = strikes.Aggregate((best, s) => Math.Abs(s - spotLtp) < Math.Abs(best - spotLtp) ? s : best);
        var atmIdx = strikes.FindIndex(s => s == atm);
        var steps = Math.Max(0, maxStepsAwayFromAtm);
        var from = Math.Max(0, atmIdx - steps);
        var to = Math.Min(strikes.Count - 1, atmIdx + steps);

        var orderedStrikes = strikes
            .Skip(from)
            .Take(to - from + 1)
            .OrderBy(s => Math.Abs(s - atm))
            .ThenBy(s => s)
            .ToList();

        var result = new List<OptionCandidate>();
        foreach (var strike in orderedStrikes)
        {
            var row = bucket.FirstOrDefault(o =>
            {
                if (o.Strike != strike)
                    return false;
                var type = (o.InstrumentType ?? string.Empty).Trim().ToUpperInvariant();
                var ts = o.Tradingsymbol.Trim().ToUpperInvariant();
                var isCe = type == "CE" || ts.EndsWith("CE", StringComparison.Ordinal);
                var isPe = type == "PE" || ts.EndsWith("PE", StringComparison.Ordinal);
                return side == "CE" ? isCe : isPe;
            });
            if (row is null || row.LotSize is null or < 1)
                continue;

            result.Add(new OptionCandidate(
                row.InstrumentToken.Trim(),
                row.Tradingsymbol.Trim(),
                row.Exchange.Trim(),
                strike,
                row.Expiry,
                row.LotSize.Value,
                side));
        }

        return result;
    }

    /// <summary>Whole lots affordable from cash; capped by <paramref name="maxLots"/>.</summary>
    public static int ComputeAffordableLots(
        decimal availableBalanceInr,
        decimal optionLtp,
        int lotSize,
        int maxLots,
        decimal utilizationFraction)
    {
        if (availableBalanceInr <= 0 || optionLtp <= 0 || lotSize < 1 || maxLots < 1)
            return 0;

        var util = utilizationFraction <= 0 ? 1m : Math.Min(1m, utilizationFraction);
        var budget = availableBalanceInr * util;
        var costPerLot = optionLtp * lotSize;
        if (costPerLot <= 0)
            return 0;

        var lots = (int)Math.Floor(budget / costPerLot);
        return Math.Clamp(lots, 0, maxLots);
    }

    public static bool TryParseExpiry(string? expiry, out DateTimeOffset value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(expiry))
            return false;

        var raw = expiry.Trim();
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
        {
            value = dto;
            return true;
        }

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
        {
            value = new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
            return true;
        }

        return false;
    }
}
