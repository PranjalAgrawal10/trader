using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pomelo.EntityFrameworkCore.MySql;
using Trader.Application.Broker;
using Trader.Application.Configuration;
using Trader.Application.Streaming;
using Trader.Application.Abstractions.Messaging;
using Trader.Application.Abstractions.Persistence;
using Trader.Application.Abstractions.Security;
using Trader.Application.Abstractions.Reporting;
using Trader.Infrastructure.Broker;
using Trader.Infrastructure.Email;
using Trader.Infrastructure.Persistence;
using Trader.Infrastructure.Persistence.Repositories;
using Trader.Infrastructure.Reporting;
using Trader.Infrastructure.Security;
using Microsoft.Extensions.Hosting;
using Trader.Infrastructure.Streaming;

namespace Trader.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.Configure<PublicWebOptions>(configuration.GetSection(PublicWebOptions.SectionName));
        services.Configure<EmailOtpOptions>(configuration.GetSection(EmailOtpOptions.SectionName));
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
        // Kite ApiKey/ApiSecret/RedirectUrl: environment variables (ZerodhaKite__*) and Development .env — not committed appsettings.
        services.Configure<ZerodhaKiteOptions>(configuration.GetSection(ZerodhaKiteOptions.SectionName));
        services.Configure<GrowwOptions>(configuration.GetSection(GrowwOptions.SectionName));
        services.Configure<LiveCandlesOptions>(configuration.GetSection(LiveCandlesOptions.SectionName));
        services.Configure<DemoAutoTradeOptions>(configuration.GetSection(DemoAutoTradeOptions.SectionName));

        services.AddSingleton<IKiteOAuthStateCodec, KiteOAuthStateCodec>();
        services.AddSingleton<ITwoFactorTotpHelper>(sp =>
        {
            var provider = sp.GetRequiredService<IDataProtectionProvider>();
            return new TwoFactorTotpHelper(provider.CreateProtector("Trader.Security.TotpSecret"));
        });
        services.AddSingleton<ITwoFactorRecoveryCodesHelper, TwoFactorRecoveryCodesHelper>();
        services.AddSingleton<ITwoFactorOtpAttemptLimiter, TwoFactorOtpAttemptLimiter>();
        services.AddSingleton<ITwoFactorLoginTicketService>(sp =>
        {
            var provider = sp.GetRequiredService<IDataProtectionProvider>();
            var auth = sp.GetRequiredService<IOptions<AuthOptions>>();
            return new TwoFactorLoginTicketService(provider.CreateProtector("Trader.Auth.TwoFactorLogin"), auth);
        });
        services.AddHttpClient<IKiteSessionExchange, KiteSessionExchange>(client =>
        {
            client.BaseAddress = new Uri("https://api.kite.trade/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHttpClient<IKiteInstrumentsClient, KiteInstrumentsClient>(client =>
            {
                client.BaseAddress = new Uri("https://api.kite.trade/");
                client.Timeout = TimeSpan.FromMinutes(10);
            })
            .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
            });
        services.AddHttpClient<IGrowwTradingClient, GrowwTradingClient>(client =>
            {
                var groww = configuration.GetSection(GrowwOptions.SectionName).Get<GrowwOptions>() ?? new GrowwOptions();
                var baseUrl = string.IsNullOrWhiteSpace(groww.ApiBaseUrl) ? "https://api.groww.in/v1/" : groww.ApiBaseUrl.Trim();
                if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
                    baseUrl += "/";
                client.BaseAddress = new Uri(baseUrl);
                client.Timeout = TimeSpan.FromSeconds(30);
            });

        var provider = configuration["Database:Provider"] ?? "MySQL";
        if (string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            var name = configuration["Database:InMemoryDatabase"] ?? "trader";
            services.AddDbContext<TraderDbContext>(options => options.UseInMemoryDatabase(name));
        }
        else
        {
            var conn = MySqlConnectionStringResolver.Resolve(configuration);

            if (string.IsNullOrWhiteSpace(conn))
            {
                var gaps = MySqlConnectionStringResolver.DescribeConfigurationGaps(configuration);
                throw new InvalidOperationException(
                    "MySQL configuration is incomplete. " +
                    "Set **DATABASE_URL** (`mysql://user:pass@host:port/db?ssl-mode=Required`) — recommended for App Platform / managed MySQL — " +
                    "or **Database__ConnectionString**, " +
                    "or discrete **Database__Host**, **Database__Name**, **Database__Username**, **Database__Password**. " +
                    "Canonical names: see **`backend/src/Trader.Api/.env.example`**. " +
                    gaps +
                    "`Database:Provider` is **MySQL** but no connection could be built.");
            }

            var serverVersion = new MySqlServerVersion(new Version(8, 0, 36));
            services.AddDbContext<TraderDbContext>(options =>
                options.UseMySql(conn, serverVersion));
        }

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserLoginAuditRepository, UserLoginAuditRepository>();
        services.AddScoped<IDemoPaperPositionRepository, DemoPaperPositionRepository>();
        services.AddScoped<IDemoPaperBuyLegRepository, DemoPaperBuyLegRepository>();
        services.AddScoped<IDemoPaperTradeLogRepository, DemoPaperTradeLogRepository>();
        services.AddScoped<IKiteFavoriteInstrumentRepository, KiteFavoriteInstrumentRepository>();
        services.AddScoped<IKiteTradingLockInstrumentRepository, KiteTradingLockInstrumentRepository>();
        services.AddScoped<IStrategyRepository, StrategyRepository>();
        services.AddScoped<IBotRepository, BotRepository>();
        services.AddScoped<ITradeRepository, TradeRepository>();
        services.AddScoped<ITradingOrderRepository, TradingOrderRepository>();
        services.AddScoped<IBrokerSetupGateway, BrokerSetupGateway>();
        services.AddScoped<IKiteInstrumentsChartSettingsGateway, KiteInstrumentsChartSettingsGateway>();
        services.AddScoped<IHistoricalCandleUpserter, HistoricalCandleUpserter>();
        services.AddSingleton<LiveCandleTickSubscriber>();
        services.AddSingleton<IKiteTickerSessionManager, KiteTickerSessionManager>();
        services.AddScoped<IEmailOtpRepository, EmailOtpRepository>();
        services.AddHttpClient<SendGridPlainTextEmailSender>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddSingleton<SmtpPlainTextEmailSender>();
        services.AddSingleton<RoutingPlainTextEmailSender>();
        services.AddSingleton<IPlainTextEmailSender>(sp => sp.GetRequiredService<RoutingPlainTextEmailSender>());
        services.AddHostedService<SmtpStartupValidator>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();

        return services;
    }
}
