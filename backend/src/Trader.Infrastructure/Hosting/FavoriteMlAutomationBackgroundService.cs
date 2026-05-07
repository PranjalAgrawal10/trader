using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Application.Configuration;
using Trader.Application.Prediction;

namespace Trader.Infrastructure.Hosting;

/// <summary>Runs <see cref="FavoriteMlAutomationService"/> on an interval when <see cref="FavoriteMlAutomationOptions.Enabled"/> is true.</summary>
public sealed class FavoriteMlAutomationBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<FavoriteMlAutomationOptions> _options;
    private readonly ILogger<FavoriteMlAutomationBackgroundService> _logger;

    public FavoriteMlAutomationBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<FavoriteMlAutomationOptions> options,
        ILogger<FavoriteMlAutomationBackgroundService> logger)
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
            var poll = opts.PollIntervalSeconds > 0
                ? TimeSpan.FromSeconds(Math.Clamp(opts.PollIntervalSeconds, 15, 3600))
                : TimeSpan.FromMinutes(Math.Clamp(opts.PollIntervalMinutes, 1, 120));
            var idle = TimeSpan.FromMinutes(5);

            try
            {
                if (opts.Enabled)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var runner = scope.ServiceProvider.GetRequiredService<FavoriteMlAutomationService>();
                    await runner.RunCycleAsync(stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Favorite ML automation cycle failed");
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
