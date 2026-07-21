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
                detail: "Send JSON with interval, rangePreset, graphType, showVolume, safeModeEnabled, safeStopLossPoints, safeTriggerPoints, gttLossEnabled, and gttProfitEnabled.",
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

    /// <summary>Live NIFTY ATM MIS BUY at ~09:15 IST with 10% GTT (not demo/paper).</summary>
    [Authorize]
    [HttpGet("kite/nifty-open-auto-trade")]
    public async Task<ActionResult<NiftyOpenAutoTradeSettingsDto>> GetNiftyOpenAutoTrade(CancellationToken ct)
    {
        try
        {
            var dto = await _niftyOpenAutoTrade.GetSettingsAsync(User.GetUserId(), ct).ConfigureAwait(false);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [Authorize]
    [HttpPut("kite/nifty-open-auto-trade")]
    public async Task<IActionResult> PutNiftyOpenAutoTrade(
        [FromBody] NiftyOpenAutoTradeSettingsPutDto? body,
        CancellationToken ct)
    {
        if (body is null)
        {
            return Problem(
                title: "Invalid body",
                detail: "Send JSON { \"enabled\": bool, \"optionSide\": \"CE\"|\"PE\", \"maxLots\": 1–10, \"expiry\": \"yyyy-MM-dd\"|null, \"stopLossPoints\": number, \"targetPoints\": number, \"stopLossEnabled\": bool, \"targetEnabled\": bool }.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            await _niftyOpenAutoTrade.SaveSettingsAsync(User.GetUserId(), body, ct).ConfigureAwait(false);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [Authorize]
    [HttpGet("kite/nifty-open-auto-trade/preview")]
    public async Task<ActionResult<NiftyOpenAutoTradePreviewDto>> PreviewNiftyOpenAutoTrade(CancellationToken ct)
    {
        try
        {
            var dto = await _niftyOpenAutoTrade.PreviewAsync(User.GetUserId(), ct).ConfigureAwait(false);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [Authorize]
    [HttpGet("kite/nifty-open-auto-trade/runs")]
    public async Task<ActionResult<IReadOnlyList<NiftyOpenAutoTradeRunDto>>> ListNiftyOpenAutoTradeRuns(
        [FromQuery] int? take,
        CancellationToken ct)
    {
        try
        {
            var dto = await _niftyOpenAutoTrade.ListRunsAsync(User.GetUserId(), take, ct).ConfigureAwait(false);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

}
