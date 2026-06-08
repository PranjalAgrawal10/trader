using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using Trader.Api.Extensions;
using Trader.Application.Broker;
using Trader.Application.Configuration;

namespace Trader.Api.Controllers.V1;

[ApiController]
[Route("api/v{version:apiVersion}/broker")]
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "v1")]
public sealed class BrokerController : ControllerBase
{
    private const string KiteOAuthStateCookie = "Trader.KiteOAuth.State";

    private readonly IBrokerService _broker;
    private readonly IOptions<ZerodhaKiteOptions> _kiteOptions;
    private readonly ILogger<BrokerController> _logger;

    public BrokerController(
        IBrokerService broker,
        IOptions<ZerodhaKiteOptions> kiteOptions,
        ILogger<BrokerController> logger)
    {
        _broker = broker;
        _kiteOptions = kiteOptions;
        _logger = logger;
    }

    [Authorize]
    [HttpGet("status")]
    public async Task<ActionResult<BrokerStatusDto>> Status(CancellationToken ct)
    {
        var dto = await _broker.GetStatusAsync(User.GetUserId(), ct);
        return Ok(dto);
    }

    [Authorize]
    [HttpGet("providers")]
    public async Task<ActionResult<IReadOnlyList<BrokerProviderAvailabilityDto>>> Providers(CancellationToken ct)
    {
        var providers = await _broker.GetOrderBrokerProvidersAsync(User.GetUserId(), ct);
        return Ok(providers);
    }

    [Authorize]
    [HttpPost("complete-setup")]
    public async Task<ActionResult<BrokerStatusDto>> CompleteSetup(CancellationToken ct)
    {
        await _broker.CompleteSetupAsync(User.GetUserId(), ct);
        var dto = await _broker.GetStatusAsync(User.GetUserId(), ct);
        return Ok(dto);
    }

    [Authorize]
    [HttpPost("disconnect")]
    public async Task<ActionResult<BrokerStatusDto>> Disconnect(CancellationToken ct)
    {
        var dto = await _broker.DisconnectAsync(User.GetUserId(), ct);
        return Ok(dto);
    }

