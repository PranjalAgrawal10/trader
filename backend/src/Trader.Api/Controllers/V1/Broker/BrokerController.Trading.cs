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
    [HttpGet("kite/margins")]
    public async Task<ActionResult<KiteUserMarginsDto>> GetKiteMargins(CancellationToken ct)
    {
        try
        {
            var dto = await _broker.GetKiteUserMarginsAsync(User.GetUserId(), ct);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

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
    [HttpPost("kite/gtt")]
    public async Task<ActionResult<KiteGttActionResultDto>> CreateKiteGtt(
        [FromBody] KiteGttCreateRequestDto? body,
        CancellationToken ct)
    {
        if (body is null)
        {
            return Problem(
                title: "Invalid body",
                detail: "Send JSON with exchange, tradingsymbol, entryTransactionType, quantity, product, and optional reference/last price or SL/target overrides.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var dto = await _broker.CreateKiteGttOcoAsync(User.GetUserId(), body, ct);
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
}
