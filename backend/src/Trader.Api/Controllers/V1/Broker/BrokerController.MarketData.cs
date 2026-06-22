using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using Trader.Api.Extensions;
using Trader.Api.Routing;
using Trader.Application.Broker;
using Trader.Application.Configuration;


namespace Trader.Api.Controllers.V1;

public sealed partial class BrokerController
{
    [HttpGet("kite/instruments/fno-commodities")]
    public async Task<ActionResult<KiteFnoCommodityListsDto>> KiteFnoCommodityInstruments(CancellationToken ct)
    {
        var dto = await _broker.GetKiteFnoCommodityInstrumentsAsync(User.GetUserId(), ct);
        return Ok(dto);
    }

    /// <summary>Contracts with largest % gain vs prior close among the capped Browse universe (quotes via Kite /quote/ohlc).</summary>
    [Authorize]
    [HttpGet("kite/instruments/today-top-performers")]
    public async Task<ActionResult<KiteTodayTopPerformersDto>> KiteTodayTopPerformers([FromQuery] int take, CancellationToken ct)
    {
        var clamped = take <= 0 ? 15 : take;
        try
        {
            var dto = await _broker.GetKiteTodayTopPerformersAsync(User.GetUserId(), clamped, ct);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Kite today-top-performers failed.");
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>Streaming search across Kite instrument CSVs: <c>Fno</c>, <c>Mcx</c>, <c>Spot</c> (NSE/BSE cash <c>EQ</c> and indices only — not commodities), or <c>All</c> (merged). Whitespace separates AND phrases; within each phrase, letter runs and digit runs become separate tokens (e.g. <c>nifty12may</c> matches symbols containing <c>nifty</c>, <c>12</c>, and <c>may</c>). Multi-word <c>gold mini</c> still matches <c>GOLDMINI</c>. Returns every match in the daily CSV (no row cap — the 50-row limit only applies to the Browse-tab preview lists, not to explicit searches).</summary>
    [Authorize]
    [HttpGet("kite/instruments/search")]
    public async Task<ActionResult<KiteInstrumentSearchDto>> SearchKiteInstruments(
        [FromQuery(Name = "q")] string? q,
        [FromQuery] KiteInstrumentSearchSegment segment,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length > 128)
        {
            return Problem(
                title: "Invalid query",
                detail: "Provide non-empty q (max 128 characters).",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var dto = await _broker.SearchKiteInstrumentsAsync(User.GetUserId(), q, segment, ct);
        return Ok(dto);
    }

    /// <summary>Historical OHLCV + SMA/EMA/SR overlays (combined). For smaller parallel responses call <see cref="KiteHistoricalChartOhlcv"/> + <see cref="KiteHistoricalChartOverlays"/> (~25s server-side composite cache).</summary>
    [Authorize]
    [HttpGet("kite/historical-candles")]
    public async Task<ActionResult<KiteHistoricalCandlesDto>> KiteHistoricalCandles(
        [FromQuery(Name = "instrumentToken")] string? instrumentToken,
        [FromQuery] string? interval,
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
    {
        var binding = BindKiteHistoricalInstrumentRequest(instrumentToken, interval, from, to);
        if (binding.Error != null)
            return binding.Error;

        try
        {
            var dto = await _broker.GetKiteHistoricalCandlesAsync(
                User.GetUserId(),
                binding.Query!.InstrumentToken,
                binding.Query!.Interval,
                binding.Query!.FromUtc,
                binding.Query!.ToUtc,
                ct);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>OHLC+V only — pair with historical-overlays using the same query for parallel chart loads.</summary>
    [Authorize]
    [HttpGet("kite/chart/historical-ohlc")]
    public async Task<ActionResult<KiteHistoricalOhlcvOnlyDto>> KiteHistoricalChartOhlcv(
        [FromQuery(Name = "instrumentToken")] string? instrumentToken,
        [FromQuery] string? interval,
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
    {
        var binding = BindKiteHistoricalInstrumentRequest(instrumentToken, interval, from, to);
        if (binding.Error != null)
            return binding.Error;

        try
        {
            var dto = await _broker.GetKiteHistoricalChartOhlcvAsync(
                User.GetUserId(),
                binding.Query!.InstrumentToken,
                binding.Query!.Interval,
                binding.Query!.FromUtc,
                binding.Query!.ToUtc,
                ct);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>OHLC+V only for multiple intervals in one call. Use comma-separated <c>intervals</c> (e.g. <c>1m,2m,3m</c>).</summary>
    [Authorize]
    [HttpGet("kite/chart/historical-ohlc/multi")]
    public async Task<ActionResult<KiteHistoricalOhlcvMultiDto>> KiteHistoricalChartOhlcvMulti(
        [FromQuery(Name = "instrumentToken")] string? instrumentToken,
        [FromQuery] string? intervals,
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(intervals))
        {
            return Problem(
                title: "Invalid intervals",
                detail: "Provide comma-separated intervals (e.g. 1m,2m,3m).",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var parsedIntervals = intervals
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (parsedIntervals.Length == 0)
        {
            return Problem(
                title: "Invalid intervals",
                detail: "Provide at least one valid interval.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var boundQueries = new List<KiteHistoricalBoundQuery>(parsedIntervals.Length);
        foreach (var parsedInterval in parsedIntervals)
        {
            var binding = BindKiteHistoricalInstrumentRequest(instrumentToken, parsedInterval, from, to);
            if (binding.Error != null)
                return binding.Error;
            boundQueries.Add(binding.Query!);
        }

        try
        {
            var items = new List<KiteHistoricalOhlcvOnlyDto>(boundQueries.Count);
            foreach (var q in boundQueries)
            {
                // Sequential awaits avoid concurrent use of scoped EF DbContext within one HTTP request.
                var dto = await _broker.GetKiteHistoricalChartOhlcvAsync(
                    User.GetUserId(),
                    q.InstrumentToken,
                    q.Interval,
                    q.FromUtc,
                    q.ToUtc,
                    ct);
                items.Add(dto);
            }
            return Ok(new KiteHistoricalOhlcvMultiDto(items));
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>Smoothing/support-resistance overlays for the trimmed window (same semantics as candles endpoint).</summary>
    [Authorize]
    [HttpGet("kite/chart/historical-overlays")]
    public async Task<ActionResult<KiteHistoricalOverlaysDto>> KiteHistoricalChartOverlays(
        [FromQuery(Name = "instrumentToken")] string? instrumentToken,
        [FromQuery] string? interval,
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
    {
        var binding = BindKiteHistoricalInstrumentRequest(instrumentToken, interval, from, to);
        if (binding.Error != null)
            return binding.Error;

        try
        {
            var dto = await _broker.GetKiteHistoricalChartOverlaysAsync(
                User.GetUserId(),
                binding.Query!.InstrumentToken,
                binding.Query!.Interval,
                binding.Query!.FromUtc,
                binding.Query!.ToUtc,
                ct);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>LTP vs prior-session close (~5s server cache).</summary>
    [Authorize]
    [HttpGet("kite/chart/live-quote")]
    public async Task<ActionResult<KiteInstrumentLiveQuoteDto>> KiteChartLiveQuote(
        [FromQuery] string? exchange,
        [FromQuery] string? tradingsymbol,
        CancellationToken ct)
    {
        try
        {
            var dto = await _broker.GetKiteInstrumentLiveQuoteAsync(
                User.GetUserId(),
                exchange ?? "",
                tradingsymbol ?? "",
                ct);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>Kite orderbook for the day (open/pending/executed/rejected/cancelled).</summary>
    private sealed record KiteHistoricalBoundQuery(string InstrumentToken, string Interval, DateTimeOffset? FromUtc, DateTimeOffset? ToUtc);

    private sealed record KiteHistoricalInstrumentBinding(KiteHistoricalBoundQuery? Query, ActionResult? Error);

    private KiteHistoricalInstrumentBinding BindKiteHistoricalInstrumentRequest(
        string? instrumentToken,
        string? interval,
        string? from,
        string? to)
    {
        if (string.IsNullOrWhiteSpace(instrumentToken))
        {
            return new KiteHistoricalInstrumentBinding(
                null,
                Problem(
                    title: "Invalid instrument",
                    detail: "Provide instrumentToken from the Kite instrument list.",
                    statusCode: StatusCodes.Status400BadRequest));
        }

        if (string.IsNullOrWhiteSpace(interval))
        {
            return new KiteHistoricalInstrumentBinding(
                null,
                Problem(
                    title: "Invalid interval",
                    detail: "Provide interval (e.g. 5m, 1d).",
                    statusCode: StatusCodes.Status400BadRequest));
        }

        DateTimeOffset? fromUtc = null;
        DateTimeOffset? toUtc = null;
        if (!string.IsNullOrWhiteSpace(from))
        {
            if (!DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var f))
            {
                return new KiteHistoricalInstrumentBinding(
                    null,
                    Problem(
                        title: "Invalid from",
                        detail: "Use an ISO 8601 instant for from.",
                        statusCode: StatusCodes.Status400BadRequest));
            }

            fromUtc = f;
        }

        if (!string.IsNullOrWhiteSpace(to))
        {
            if (!DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t))
            {
                return new KiteHistoricalInstrumentBinding(
                    null,
                    Problem(
                        title: "Invalid to",
                        detail: "Use an ISO 8601 instant for to.",
                        statusCode: StatusCodes.Status400BadRequest));
            }

            toUtc = t;
        }

        return new KiteHistoricalInstrumentBinding(new KiteHistoricalBoundQuery(instrumentToken, interval!, fromUtc, toUtc), null);
    }
}
