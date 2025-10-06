using System.Threading.RateLimiting;
using DysonNetwork.Shared.Http;
using Microsoft.AspNetCore.RateLimiting;
using Yarp.ReverseProxy.Configuration;

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
    options.AddFixedWindowLimiter("fixed", limiterOptions =>
    {
        limiterOptions.PermitLimit = 120;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 0;
    });
});

var serviceNames = new[] { "ring", "pass", "drive", "sphere", "develop" };

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
        "pass" => "/id",
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
    HealthCheck = new()
    {
        Active = new()
        {
            Enabled = true,
            Interval = TimeSpan.FromSeconds(10),
            Timeout = TimeSpan.FromSeconds(5),
            Policy = "ActiveHealthy",
            Path = "/health"
        },
        Passive = new()
        {
            Enabled = true,
            Policy = "PassiveHealthy"
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

app.UseCors();

app.UseRateLimiter();

app.MapReverseProxy().RequireRateLimiting("fixed");

app.MapControllers();

app.Run();
