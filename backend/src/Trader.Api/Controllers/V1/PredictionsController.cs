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

    public PredictionsController(IPriceDirectionPredictionService predictions)
    {
        _predictions = predictions;
    }

    /// <summary>
    /// ML.NET on-the-fly logistic model over short-horizon return / SMA features (not investment advice).
    /// </summary>
    [HttpGet("price-direction")]
    public async Task<ActionResult<PriceDirectionResponseDto>> PriceDirection(
        [FromQuery(Name = "instrumentToken")] string? instrumentToken,
        [FromQuery] string? interval,
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
            var r = await _predictions
                .PredictForInstrumentAsync(User.GetUserId(), instrumentToken.Trim(), interval.Trim(), ct)
                .ConfigureAwait(false);

            return Ok(new PriceDirectionResponseDto(
                MapDirection(r.Direction),
                r.Confidence,
                r.ModelId,
                r.Detail));
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
}

public sealed record PriceDirectionResponseDto(
    string Direction,
    int Confidence,
    string ModelId,
    string Detail);
