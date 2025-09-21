using Aspire.Hosting.Yarp.Transforms;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var isDev = builder.Environment.IsDevelopment();

// Database was configured separately in each service.
// var database = builder.AddPostgres("database");

var cache = builder.AddRedis("cache");
var queue = builder.AddNats("queue").WithJetStream();

var ringService = builder.AddProject<Projects.DysonNetwork_Ring>("ring")
    .WithReference(queue);
var passService = builder.AddProject<Projects.DysonNetwork_Pass>("pass")
    .WithReference(cache)
    .WithReference(queue)
    .WithReference(ringService);
var driveService = builder.AddProject<Projects.DysonNetwork_Drive>("drive")
    .WithReference(cache)
    .WithReference(queue)
    .WithReference(passService)
    .WithReference(ringService);
var sphereService = builder.AddProject<Projects.DysonNetwork_Sphere>("sphere")
    .WithReference(cache)
    .WithReference(queue)
    .WithReference(passService)
    .WithReference(ringService)
    .WithReference(driveService);
var developService = builder.AddProject<Projects.DysonNetwork_Develop>("develop")
    .WithReference(cache)
    .WithReference(passService)
    .WithReference(ringService);

List<IResourceBuilder<ProjectResource>> services =
    [ringService, passService, driveService, sphereService, developService];

for (var idx = 0; idx < services.Count; idx++)
{
    var service = services[idx];
    var grpcPort = 7002 + idx;

    if (isDev)
    {
        service.WithEnvironment("GRPC_PORT", grpcPort.ToString());

        var httpPort = 8001 + idx;
        service.WithEnvironment("HTTP_PORTS", httpPort.ToString());
        service.WithHttpEndpoint(httpPort, targetPort: null, isProxied: false, name: "http");
    }
    else
    {
        service.WithHttpEndpoint(8080, targetPort: null, isProxied: false, name: "http");
    }

    service.WithEndpoint(isDev ? grpcPort : 7001, isDev ? null : 7001, "https", name: "grpc", isProxied: false);
}

// Extra double-ended references
ringService.WithReference(passService);

var gateway = builder.AddYarp("gateway")
    .WithConfiguration(yarp =>
    {
        var ringCluster = yarp.AddCluster(ringService.GetEndpoint("http"));
        yarp.AddRoute("/ws", ringCluster);
        yarp.AddRoute("/ring/{**catch-all}", ringCluster)
            .WithTransformPathRemovePrefix("/ring")
            .WithTransformPathPrefix("/api");
        var passCluster = yarp.AddCluster(passService.GetEndpoint("http"));
        yarp.AddRoute("/.well-known/openid-configuration", passCluster);
        yarp.AddRoute("/.well-known/jwks", passCluster);
        yarp.AddRoute("/id/{**catch-all}", passCluster)
            .WithTransformPathRemovePrefix("/id")
            .WithTransformPathPrefix("/api");
        var driveCluster = yarp.AddCluster(driveService.GetEndpoint("http"));
        yarp.AddRoute("/api/tus", driveCluster);
        yarp.AddRoute("/drive/{**catch-all}", driveCluster)
            .WithTransformPathRemovePrefix("/drive")
            .WithTransformPathPrefix("/api");
        var sphereCluster = yarp.AddCluster(sphereService.GetEndpoint("http"));
        yarp.AddRoute("/sphere/{**catch-all}", sphereCluster)
            .WithTransformPathRemovePrefix("/sphere")
            .WithTransformPathPrefix("/api");
        var developCluster = yarp.AddCluster(developService.GetEndpoint("http"));
        yarp.AddRoute("/develop/{**catch-all}", developCluster)
            .WithTransformPathRemovePrefix("/develop")
            .WithTransformPathPrefix("/api");
    });

if (isDev) gateway.WithHostPort(5001);

builder.AddDockerComposeEnvironment("docker-compose");

builder.Build().Run();