using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trader.Api.Extensions;
using Trader.Api.Routing;
using Trader.Application.Wallet;

namespace Trader.Api.Controllers.V1;

[Authorize]
public sealed class WalletController : V1NamedControllerBase
{
    private readonly IWalletService _wallet;

    public WalletController(IWalletService wallet)
    {
        _wallet = wallet;
    }

    /// <summary>Current simulated wallet balance (INR).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(WalletBalanceResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<WalletBalanceResponse>> Get(CancellationToken ct) =>
        Ok(await _wallet.GetBalanceAsync(User.GetUserId(), ct));

    /// <summary>Add funds without payment gateway (development / UX placeholder).</summary>
    [HttpPost("load")]
    [ProducesResponseType(typeof(WalletBalanceResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<WalletBalanceResponse>> Load([FromBody] WalletLoadRequest request, CancellationToken ct)
    {
        if (request is null)
            return Problem(title: "Bad Request", detail: "Request body is required.", statusCode: StatusCodes.Status400BadRequest);

        var result = await _wallet.LoadMoneyAsync(User.GetUserId(), request.Amount, ct);
        return Ok(result);
    }
}
