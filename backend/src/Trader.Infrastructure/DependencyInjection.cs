using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pomelo.EntityFrameworkCore.MySql;
using Trader.Application.Broker;
using Trader.Application.Configuration;
using Trader.Application.Abstractions.Persistence;
using Trader.Application.Abstractions.Security;
using Trader.Infrastructure.Broker;
using Trader.Infrastructure.Persistence;
using Trader.Infrastructure.Persistence.Repositories;
using Trader.Infrastructure.Security;

namespace Trader.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<ZerodhaKiteOptions>(configuration.GetSection(ZerodhaKiteOptions.SectionName));

        services.AddDataProtection();
        services.AddSingleton<IKiteOAuthStateCodec>(sp =>
        {
            var provider = sp.GetRequiredService<IDataProtectionProvider>();
            return new KiteOAuthStateCodec(provider.CreateProtector("Trader.Broker.Kite.State"));
        });
        services.AddSingleton<ITwoFactorTotpHelper>(sp =>
        {
            var provider = sp.GetRequiredService<IDataProtectionProvider>();
            return new TwoFactorTotpHelper(provider.CreateProtector("Trader.Security.TotpSecret"));
        });
        services.AddSingleton<ITwoFactorLoginTicketService>(sp =>
        {
            var provider = sp.GetRequiredService<IDataProtectionProvider>();
            return new TwoFactorLoginTicketService(provider.CreateProtector("Trader.Auth.TwoFactorLogin"));
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
                var hasProcCs = Environment.GetEnvironmentVariable("ConnectionStrings__MySQL") is { Length: > 0 };
                var gaps = MySqlConnectionStringResolver.DescribeDiscreteGaps(configuration);
                throw new InvalidOperationException(
                    "MySQL connection string is missing. " +
                    "Set all four: **Database__Host**, **Database__Name**, **Database__Username** (or **UserId**), **Database__Password** " +
                    "(optional **Database__Port**, **Database__SslMode**), **or** use **MYSQL_*** / **DB_*** aliases documented in the README, **or** a real **ConnectionStrings__MySQL** / **DATABASE_URL** (**mysql://**, not a **${…}** placeholder). " +
                    "`appsettings.json` sets `Database:Provider` to **MySQL** but no valid connection could be built. " +
                    gaps +
                    $"Process env snapshot: **ConnectionStrings__MySQL**={(hasProcCs ? "set (may be unexpanded `${{…}}` — ignored)" : "absent")}. " +
                    "On DigitalOcean App Platform, attach vars to the **API** component with **RUN_TIME** and **Encrypt** secrets.");
            }

            var serverVersion = new MySqlServerVersion(new Version(8, 0, 36));
            services.AddDbContext<TraderDbContext>(options =>
                options.UseMySql(conn, serverVersion));
        }

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IStrategyRepository, StrategyRepository>();
        services.AddScoped<IBotRepository, BotRepository>();
        services.AddScoped<ITradeRepository, TradeRepository>();
        services.AddScoped<IBrokerSetupGateway, BrokerSetupGateway>();

        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();

        return services;
    }
}
