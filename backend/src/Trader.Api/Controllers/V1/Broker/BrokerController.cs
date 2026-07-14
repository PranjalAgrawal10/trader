using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Api.Routing;
using Trader.Application.Broker;
using Trader.Application.Configuration;

namespace Trader.Api.Controllers.V1;

[V1Route("broker")]
public sealed partial class BrokerController : V1ControllerBase
{
    private const string KiteOAuthStateCookie = "Trader.KiteOAuth.State";

    private readonly IBrokerService _broker;
    private readonly NiftyOpenAutoTradeService _niftyOpenAutoTrade;
    private readonly IOptions<ZerodhaKiteOptions> _kiteOptions;
    private readonly ILogger<BrokerController> _logger;

    public BrokerController(
        IBrokerService broker,
        NiftyOpenAutoTradeService niftyOpenAutoTrade,
        IOptions<ZerodhaKiteOptions> kiteOptions,
        ILogger<BrokerController> logger)
    {
        _broker = broker;
        _niftyOpenAutoTrade = niftyOpenAutoTrade;
        _kiteOptions = kiteOptions;
        _logger = logger;
    }
}
