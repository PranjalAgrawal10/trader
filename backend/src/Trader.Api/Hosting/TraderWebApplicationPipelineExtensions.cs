using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Trader.Api.Routing;
using Trader.Infrastructure.Persistence;

namespace Trader.Api.Hosting;

internal static class TraderWebApplicationPipelineExtensions
{
    public static async Task UseTraderApiPipelineAsync(this WebApplication app)
    {
        app.UseSerilogRequestLogging(options =>
        {
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("TraceId", Activity.Current?.Id ?? httpContext.TraceIdentifier);
                diagnosticContext.Set("ClientIp", httpContext.Connection.RemoteIpAddress?.ToString());
                diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString());
            };
        });

        await ApplyDatabaseMigrationsIfConfiguredAsync(app);

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(o => o.SwaggerEndpoint("/swagger/v1/swagger.json", "Trader v1"));
        }
        else if (app.Configuration.GetValue("Swagger:Enabled", false))
        {
            app.UseSwagger();
            app.UseSwaggerUI(o => o.SwaggerEndpoint("/swagger/v1/swagger.json", "Trader v1"));
        }

        if (!app.Environment.IsEnvironment("IntegrationTesting"))
        {
            app.UseForwardedHeaders();
            app.UseHttpsRedirection();
        }

        app.UseCors();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapTraderEndpoints();
    }

    private static async Task ApplyDatabaseMigrationsIfConfiguredAsync(WebApplication app)
    {
        if (app.Environment.IsEnvironment("IntegrationTesting")
            || !string.Equals(app.Configuration["Database:Provider"], "MySQL", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var mysqlCs = MySqlConnectionStringResolver.Resolve(app.Configuration);
        if (string.IsNullOrWhiteSpace(mysqlCs))
        {
            throw new InvalidOperationException(
                "MySQL is configured but the connection could not be built. Set **DATABASE_URL** (preferred) or discrete **Database__Host** / **Name** / **Username** / **Password**, or see **`backend/src/Trader.Api/.env.example`**.");
        }

        if (app.Environment.IsDevelopment())
        {
            await MySqlBootstrap.EnsureDatabaseExistsAsync(mysqlCs, CancellationToken.None);
        }

        if (app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", true))
        {
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TraderDbContext>();
            await db.Database.MigrateAsync();
        }
    }
}
