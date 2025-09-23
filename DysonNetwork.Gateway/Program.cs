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
                .AllowCredentials();
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

var routes = new[]
{
    new RouteConfig()
    {
        RouteId = "ring-ws",
        ClusterId = "ring",
        Match = new RouteMatch { Path = "/ws" }
    },
    new RouteConfig()
    {
        RouteId = "ring-api",
        ClusterId = "ring",
        Match = new RouteMatch { Path = "/ring/{**catch-all}" },
        Transforms =
        [
            new Dictionary<string, string> { { "PathRemovePrefix", "/ring" } },
            new Dictionary<string, string> { { "PathPrefix", "/api" } }
        ]
    },
    new RouteConfig()
    {
        RouteId = "pass-openid",
        ClusterId = "pass",
        Match = new RouteMatch { Path = "/.well-known/openid-configuration" }
    },
    new RouteConfig()
    {
        RouteId = "pass-jwks",
        ClusterId = "pass",
        Match = new RouteMatch { Path = "/.well-known/jwks" }
    },
    new RouteConfig()
    {
        RouteId = "pass-api",
        ClusterId = "pass",
        Match = new RouteMatch { Path = "/id/{**catch-all}" },
        Transforms =
        [
            new Dictionary<string, string> { { "PathRemovePrefix", "/id" } },
            new Dictionary<string, string> { { "PathPrefix", "/api" } }
        ]
    },
    new RouteConfig()
    {
        RouteId = "drive-tus",
        ClusterId = "drive",
        Match = new RouteMatch { Path = "/api/tus" }
    },
    new RouteConfig()
    {
        RouteId = "drive-api",
        ClusterId = "drive",
        Match = new RouteMatch { Path = "/drive/{**catch-all}" },
        Transforms =
        [
            new Dictionary<string, string> { { "PathRemovePrefix", "/drive" } },
            new Dictionary<string, string> { { "PathPrefix", "/api" } }
        ]
    },
    new RouteConfig()
    {
        RouteId = "sphere-api",
        ClusterId = "sphere",
        Match = new RouteMatch { Path = "/sphere/{**catch-all}" },
        Transforms =
        [
            new Dictionary<string, string> { { "PathRemovePrefix", "/sphere" } },
            new Dictionary<string, string> { { "PathPrefix", "/api" } }
        ]
    },
    new RouteConfig()
    {
        RouteId = "develop-api",
        ClusterId = "develop",
        Match = new RouteMatch { Path = "/develop/{**catch-all}" },
        Transforms =
        [
            new Dictionary<string, string> { { "PathRemovePrefix", "/develop" } },
            new Dictionary<string, string> { { "PathPrefix", "/api" } }
        ]
    }
};

var clusters = new[]
{
    new ClusterConfig()
    {
        ClusterId = "ring",
        Destinations = new Dictionary<string, DestinationConfig>
        {
            { "destination1", new DestinationConfig() { Address = "http://ring" } }
        }
    },
    new ClusterConfig()
    {
        ClusterId = "pass",
        Destinations = new Dictionary<string, DestinationConfig>
        {
            { "destination1", new DestinationConfig() { Address = "http://pass" } }
        }
    },
    new ClusterConfig()
    {
        ClusterId = "drive",
        Destinations = new Dictionary<string, DestinationConfig>
        {
            { "destination1", new DestinationConfig() { Address = "http://drive" } }
        }
    },
    new ClusterConfig()
    {
        ClusterId = "sphere",
        Destinations = new Dictionary<string, DestinationConfig>
        {
            { "destination1", new DestinationConfig() { Address = "http://sphere" } }
        }
    },
    new ClusterConfig()
    {
        ClusterId = "develop",
        Destinations = new Dictionary<string, DestinationConfig>
        {
            { "destination1", new DestinationConfig() { Address = "http://develop" } }
        }
    }
};

builder.Services
.AddReverseProxy()
.LoadFromMemory(routes, clusters)
.AddServiceDiscoveryDestinationResolver();

var app = builder.Build();

app.UseCors();

app.UseRateLimiter();

app.MapReverseProxy().RequireRateLimiting("fixed");

app.Run();
