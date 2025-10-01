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
    .WithReference(ringService)
    .WithReference(sphereService);

passService.WithReference(developService).WithReference(driveService);

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

var gateway = builder.AddProject<Projects.DysonNetwork_Gateway>("gateway")
    .WithReference(ringService)
    .WithReference(passService)
    .WithReference(driveService)
    .WithReference(sphereService)
    .WithReference(developService)
    .WithEnvironment("HTTP_PORTS", "5001")
    .WithHttpEndpoint(port: 5001, targetPort: null, isProxied: false, name: "http");

builder.AddDockerComposeEnvironment("docker-compose");

builder.Build().Run();
