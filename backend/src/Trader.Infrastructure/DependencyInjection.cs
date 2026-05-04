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
            var conn =
                configuration.GetConnectionString("MySQL")
                ?? configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrWhiteSpace(conn))
            {
                var hasProcCs = Environment.GetEnvironmentVariable("ConnectionStrings__MySQL") is { Length: > 0 };
                throw new InvalidOperationException(
                    "MySQL connection string is missing. " +
                    "`appsettings.json` sets `Database:Provider` to **MySQL** but `ConnectionStrings:MySQL` is empty after configuration loads. " +
                    $"Process env **ConnectionStrings__MySQL** present={(hasProcCs ? "yes" : "no")}. " +
                    "On DigitalOcean App Platform, add **ConnectionStrings__MySQL** to the **same Web Service** as the API, scope **RUN_TIME**, **Encrypt** the value. " +
                    "Use a Pomelo-style string (e.g. `Server=...;Port=25060;Database=...;User Id=...;Password=...;SslMode=Required;` for managed MySQL). " +
                    "Name must use double underscores: `ConnectionStrings__MySQL`, not `DATABASE_URL`, unless you map it yourself.");
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
