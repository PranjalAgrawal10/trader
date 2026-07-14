using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Application.Broker;
using Trader.Application.Configuration;

namespace Trader.Infrastructure.Hosting;

/// <summary>Polls near 09:15 IST and runs <see cref="NiftyOpenAutoTradeService"/> for opted-in users.</summary>
public sealed class NiftyOpenAutoTradeBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<NiftyOpenAutoTradeOptions> _options;
    private readonly ILogger<NiftyOpenAutoTradeBackgroundService> _logger;

    public NiftyOpenAutoTradeBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<NiftyOpenAutoTradeOptions> options,
        ILogger<NiftyOpenAutoTradeBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var opts = _options.CurrentValue;
            var poll = TimeSpan.FromSeconds(Math.Clamp(opts.PollIntervalSeconds, 2, 60));
            var idle = TimeSpan.FromMinutes(1);

            try
            {
                if (opts.Enabled)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var runner = scope.ServiceProvider.GetRequiredService<NiftyOpenAutoTradeService>();
                    await runner.RunCycleAsync(stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NIFTY open auto-trade cycle failed");
            }

            try
            {
                await Task.Delay(opts.Enabled ? poll : idle, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
