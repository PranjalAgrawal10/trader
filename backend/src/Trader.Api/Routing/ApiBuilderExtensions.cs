using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Serialization;
using Trader.Api.Filters;

namespace Trader.Api.Routing;

public static class ApiBuilderExtensions
{
    public static IServiceCollection AddTraderApiControllers(this IServiceCollection services)
    {
        services.AddScoped<ApplicationExceptionFilter>();

        services
            .AddControllers(options => options.Filters.AddService<ApplicationExceptionFilter>())
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(
                    new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            });

        services
            .AddApiVersioning(options =>
            {
                options.DefaultApiVersion = new ApiVersion(1, 0);
                options.AssumeDefaultVersionWhenUnspecified = true;
                options.ReportApiVersions = true;
            })
            .AddMvc();

        return services;
    }
}
