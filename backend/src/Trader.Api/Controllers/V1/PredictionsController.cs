using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Trader.Api.Extensions;
using Trader.Application.Prediction;

namespace Trader.Api.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "v1")]
public sealed class PredictionsController : ControllerBase
{
    private readonly IPriceDirectionPredictionService _predictions;
    private readonly FavoriteMlAutomationService _favoriteMlAutomation;
    private readonly IPriceDirectionPredictionEngineRegistry _engineRegistry;

    public PredictionsController(
        IPriceDirectionPredictionService predictions,
        FavoriteMlAutomationService favoriteMlAutomation,
        IPriceDirectionPredictionEngineRegistry engineRegistry)
    {
        _predictions = predictions;
        _favoriteMlAutomation = favoriteMlAutomation;
        _engineRegistry = engineRegistry;
    }

    /// <summary>
    /// Registered price-direction models and the configured default (for UI or clients).
    /// </summary>
    [HttpGet("price-direction/models")]
    public ActionResult<PriceDirectionModelsResponseDto> PriceDirectionModels()
    {
        var models = _engineRegistry.ListModels();
        return Ok(new PriceDirectionModelsResponseDto(
            _engineRegistry.DefaultModelId,
            models.Select(static m => new PriceDirectionModelItemDto(m.Id, m.Description)).ToList()));
    }

