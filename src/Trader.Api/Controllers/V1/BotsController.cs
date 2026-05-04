using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trader.Api.Extensions;
using Trader.Application.Bots;

namespace Trader.Api.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "v1")]
public sealed class BotsController : ControllerBase
{
    private readonly IBotService _bots;

    public BotsController(IBotService bots)
    {
        _bots = bots;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BotResponse>>> List(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var items = await _bots.ListAsync(userId, ct);
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BotResponse>> Get(Guid id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var item = await _bots.GetAsync(userId, id, ct);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<ActionResult<BotResponse>> Create([FromBody] CreateBotRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var created = await _bots.CreateAsync(userId, request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id, version = "1.0" }, created);
    }

    [HttpPost("{id:guid}/assign-strategy")]
    public async Task<ActionResult<BotResponse>> AssignStrategy(Guid id, [FromBody] AssignStrategyRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var bot = await _bots.AssignStrategyAsync(userId, id, request.StrategyId, ct);
        return bot is null ? NotFound() : Ok(bot);
    }

    [HttpPost("{id:guid}/start")]
    public async Task<ActionResult<BotResponse>> Start(Guid id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var bot = await _bots.StartAsync(userId, id, ct);
        return bot is null ? NotFound() : Ok(bot);
    }

    [HttpPost("{id:guid}/stop")]
    public async Task<ActionResult<BotResponse>> Stop(Guid id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var bot = await _bots.StopAsync(userId, id, ct);
        return bot is null ? NotFound() : Ok(bot);
    }
}
