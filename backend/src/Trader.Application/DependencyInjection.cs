using Microsoft.Extensions.DependencyInjection;
using Trader.Application.Auth;
using Trader.Application.Bots;
using Trader.Application.Broker;
using Trader.Application.Strategies;
using Trader.Application.Trades;
using Trader.Application.Wallet;

namespace Trader.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IWalletService, WalletService>();
        services.AddScoped<IBrokerService, BrokerService>();
        services.AddScoped<IStrategyService, StrategyService>();
        services.AddScoped<IBotService, BotService>();
        services.AddScoped<ITradeService, TradeService>();
        services.AddScoped<IEmailOtpService, EmailOtpService>();
        return services;
    }
}
