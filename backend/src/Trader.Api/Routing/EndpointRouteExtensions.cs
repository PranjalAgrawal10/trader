using Trader.Api.Hubs;

namespace Trader.Api.Routing;

public static class EndpointRouteExtensions
{
    public static WebApplication MapTraderEndpoints(this WebApplication app)
    {
        app.MapTraderHealthEndpoints();
        app.MapTraderSignalREndpoints();
        app.MapControllers();
        return app;
    }

    public static WebApplication MapTraderHealthEndpoints(this WebApplication app)
    {
        app.MapGet(ApiRoutes.Health, () => Results.Ok(new { status = "ok" }))
            .AllowAnonymous();

        // Same payload; use when ingress only forwards `/api/*` to the API (root `/health` may hit the static site).
        app.MapGet(ApiRoutes.ApiHealth, () => Results.Ok(new { status = "ok" }))
            .AllowAnonymous();

        app.MapGet(ApiRoutes.Root, () => Results.Json(new
            {
                service = "Trader.Api",
                health = ApiRoutes.Health,
                api = ApiRoutes.ApiV1Root,
                swagger = app.Configuration.GetValue("Swagger:Enabled", false) || app.Environment.IsDevelopment()
                    ? ApiRoutes.Swagger
                    : (string?)null,
            }))
            .AllowAnonymous();

        return app;
    }

    public static WebApplication MapTraderSignalREndpoints(this WebApplication app)
    {
        app.MapHub<MarketHub>(ApiRoutes.HubsMarket);
        return app;
    }
}