    /// <summary>
    /// On-the-fly models over short-horizon features (not investment advice). Use <paramref name="model"/> to pick an engine; omit for the configured default.
    /// When enough candles are available, the prediction is stored for the signed-in user.
    /// </summary>
    [HttpGet("price-direction")]
    public async Task<ActionResult<PriceDirectionResponseDto>> PriceDirection(
        [FromQuery(Name = "instrumentToken")] string? instrumentToken,
        [FromQuery] string? interval,
        [FromQuery] string? model,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(instrumentToken))
        {
            return Problem(
                title: "Invalid instrument",
                detail: "Provide instrumentToken (Kite numeric token).",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(interval))
        {
            return Problem(
                title: "Invalid interval",
                detail: "Provide interval (e.g. 5m, 15m, 1h) — same codes as chart settings.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var env = await _predictions
                .PredictForInstrumentAsync(
                    User.GetUserId(),
                    instrumentToken.Trim(),
                    interval.Trim(),
                    source: null,
                    string.IsNullOrWhiteSpace(model) ? null : model.Trim(),
                    ct)
                .ConfigureAwait(false);

            var r = env.Result;
            return Ok(new PriceDirectionResponseDto(
                MapDirection(r.Direction),
                r.Confidence,
                r.ModelId,
                r.Detail,
                env.StoredId,
                env.RefBarTimeUtc,
                env.RefClose,
                env.PredictedAtUtc,
                MapPersistenceKind(env.PersistenceKind)));
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>Stored LightGBM triple-barrier runs only (<c>MlLightGbmTripleBarrierPredictions</c>).</summary>
    [HttpGet("price-direction/lightgbm-triple-barrier/history")]
    public async Task<ActionResult<IReadOnlyList<MlPriceDirectionPredictionItemDto>>> LightGbmTripleBarrierHistory(
        [FromQuery(Name = "instrumentToken")] string? instrumentToken,
        [FromQuery] string? interval,
        [FromQuery] int? take,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(instrumentToken))
        {
            return Problem(
                title: "Invalid instrument",
                detail: "Provide instrumentToken (Kite numeric token).",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(interval))
        {
            return Problem(
                title: "Invalid interval",
                detail: "Provide interval (e.g. 5m, 15m, 1h).",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var list = await _predictions
                .ListLightGbmTripleBarrierHistoryAsync(
                    User.GetUserId(),
                    instrumentToken.Trim(),
                    interval.Trim(),
                    take ?? 500,
                    ct)
                .ConfigureAwait(false);
            return Ok(list);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>Recent stored predictions for an instrument and interval (newest first).</summary>
    [HttpGet("price-direction/history")]
    public async Task<ActionResult<IReadOnlyList<MlPriceDirectionPredictionItemDto>>> PriceDirectionHistory(
        [FromQuery(Name = "instrumentToken")] string? instrumentToken,
        [FromQuery] string? interval,
        [FromQuery] int? take,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(instrumentToken))
        {
            return Problem(
                title: "Invalid instrument",
                detail: "Provide instrumentToken (Kite numeric token).",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(interval))
        {
            return Problem(
                title: "Invalid interval",
                detail: "Provide interval (e.g. 5m, 15m, 1h).",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var list = await _predictions
                .ListPredictionHistoryAsync(
                    User.GetUserId(),
                    instrumentToken.Trim(),
                    interval.Trim(),
                    take ?? 500,
                    ct)
                .ConfigureAwait(false);
            return Ok(list);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>Recent automation-sourced ML predictions (newest first), joined with favorite symbol when available.</summary>
    [HttpGet("price-direction/automation-recent")]
    public async Task<ActionResult<IReadOnlyList<MlAutomationPredictionListItemDto>>> AutomationRecent(
        [FromQuery] int? take,
        CancellationToken ct)
    {
        try
        {
            var list = await _predictions
                .ListAutomationRecentAsync(User.GetUserId(), take ?? 100, ct)
                .ConfigureAwait(false);
            return Ok(list);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>
    /// Send automation-sourced rows in a UTC half-open PredictedAt window <c>[fromUtc, toUtcExclusive)</c>; omit both for today (full local report day).
    /// Max span 93 days. Requires SMTP and profile email.
    /// </summary>
    [HttpPost("price-direction/automation-report-email")]
    [ProducesResponseType(typeof(FavoriteMlManualAutomationEmailReportResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<FavoriteMlManualAutomationEmailReportResult>> PostAutomationReportEmail(
        [FromBody] ManualAutomationReportEmailRequestDto? body,
        CancellationToken ct)
    {
        try
        {
            var result = await _favoriteMlAutomation
                .SendManualAutomationEmailReportAsync(
                    User.GetUserId(),
                    body?.FromUtc,
                    body?.ToUtcExclusive,
                    ct)
                .ConfigureAwait(false);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>Resolve a pending prediction when the next bar is known (correct/wrong vs reference close).</summary>
    [HttpPatch("price-direction/{id:guid}/resolve")]
    public async Task<IActionResult> PriceDirectionResolve(
        Guid id,
        [FromBody] ResolveMlPredictionBodyDto body,
        CancellationToken ct)
    {
        try
        {
            await _predictions
                .ResolvePredictionAsync(User.GetUserId(), id, body.NextBarTime, body.NextClose, ct)
                .ConfigureAwait(false);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static string MapDirection(PriceDirectionLabel d) =>
        d switch
        {
            PriceDirectionLabel.Up => "up",
            PriceDirectionLabel.Down => "down",
            _ => "neutral",
        };

    private static string? MapPersistenceKind(MlPredictionPersistenceKind k) =>
        k switch
        {
            MlPredictionPersistenceKind.ClassicPriceDirection => "classicPriceDirection",
            MlPredictionPersistenceKind.LightGbmTripleBarrier => "lightgbmTripleBarrier",
            _ => null,
        };
}

public sealed record PriceDirectionResponseDto(
    string Direction,
    int Confidence,
    string ModelId,
    string Detail,
    Guid? PredictionId,
    DateTimeOffset? RefBarTime,
    decimal? RefClose,
    DateTimeOffset? PredictedAt,
    string? PredictionStorage);

public sealed record PriceDirectionModelsResponseDto(
    string DefaultModelId,
    IReadOnlyList<PriceDirectionModelItemDto> Models);

public sealed record PriceDirectionModelItemDto(string Id, string Description);

public sealed record ResolveMlPredictionBodyDto(
    DateTimeOffset NextBarTime,
    decimal NextClose);

/// <summary>Optional body for POST …/automation-report-email. Both null = today (full local report day). Both set = UTC half-open range.</summary>
public sealed record ManualAutomationReportEmailRequestDto(
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtcExclusive = null);
