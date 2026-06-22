using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Exceptions;
using System.IdentityModel.Tokens.Jwt;
using System.Threading.RateLimiting;
using Trader.Api.Routing;
using Trader.Api.Streaming;
using Trader.Application;
using Trader.Application.Configuration;
using Trader.Application.Streaming;
using Trader.Infrastructure;

namespace Trader.Api.Hosting;

internal static class TraderWebApplicationBuilderExtensions
{
    public static WebApplicationBuilder AddTraderApiHost(this WebApplicationBuilder builder)
    {
        DotEnvBootstrap.Apply(builder.Configuration, builder.Environment);

        builder.Host.UseSerilog((context, services, loggerConfiguration) =>
        {
            loggerConfiguration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "Trader.Api")
                .Enrich.WithEnvironmentName()
                .Enrich.WithProcessId()
                .Enrich.WithThreadId()
                .Enrich.WithExceptionDetails();
        });

        builder.Services.AddTraderApiControllers();
        AddDataProtection(builder);
        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<SignalRMarketTickDispatcher>();
        builder.Services.AddSingleton<IMarketTickDispatcher, FanOutMarketTickDispatcher>();
        AddSwagger(builder);
        AddJwtAuthentication(builder);
        AddCors(builder);
        AddRateLimiting(builder.Services);

        return builder;
    }

    private static void AddDataProtection(WebApplicationBuilder builder)
    {
        var dataProtectionBuilder = builder.Services.AddDataProtection().SetApplicationName("Trader");
        if (builder.Environment.IsEnvironment("IntegrationTesting"))
            return;

        var dataProtectionKeysPath = builder.Configuration["DataProtection:KeyRingPath"];
        if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
        {
            Directory.CreateDirectory(dataProtectionKeysPath);
            dataProtectionBuilder.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
        }
        else if (builder.Environment.IsProduction())
        {
            // Without a persisted path, redeploys rotate keys — encrypted DB fields (2FA, Kite tokens) become unreadable.
            dataProtectionBuilder.UseEphemeralDataProtectionProvider();
        }
    }

    private static void AddSwagger(WebApplicationBuilder builder)
    {
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
                Description = "JWT Bearer token.",
            };
            c.AddSecurityDefinition("Bearer", scheme);
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
                    },
                    Array.Empty<string>()
                },
            });
        });
    }

    private static void AddJwtAuthentication(WebApplicationBuilder builder)
    {
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
                var hasProcKey = Environment.GetEnvironmentVariable("Jwt__Key") is { Length: > 0 };
                var cfgIssuer = string.IsNullOrWhiteSpace(builder.Configuration["Jwt:Issuer"]) ? "missing" : "set";
                var cfgAudience = string.IsNullOrWhiteSpace(builder.Configuration["Jwt:Audience"]) ? "missing" : "set";
                var cfgKeyLen = string.IsNullOrEmpty(builder.Configuration["Jwt:Key"])
                    ? 0
                    : Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!).Length;

                throw new InvalidOperationException(
                    $"JWT is not configured for this environment (ASPNETCORE_ENVIRONMENT={builder.Environment.EnvironmentName}). " +
                    $"Config: Jwt:Issuer={cfgIssuer}, Jwt:Audience={cfgAudience}, Jwt:Key UTF8 length={cfgKeyLen}. " +
                    $"Process env Jwt__Key present={(hasProcKey ? "yes" : "no")}. " +
                    "On DigitalOcean App Platform, add to the **Web Service** (API) component with **RUN_TIME** scope (Encrypt secrets): " +
                    "**Jwt__Issuer**, **Jwt__Audience**, **Jwt__Key** (≥ 32 UTF-8 bytes), **Cors__Origins__0**. " +
                    "Names use double underscores (__). App-level env vars must be attached to this component. " +
                    "If Jwt__Key is set in the UI but this still says Jwt__Key present=no, the variable name or scope is wrong.");
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
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(accessToken)
                            && context.Request.Path.StartsWithSegments("/hubs"))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    },
                    OnAuthenticationFailed = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("Trader.Api.JwtBearer");
                        logger.LogWarning(
                            context.Exception,
                            "JWT rejected: {Path}. See inner exception for issuer/audience/signature/lifetime details.",
                            context.Request.Path);
                        return Task.CompletedTask;
                    },
                };
            });

        builder.Services.AddAuthorization();
    }

    private static void AddCors(WebApplicationBuilder builder)
    {
        var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                          ?? Array.Empty<string>();
        if (corsOrigins.Length == 0)
        {
            throw new InvalidOperationException(
                "Configure CORS via Cors:Origins or environment variables (e.g. Cors__Origins__0) for this environment.");
        }

        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
                policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials());
        });
    }

    private static void AddRateLimiting(IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);

                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/problem+json";
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many login attempts",
                        Detail = "Please wait and try logging in again.",
                    },
                    cancellationToken: ct);
            };

            options.AddPolicy("auth-login", httpContext =>
            {
                var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: $"auth-login:{ip}",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 8,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
            });
        });
    }
}
