using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trader.Api.Extensions;
using Trader.Application.Strategies;

namespace Trader.Api.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "v1")]
public sealed class StrategiesController : ControllerBase
{
    private readonly IStrategyService _strategies;

    public StrategiesController(IStrategyService strategies)
    {
        _strategies = strategies;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StrategyResponse>>> List(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var items = await _strategies.ListAsync(userId, ct);
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<StrategyResponse>> Get(Guid id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var item = await _strategies.GetAsync(userId, id, ct);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<ActionResult<StrategyResponse>> Create([FromBody] CreateStrategyRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var created = await _strategies.CreateAsync(userId, request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id, version = "1.0" }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<StrategyResponse>> Update(Guid id, [FromBody] UpdateStrategyRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var updated = await _strategies.UpdateAsync(userId, id, request, ct);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var ok = await _strategies.DeleteAsync(userId, id, ct);
        return ok ? NoContent() : NotFound();
    }
}
