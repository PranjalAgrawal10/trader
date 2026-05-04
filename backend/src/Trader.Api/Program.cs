using System.Text;
using Asp.Versioning;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.EntityFrameworkCore;
using Trader.Api.Filters;
using Trader.Api.Hosting;
using Trader.Application;
using Trader.Application.Configuration;
using Trader.Infrastructure;
using Trader.Infrastructure.Persistence;

namespace Trader.Api;

public sealed class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        DotEnvBootstrap.Apply(builder.Configuration, builder.Environment);

        builder.Services.AddControllers(options => options.Filters.Add<ApplicationExceptionFilter>())
            .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(
                new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
        });
        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(builder.Configuration);

        builder.Services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new ApiVersion(1, 0);
            options.AssumeDefaultVersionWhenUnspecified = true;
            options.ReportApiVersions = true;
        }).AddMvc();

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "Trader API", Version = "v1" });
            var scheme = new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "JWT Bearer token."
            };
            c.AddSecurityDefinition("Bearer", scheme);
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                    },
                    Array.Empty<string>()
                }
            });
        });

        var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
        var jwt = jwtSection.Get<JwtOptions>() ?? new JwtOptions();

        if (!builder.Environment.IsDevelopment()
            && !builder.Environment.IsEnvironment("IntegrationTesting"))
        {
            var keyLength = string.IsNullOrEmpty(jwt.Key) ? 0 : Encoding.UTF8.GetBytes(jwt.Key).Length;
            if (string.IsNullOrWhiteSpace(jwt.Issuer)
                || string.IsNullOrWhiteSpace(jwt.Audience)
                || keyLength < 32)
            {
                throw new InvalidOperationException(
                    "JWT is not configured for this environment. On DigitalOcean App Platform, open your **Web Service** (API) component → **Settings → Environment variables** " +
                    "(or **Edit** in the app spec) and add at **run time** (use **Encrypt** for secrets): " +
                    "**Jwt__Issuer**, **Jwt__Audience**, **Jwt__Key** (random string, UTF-8 length ≥ 32 bytes), and **Cors__Origins__0** (your SPA origin, e.g. https://your-app.ondigitalocean.app). " +
                    "Use double underscores in names; scope must include **RUN_TIME**. If these are set only on the static site or as **BUILD_TIME** only, the API will not see them. " +
                    "Until the process starts successfully, health checks may report **connection refused** on port 8080.");
            }
        }

        if (!builder.Environment.IsEnvironment("IntegrationTesting"))
        {
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.KnownNetworks.Clear();
                options.KnownProxies.Clear();
            });
        }

        if (!builder.Environment.IsEnvironment("IntegrationTesting"))
        {
            var dataProtectionKeysPath = builder.Configuration["DataProtection:KeyRingPath"];
            if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
            {
                Directory.CreateDirectory(dataProtectionKeysPath);
                builder.Services.AddDataProtection()
                    .SetApplicationName("Trader")
                    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
            }
            else if (builder.Environment.IsProduction())
            {
                // Stateless hosts (e.g. App Platform without a mounted key path): avoid filesystem key ring warnings;
                // keys still reset on restart — use DataProtection:KeyRingPath + persistent storage to keep payloads stable.
                builder.Services.AddDataProtection()
                    .SetApplicationName("Trader")
                    .UseEphemeralDataProtectionProvider();
            }
        }

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                    ClockSkew = TimeSpan.FromMinutes(1),
                    NameClaimType = JwtRegisteredClaimNames.Sub,
                };
            });

        builder.Services.AddAuthorization();

        var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                          ?? Array.Empty<string>();
        if (corsOrigins.Length == 0)
        {
            throw new InvalidOperationException(
                "Configure CORS via Cors:Origins (e.g. Cors__Origins__0 in .env) or appsettings for this environment.");
        }

        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
                policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials());
        });

        var app = builder.Build();

        if (app.Environment.IsDevelopment()
            && !app.Environment.IsEnvironment("IntegrationTesting")
            && string.Equals(app.Configuration["Database:Provider"], "MySQL", StringComparison.OrdinalIgnoreCase))
        {
            var mysqlCs = app.Configuration.GetConnectionString("MySQL");
            await MySqlBootstrap.EnsureDatabaseExistsAsync(mysqlCs, CancellationToken.None);

            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TraderDbContext>();
            await db.Database.MigrateAsync();
        }

        if (app.Environment.IsDevelopment())
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
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
            .AllowAnonymous();

        app.MapControllers();

        await app.RunAsync();
    }
}