    [Authorize]
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
    [Authorize]
    [HttpGet("kite/orders")]
    public async Task<ActionResult<KiteOrderBookDto>> KiteOrders(CancellationToken ct)
    {
        try
        {
            var dto = await _broker.GetKiteOrdersAsync(User.GetUserId(), ct);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>Current Kite net positions (non-zero quantity rows from positions/net).</summary>
    [Authorize]
    [HttpGet("kite/positions/net")]
    public async Task<ActionResult<IReadOnlyList<KiteNetPositionDto>>> KiteNetPositions(CancellationToken ct)
    {
        try
        {
            var dto = await _broker.GetKiteNetPositionsAsync(User.GetUserId(), ct);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [Authorize]
    [HttpGet("positions/net")]
    public async Task<ActionResult<IReadOnlyList<KiteNetPositionDto>>> NetPositions(
        [FromQuery] string? broker,
        CancellationToken ct)
    {
        try
        {
            var dto = await _broker.GetNetPositionsAsync(User.GetUserId(), broker, ct);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [Authorize]
    [HttpGet("kite/scalper-settings")]
    public async Task<ActionResult<ScalperSettingsDto>> GetScalperSettings(CancellationToken ct)
    {
        try
        {
            var dto = await _broker.GetScalperSettingsAsync(User.GetUserId(), ct);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [Authorize]
    [HttpPut("kite/scalper-settings")]
    public async Task<IActionResult> PutScalperSettings(
        [FromBody] ScalperSettingsPutDto? body,
        CancellationToken ct)
    {
        if (body is null)
        {
            return Problem(
                title: "Invalid body",
                detail: "Send JSON with interval, rangePreset, graphType, showVolume, safeModeEnabled, safeStopLossPoints, and safeTriggerPoints.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            await _broker.SaveScalperSettingsAsync(User.GetUserId(), body, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [Authorize]
    [HttpPost("kite/orders/{orderId}/cancel")]
    public async Task<ActionResult<KiteOrderActionResultDto>> CancelKiteOrder(
        [FromRoute] string orderId,
        [FromBody] KiteOrderCancelRequestDto? body,
        CancellationToken ct)
    {
        if (body is null)
        {
            return Problem(
                title: "Invalid body",
                detail: "Send JSON with optional variety and parentOrderId.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var dto = await _broker.CancelKiteOrderAsync(User.GetUserId(), orderId, body, ct);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [Authorize]
    [HttpPost("kite/orders/{orderId}/modify")]
    public async Task<ActionResult<KiteOrderActionResultDto>> ModifyKiteOrder(
        [FromRoute] string orderId,
        [FromBody] KiteOrderModifyRequestDto? body,
        CancellationToken ct)
    {
        if (body is null)
        {
            return Problem(
                title: "Invalid body",
                detail: "Send JSON with variety, symbol, quantity, side, type and price fields.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var dto = await _broker.ModifyKiteOrderAsync(User.GetUserId(), orderId, body, ct);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [Authorize]
    [HttpPost("kite/orders/{orderId}/repeat")]
    public async Task<ActionResult<KiteOrderActionResultDto>> RepeatKiteOrder(
        [FromRoute] string orderId,
        [FromBody] KiteOrderRepeatRequestDto? body,
        CancellationToken ct)
    {
        if (body is null)
        {
            return Problem(
                title: "Invalid body",
                detail: "Send JSON with optional variety.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var dto = await _broker.RepeatKiteOrderAsync(User.GetUserId(), orderId, body, ct);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [Authorize]
    [HttpPost("kite/orders/place")]
    public async Task<ActionResult<KiteOrderActionResultDto>> PlaceKiteOrder(
        [FromBody] KiteOrderPlaceRequestDto? body,
        CancellationToken ct)
    {
        if (body is null)
        {
            return Problem(
                title: "Invalid body",
                detail: "Send JSON with variety, symbol, side, quantity, product, orderType and optional price/trigger.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var dto = await _broker.PlaceKiteOrderAsync(User.GetUserId(), body, ct);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [Authorize]
    [HttpPost("orders/place")]
    public async Task<ActionResult<KiteOrderActionResultDto>> PlaceOrder(
        [FromBody] KiteOrderPlaceRequestDto? body,
        CancellationToken ct)
    {
        if (body is null)
        {
            return Problem(
                title: "Invalid body",
                detail: "Send JSON with optional broker plus symbol, side, quantity, product, orderType and optional price/trigger.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var dto = await _broker.PlaceOrderAsync(User.GetUserId(), body, ct);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>Saved favorite Kite instruments for the current user (persisted in the database).</summary>
    [Authorize]
    [HttpGet("kite/favorites")]
    public async Task<ActionResult<KiteFavoriteInstrumentsListDto>> KiteFavorites(CancellationToken ct)
    {
        try
        {
            var dto = await _broker.GetKiteFavoriteInstrumentsAsync(User.GetUserId(), ct);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [Authorize]
    [HttpPost("kite/favorites")]
    public async Task<IActionResult> AddKiteFavorite([FromBody] KiteInstrumentListItemDto? body, CancellationToken ct)
    {
        if (body is null)
        {
            return Problem(
                title: "Invalid body",
                detail: "Send a JSON instrument row (instrumentToken, tradingsymbol, exchange, …).",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            await _broker.AddKiteFavoriteInstrumentAsync(User.GetUserId(), body, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [Authorize]
    [HttpDelete("kite/favorites")]
    public async Task<IActionResult> RemoveKiteFavorite(
        [FromQuery(Name = "instrumentToken")] string? instrumentToken,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(instrumentToken))
        {
            return Problem(
                title: "Invalid instrument",
                detail: "Provide instrumentToken.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            await _broker.RemoveKiteFavoriteInstrumentAsync(User.GetUserId(), instrumentToken, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>Kite instruments marked locked for trading (separate persisted list from favorites).</summary>
    [Authorize]
    [HttpGet("kite/trading-locks")]
    public async Task<ActionResult<KiteTradingLocksListDto>> KiteTradingLocks(CancellationToken ct)
    {
        try
        {
            var dto = await _broker.GetKiteTradingLocksAsync(User.GetUserId(), ct);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [Authorize]
    [HttpPost("kite/trading-locks")]
    public async Task<IActionResult> AddKiteTradingLock([FromBody] KiteInstrumentListItemDto? body, CancellationToken ct)
    {
        if (body is null)
        {
            return Problem(
                title: "Invalid body",
                detail: "Send a JSON instrument row (instrumentToken, tradingsymbol, exchange, …).",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            await _broker.AddKiteTradingLockAsync(User.GetUserId(), body, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [Authorize]
    [HttpDelete("kite/trading-locks")]
    public async Task<IActionResult> RemoveKiteTradingLock(
        [FromQuery(Name = "instrumentToken")] string? instrumentToken,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(instrumentToken))
        {
            return Problem(
                title: "Invalid instrument",
                detail: "Provide instrumentToken.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            await _broker.RemoveKiteTradingLockAsync(User.GetUserId(), instrumentToken, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>Persisted Kite instruments page chart toolbar (interval, range, line/bar) and optional per-instrument zoom map. Requires Bearer auth.</summary>
    [Authorize]
    [HttpGet("kite/instruments/chart-settings")]
    public async Task<ActionResult<KiteInstrumentsChartSettingsDto>> GetKiteInstrumentsChartSettings(CancellationToken ct)
    {
        try
        {
            var dto = await _broker.GetKiteInstrumentsChartSettingsAsync(User.GetUserId(), ct);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [Authorize]
    [HttpPut("kite/instruments/chart-settings")]
    public async Task<IActionResult> PutKiteInstrumentsChartSettings(
        [FromBody] KiteInstrumentsChartSettingsDto? body,
        CancellationToken ct)
    {
        if (body is null)
        {
            return Problem(
                title: "Invalid body",
                detail: "Send JSON with interval, rangePreset, and graphType; optional mlAutomationEnabled and trendAnalysisIntervals.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            await _broker.SaveKiteInstrumentsChartSettingsAsync(User.GetUserId(), body, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>Demo auto-trade intent (no broker orders). EOD outcome is hypothetical from same-day automation rows.</summary>
    [Authorize]
    [HttpPut("kite/instruments/demo-auto-trade")]
    public async Task<IActionResult> PutDemoAutoTrade([FromBody] DemoAutoTradePutDto? body, CancellationToken ct)
    {
        if (body is null)
        {
            return Problem(
                title: "Invalid body",
                detail: "Send JSON { \"enabled\": bool, optional \"strategy\": \"equal_split\" | … }.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            await _broker.SetDemoAutoTradePreferencesAsync(User.GetUserId(), body.Enabled, body.Strategy, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>Open demo paper long positions (whole contracts) per locked instrument.</summary>
    [Authorize]
    [HttpGet("kite/instruments/demo-paper-positions")]
    public async Task<ActionResult<IReadOnlyList<DemoPaperPositionListItemDto>>> GetDemoPaperPositions(CancellationToken ct)
    {
        var list = await _broker.GetDemoPaperPositionsAsync(User.GetUserId(), ct).ConfigureAwait(false);
        return Ok(list);
    }

    /// <summary>Manual demo paper trade history (newest first).</summary>
    [Authorize]
    [HttpGet("kite/instruments/demo-paper-trades")]
    public async Task<ActionResult<IReadOnlyList<DemoPaperTradeHistoryRowDto>>> GetDemoPaperTradeHistory(
        [FromQuery] int? take,
        CancellationToken ct)
    {
        var list = await _broker.GetDemoPaperTradeHistoryAsync(User.GetUserId(), take, ct).ConfigureAwait(false);
        return Ok(list);
    }

    /// <summary>Manual paper buy (debits wallet) or sell (credits wallet) at cached Kite LTP — no orders.</summary>
    [Authorize]
    [HttpPost("kite/instruments/demo-paper-trade")]
    public async Task<ActionResult<DemoPaperTradeResultDto>> PostDemoPaperTrade(
        [FromBody] DemoPaperTradeRequestDto? body,
        CancellationToken ct)
    {
        if (body is null)
        {
            return Problem(
                title: "Invalid body",
                detail: "Send JSON { \"instrumentToken\", \"side\": \"buy\" | \"sell\", \"contracts\": n }.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var dto = await _broker.ExecuteDemoPaperTradeAsync(User.GetUserId(), body, ct).ConfigureAwait(false);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [Authorize]
    [HttpPut("kite/instruments/chart-zoom")]
    public async Task<IActionResult> PutKiteInstrumentsChartZoom(
        [FromBody] KiteInstrumentsChartZoomPutDto? body,
        CancellationToken ct)
    {
        if (body is null)
        {
            return Problem(
                title: "Invalid body",
                detail:
                    "Send JSON with instrumentToken plus optional visibleFraction (between 0 and 1 exclusive) or legacy visibleBars; omit both optional fields to clear saved zoom.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            await _broker.SaveKiteInstrumentsChartZoomAsync(User.GetUserId(), body, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [Authorize]
    [HttpPut("kite/instruments/chart-interval")]
    public async Task<IActionResult> PutKiteInstrumentsChartInterval(
        [FromBody] KiteInstrumentsChartIntervalPutDto? body,
        CancellationToken ct)
    {
        if (body is null)
        {
            return Problem(
                title: "Invalid body",
                detail: "Send JSON with instrumentToken and interval (UI code 1m…1d, or null to use the page default for that token).",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            await _broker.SaveKiteInstrumentsChartIntervalOverrideAsync(User.GetUserId(), body, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [Authorize]
    [HttpGet("kite/login-url")]
    public async Task<ActionResult<KiteLoginUrlDto>> KiteLoginUrl(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var r = await _broker.GetKiteLoginUrlAsync(userId, ct);
        Response.Cookies.Append(KiteOAuthStateCookie, r.PendingOAuthStateKey, BuildKiteOAuthCookieOptions());
        return Ok(new KiteLoginUrlDto(r.LoginUrl));
    }

    /// <summary>Kite redirect target — register this exact URL on the Kite developer console.</summary>
    [AllowAnonymous]
    [HttpGet("kite/callback")]
    public async Task<IActionResult> KiteCallback(
        [FromQuery] string? request_token,
        [FromQuery] string? status,
        [FromQuery] string? state,
        [FromQuery(Name = "trader_oauth")] string? traderOauth,
        CancellationToken ct)
    {
        var redirectBase = _kiteOptions.Value.PostLoginRedirectUrl.TrimEnd('/');
        var cookieOptions = BuildKiteOAuthCookieOptions();

        var traderFromQuery = FirstNonEmpty(
            traderOauth,
            Request.Query["trader_oauth"].FirstOrDefault());
        var effectiveState = FirstNonEmpty(state, traderFromQuery, Request.Cookies[KiteOAuthStateCookie]);

        Response.Cookies.Delete(KiteOAuthStateCookie, new CookieOptions
        {
            Path = cookieOptions.Path,
            HttpOnly = true,
            Secure = cookieOptions.Secure,
            SameSite = cookieOptions.SameSite,
        });

        // Kite v3 docs: redirect includes request_token; `status` query is not always sent. Only fail if status is present and not success.
        var statusPresentAndFailed = !string.IsNullOrEmpty(status)
            && !string.Equals(status, "success", StringComparison.OrdinalIgnoreCase);

        if (statusPresentAndFailed
            || string.IsNullOrEmpty(request_token)
            || string.IsNullOrWhiteSpace(effectiveState))
        {
            var cookieHadState = Request.Cookies.ContainsKey(KiteOAuthStateCookie);
            var queryKeys = string.Join(',', Request.Query.Keys);
            _logger.LogWarning(
                "Kite OAuth callback rejected: StatusPresentAndFailed={StatusFailed}, HasRequestToken={HasToken}, HasStateQuery={HasStateQ}, HasTraderOauth={HasTraderOauth}, CookieHadFallback={CookieFallback}, CallbackStatus={KiteStatus}, QueryKeys={QueryKeys}",
                statusPresentAndFailed,
                !string.IsNullOrEmpty(request_token),
                !string.IsNullOrWhiteSpace(state),
                !string.IsNullOrWhiteSpace(traderFromQuery),
                cookieHadState,
                status ?? "(null)",
                queryKeys);

            string userMessage = statusPresentAndFailed
                ? "Login was cancelled or failed at Zerodha."
                : string.IsNullOrEmpty(request_token)
                    ? "Missing request token from Kite. Check the redirect URL in the developer console."
                    : string.IsNullOrWhiteSpace(effectiveState)
                        ? "Missing OAuth state. Use the same browser session, or ensure the Kite redirect URL matches this API (see README)."
                        : "Login was cancelled or failed.";

            return Redirect($"{redirectBase}?kite=error&message={Uri.EscapeDataString(userMessage)}");
        }

        try
        {
            await _broker.CompleteKiteOAuthAsync(request_token, effectiveState, ct);
            return Redirect($"{redirectBase}?kite=success");
        }
        catch (InvalidOperationException ex)
        {
            return Redirect($"{redirectBase}?kite=error&message={Uri.EscapeDataString(ex.Message)}");
        }
    }

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

    private CookieOptions BuildKiteOAuthCookieOptions()
    {
        // SPA (other origin) calls /broker/kite/login-url with credentials: SameSite=None + Secure helps the fallback cookie stick in production HTTPS.
        var https = Request.IsHttps;
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = https,
            SameSite = https ? SameSiteMode.None : SameSiteMode.Lax,
            Path = "/",
            MaxAge = TimeSpan.FromMinutes(20),
        };
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }

        return null;
    }
}
