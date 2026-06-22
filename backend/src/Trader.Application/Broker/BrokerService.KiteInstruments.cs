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
    public async Task<KiteFnoCommodityListsDto> GetKiteFnoCommodityInstrumentsAsync(Guid userId, CancellationToken ct = default)
    {
        var (apiKey, accessToken) = await RequireKiteInstrumentSessionAsync(userId, ct).ConfigureAwait(false);

        var fetchCap = (int?)KiteInstrumentBrowseExchangeFetchRows;
        var nfoTask = _kiteInstruments.FetchExchangeInstrumentsAsync("NFO", apiKey, accessToken, fetchCap, ct);
        var bfoTask = _kiteInstruments.FetchExchangeInstrumentsAsync("BFO", apiKey, accessToken, fetchCap, ct);
        var mcxTask = _kiteInstruments.FetchExchangeInstrumentsAsync("MCX", apiKey, accessToken, fetchCap, ct);

        await Task.WhenAll(nfoTask, bfoTask, mcxTask).ConfigureAwait(false);

        var nfo = await nfoTask.ConfigureAwait(false);
        if (!nfo.Success)
            throw new InvalidOperationException(nfo.ErrorMessage ?? "Could not load NFO instruments from Kite.");

        var fno = new List<KiteInstrumentListItemDto>(nfo.Items);
        var fnoTruncated = nfo.Truncated;

        var bfo = await bfoTask.ConfigureAwait(false);
        if (bfo.Success)
        {
            fno.AddRange(bfo.Items);
            fnoTruncated |= bfo.Truncated;
        }

        fno.Sort(static (a, b) => CompareInstrumentExpiryAscending(a, b));

        var combinedFnoCount = fno.Count;
        if (combinedFnoCount > KiteInstrumentBrowsePanelMaxRows)
        {
            fno = fno.Take(KiteInstrumentBrowsePanelMaxRows).ToList();
            fnoTruncated = true;
        }

        var mcx = await mcxTask.ConfigureAwait(false);
        if (!mcx.Success)
            throw new InvalidOperationException(mcx.ErrorMessage ?? "Could not load MCX instruments from Kite.");

        var commodities = new List<KiteInstrumentListItemDto>(mcx.Items);
        var commoditiesTruncated = mcx.Truncated;
        commodities.Sort(static (a, b) => CompareInstrumentExpiryAscending(a, b));
        if (commodities.Count > KiteInstrumentBrowsePanelMaxRows)
        {
            commodities = commodities.Take(KiteInstrumentBrowsePanelMaxRows).ToList();
            commoditiesTruncated = true;
        }

        return new KiteFnoCommodityListsDto(fno, commodities, fnoTruncated, commoditiesTruncated);
    }

    /// <inheritdoc />
    public async Task<KiteTodayTopPerformersDto> GetKiteTodayTopPerformersAsync(Guid userId, int take, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, KiteTodayTopPerformersMaxTake);

        static string QuoteKey(KiteInstrumentListItemDto x) =>
            $"{x.Exchange.Trim()}:{x.Tradingsymbol.Trim()}";

        var lists = await GetKiteFnoCommodityInstrumentsAsync(userId, ct).ConfigureAwait(false);

        var uniqueRows = lists.Fno
            .Concat(lists.Commodities)
            .GroupBy(QuoteKey, StringComparer.Ordinal)
            .Select(static g => g.First())
            .ToList();

        var quoteKeys = uniqueRows.Select(QuoteKey).ToList();

        var (apiKey, accessToken) = await RequireKiteInstrumentSessionAsync(userId, ct).ConfigureAwait(false);

        var quotes = new Dictionary<string, KiteQuoteOhlcTickDto>(StringComparer.Ordinal);
        for (var off = 0; off < quoteKeys.Count; off += KiteQuoteOhlcBatchSizeBroker)
        {
            var batchCount = Math.Min(KiteQuoteOhlcBatchSizeBroker, quoteKeys.Count - off);
            var slice = quoteKeys.Skip(off).Take(batchCount).ToList();
            var fetch = await _kiteInstruments.FetchQuoteOhlcAsync(slice, apiKey, accessToken, ct).ConfigureAwait(false);
            if (!fetch.Success)
                throw new InvalidOperationException(fetch.ErrorMessage ?? "Could not load OHLC quotes from Kite.");

            foreach (var kv in fetch.ByKey)
                quotes[kv.Key] = kv.Value;
        }

        var movers = new List<KiteInstrumentMoverDto>(uniqueRows.Count);
        foreach (var r in uniqueRows)
        {
            if (!quotes.TryGetValue(QuoteKey(r), out var q))
                continue;

            var prevClose = q.OhlcClose;
            if (prevClose <= 0)
                continue;

            var pct = (q.LastPrice - prevClose) / prevClose * 100m;
            movers.Add(new KiteInstrumentMoverDto(r, q.LastPrice, prevClose, pct));
        }

        var items = movers
            .OrderByDescending(static m => m.ChangePercent)
            .Take(take)
            .ToList();

        return new KiteTodayTopPerformersDto(
            items,
            "% vs previous session close (Kite OHLC); universe = same capped preview as Browse lists.");
    }

    public async Task<KiteInstrumentSearchDto> SearchKiteInstrumentsAsync(
        Guid userId,
        string query,
        KiteInstrumentSearchSegment segment,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new InvalidOperationException("Search text is required.");

        var needle = query.Trim();
        if (needle.Length > KiteInstrumentSearchQueryMaxLength)
            throw new InvalidOperationException($"Search text must be at most {KiteInstrumentSearchQueryMaxLength} characters.");

        var (apiKey, accessToken) = await RequireKiteInstrumentSessionAsync(userId, ct).ConfigureAwait(false);

        if (segment == KiteInstrumentSearchSegment.All)
        {
            var fTask = SearchKiteInstrumentsAsync(userId, query, KiteInstrumentSearchSegment.Fno, ct);
            var mTask = SearchKiteInstrumentsAsync(userId, query, KiteInstrumentSearchSegment.Mcx, ct);
            var sTask = SearchKiteInstrumentsAsync(userId, query, KiteInstrumentSearchSegment.Spot, ct);
            await Task.WhenAll(fTask, mTask, sTask).ConfigureAwait(false);
            var f = await fTask.ConfigureAwait(false);
            var m = await mTask.ConfigureAwait(false);
            var s = await sTask.ConfigureAwait(false);

            var scanTruncated = f.ScanTruncated || m.ScanTruncated || s.ScanTruncated;
            var byKey = new Dictionary<string, KiteInstrumentListItemDto>(StringComparer.Ordinal);
            void AddDistinct(IReadOnlyList<KiteInstrumentListItemDto> items)
            {
                foreach (var item in items)
                {
                    var k = $"{item.Exchange.Trim()}:{item.Tradingsymbol.Trim()}";
                    if (!byKey.ContainsKey(k))
                        byKey[k] = item;
                }
            }

            AddDistinct(f.Items);
            AddDistinct(m.Items);
            AddDistinct(s.Items);
            return new KiteInstrumentSearchDto(byKey.Values.ToList(), scanTruncated);
        }

        if (segment == KiteInstrumentSearchSegment.Fno)
        {
            var nfoTask = _kiteInstruments.SearchExchangeInstrumentsAsync(
                "NFO", apiKey, accessToken, needle, KiteInstrumentSearchUnlimited, ct: ct);
            var bfoTask = _kiteInstruments.SearchExchangeInstrumentsAsync(
                "BFO", apiKey, accessToken, needle, KiteInstrumentSearchUnlimited, ct: ct);
            await Task.WhenAll(nfoTask, bfoTask).ConfigureAwait(false);

            var nfo = await nfoTask.ConfigureAwait(false);
            if (!nfo.Success)
                throw new InvalidOperationException(nfo.ErrorMessage ?? "Could not search NFO instruments on Kite.");

            var combined = new List<KiteInstrumentListItemDto>(nfo.Items);
            var scanTruncated = nfo.Truncated;

            var bfo = await bfoTask.ConfigureAwait(false);
            if (bfo.Success)
            {
                combined.AddRange(bfo.Items);
                scanTruncated |= bfo.Truncated;
            }

            return new KiteInstrumentSearchDto(combined, scanTruncated);
        }

        if (segment == KiteInstrumentSearchSegment.Spot)
        {
            var nseTask = _kiteInstruments
                .SearchExchangeInstrumentsAsync(
                    "NSE", apiKey, accessToken, needle, KiteInstrumentSearchUnlimited, equityCashOnly: true, ct);
            var bseTask = _kiteInstruments
                .SearchExchangeInstrumentsAsync(
                    "BSE", apiKey, accessToken, needle, KiteInstrumentSearchUnlimited, equityCashOnly: true, ct);
            await Task.WhenAll(nseTask, bseTask).ConfigureAwait(false);

            var nse = await nseTask.ConfigureAwait(false);
            if (!nse.Success)
                throw new InvalidOperationException(nse.ErrorMessage ?? "Could not search NSE equity instruments on Kite.");

            var combined = new List<KiteInstrumentListItemDto>(nse.Items);
            var scanTruncated = nse.Truncated;

            var bse = await bseTask.ConfigureAwait(false);
            if (!bse.Success)
                throw new InvalidOperationException(bse.ErrorMessage ?? "Could not search BSE equity instruments on Kite.");

            combined.AddRange(bse.Items);
            scanTruncated |= bse.Truncated;

            return new KiteInstrumentSearchDto(combined, scanTruncated);
        }

        if (segment == KiteInstrumentSearchSegment.Mcx)
        {
            var mcx = await _kiteInstruments
                .SearchExchangeInstrumentsAsync(
                    "MCX",
                    apiKey,
                    accessToken,
                    needle,
                    KiteInstrumentSearchUnlimited,
                    ct: ct)
                .ConfigureAwait(false);
            if (!mcx.Success)
                throw new InvalidOperationException(mcx.ErrorMessage ?? "Could not search MCX instruments on Kite.");

            return new KiteInstrumentSearchDto(mcx.Items, mcx.Truncated);
        }

        throw new InvalidOperationException($"Unexpected segment: {segment}.");
    }
    /// <summary>Kite CSV expiry is typically <c>yyyy-MM-dd</c>; derivatives without a parseable date sort after dated rows.</summary>
    private static int CompareInstrumentExpiryAscending(KiteInstrumentListItemDto a, KiteInstrumentListItemDto b)
    {
        var da = TryParseKiteExpiryDate(a.Expiry);
        var db = TryParseKiteExpiryDate(b.Expiry);
        if (da.HasValue && db.HasValue)
            return da.Value.CompareTo(db.Value);
        if (da.HasValue)
            return -1;
        if (db.HasValue)
            return 1;
        return string.CompareOrdinal(a.Tradingsymbol ?? "", b.Tradingsymbol ?? "");
    }

    private static DateOnly? TryParseKiteExpiryDate(string? expiry)
    {
        if (string.IsNullOrWhiteSpace(expiry))
            return null;
        return DateOnly.TryParse(expiry.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;
    }
}
