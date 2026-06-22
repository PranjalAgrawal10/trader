using Serilog;
using Serilog.Events;
using Trader.Api.Hosting;

namespace Trader.Api;

public sealed class Program
{
    public static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateBootstrapLogger();

        try
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.AddTraderApiHost();

            var app = builder.Build();
            await app.UseTraderApiPipelineAsync();
            await app.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Trader API host terminated unexpectedly");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
