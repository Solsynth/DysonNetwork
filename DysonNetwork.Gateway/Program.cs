using System.Threading.RateLimiting;
using DysonNetwork.Shared.Http;
using Yarp.ReverseProxy.Configuration;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.ConfigureAppKestrel(builder.Configuration, maxRequestBodySize: long.MaxValue, enableGrpc: false);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(
        policy =>
        {
            policy.SetIsOriginAllowed(origin => true)
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials()
                .WithExposedHeaders("X-Total");
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

var serviceNames = new[] { "ring", "pass", "drive", "sphere", "develop", "insight", "zone" };

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
        RouteId = "drive-tus",
        ClusterId = "drive",
        Match = new RouteMatch { Path = "/api/tus" }
    }
};

var apiRoutes = serviceNames.Select(serviceName =>
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

var swaggerRoutes = serviceNames.Select(serviceName => new RouteConfig
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

var clusters = serviceNames.Select(serviceName => new ClusterConfig
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

builder.Services.AddControllers();

var app = builder.Build();

var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.All
};
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

app.UseCors();

app.MapReverseProxy().RequireRateLimiting("fixed");

app.MapControllers();

app.Run();
