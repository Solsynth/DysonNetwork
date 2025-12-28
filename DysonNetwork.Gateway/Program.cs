using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using DysonNetwork.Gateway.Health;
using DysonNetwork.Shared.Http;
using Yarp.ReverseProxy.Configuration;
using Microsoft.AspNetCore.HttpOverrides;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.ConfigureAppKestrel(builder.Configuration, maxRequestBodySize: long.MaxValue, enableGrpc: false);

builder.Services.AddSingleton<GatewayReadinessStore>();
builder.Services.AddHostedService<GatewayHealthAggregator>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.SetIsOriginAllowed(origin => true)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials()
            .WithExposedHeaders("X-Total", "X-NotReady");
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("fixed", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ip,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120, // 120 requests...
                Window = TimeSpan.FromMinutes(1), // ...per minute per IP
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 10 // allow short bursts instead of instant 503s
            });
    });

    options.OnRejected = async (context, token) =>
    {
        // Log the rejected IP
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("RateLimiter");

        var ip = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        logger.LogWarning("Rate limit exceeded for IP: {IP}", ip);

        // Respond to the client
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsync(
            "Rate limit exceeded. Try again later.", token);
    };
});


var specialRoutes = new[]
{
    new RouteConfig
    {
        RouteId = "ring-ws",
        ClusterId = "ring",
        Match = new RouteMatch { Path = "/ws" }
    },
    new RouteConfig
    {
        RouteId = "pass-openid",
        ClusterId = "pass",
        Match = new RouteMatch { Path = "/.well-known/openid-configuration" }
    },
    new RouteConfig
    {
        RouteId = "pass-jwks",
        ClusterId = "pass",
        Match = new RouteMatch { Path = "/.well-known/jwks" }
    },
    new RouteConfig
    {
        RouteId = "sphere-webfinger",
        ClusterId = "sphere",
        Match = new RouteMatch { Path = "/.well-known/webfinger" }
    },
    new RouteConfig
    {
        RouteId = "sphere-activitypub",
        ClusterId = "sphere",
        Match = new RouteMatch { Path = "activitypub" }
    },
    new RouteConfig
    {
        RouteId = "drive-tus",
        ClusterId = "drive",
        Match = new RouteMatch { Path = "/api/tus" }
    }
};

var apiRoutes = GatewayConstant.ServiceNames.Select(serviceName =>
{
    var apiPath = serviceName switch
    {
        _ => $"/{serviceName}"
    };
    return new RouteConfig
    {
        RouteId = $"{serviceName}-api",
        ClusterId = serviceName,
        Match = new RouteMatch { Path = $"{apiPath}/{{**catch-all}}" },
        Transforms =
        [
            new Dictionary<string, string> { { "PathRemovePrefix", apiPath } },
            new Dictionary<string, string> { { "PathPrefix", "/api" } }
        ]
    };
});

var swaggerRoutes = GatewayConstant.ServiceNames.Select(serviceName => new RouteConfig
{
    RouteId = $"{serviceName}-swagger",
    ClusterId = serviceName,
    Match = new RouteMatch { Path = $"/swagger/{serviceName}/{{**catch-all}}" },
    Transforms =
    [
        new Dictionary<string, string> { { "PathRemovePrefix", $"/swagger/{serviceName}" } },
        new Dictionary<string, string> { { "PathPrefix", "/swagger" } }
    ]
});

var routes = specialRoutes.Concat(apiRoutes).Concat(swaggerRoutes).ToArray();

var clusters = GatewayConstant.ServiceNames.Select(serviceName => new ClusterConfig
{
    ClusterId = serviceName,
    HealthCheck = new HealthCheckConfig
    {
        Active = new ActiveHealthCheckConfig
        {
            Enabled = true,
            Interval = TimeSpan.FromSeconds(10),
            Timeout = TimeSpan.FromSeconds(5),
            Path = "/health"
        },
        Passive = new PassiveHealthCheckConfig
        {
            Enabled = true
        }
    },
    Destinations = new Dictionary<string, DestinationConfig>
    {
        { "destination1", new DestinationConfig { Address = $"http://{serviceName}" } }
    }
}).ToArray();

builder.Services
    .AddReverseProxy()
    .LoadFromMemory(routes, clusters)
    .AddServiceDiscoveryDestinationResolver();

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals;
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;

    options.JsonSerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
});

var app = builder.Build();

var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.All
};
forwardedHeadersOptions.KnownIPNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

app.UseCors();

app.UseMiddleware<GatewayReadinessMiddleware>();

app.MapReverseProxy().RequireRateLimiting("fixed");

app.MapControllers();

app.Run();