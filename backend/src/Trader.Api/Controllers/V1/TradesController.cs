using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trader.Api.Extensions;
using Trader.Api.Routing;
using Trader.Application.Bots;
using Trader.Application.Trades;

namespace Trader.Api.Controllers.V1;

[Authorize]
public sealed class TradesController : V1NamedControllerBase
{
    private readonly ITradeService _trades;

    public TradesController(ITradeService trades)
    {
        _trades = trades;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TradeResponse>>> List([FromQuery] Guid? botId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var items = await _trades.ListAsync(userId, botId, ct);
        return Ok(items);
    }

    [HttpGet("orders")]
    public async Task<ActionResult<IReadOnlyList<TradingOrderResponse>>> ListOrders([FromQuery] Guid? botId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var items = await _trades.ListOrdersAsync(userId, botId, ct);
        return Ok(items);
    }
}
