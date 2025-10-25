using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var isDev = builder.Environment.IsDevelopment();

var cache = builder.AddRedis("cache");
var queue = builder.AddNats("queue").WithJetStream();

var ringService = builder.AddProject<Projects.DysonNetwork_Ring>("ring");
var passService = builder.AddProject<Projects.DysonNetwork_Pass>("pass")
    .WithReference(ringService);
var driveService = builder.AddProject<Projects.DysonNetwork_Drive>("drive")
    .WithReference(passService)
    .WithReference(ringService);
var sphereService = builder.AddProject<Projects.DysonNetwork_Sphere>("sphere")
    .WithReference(passService)
    .WithReference(ringService)
    .WithReference(driveService);
var developService = builder.AddProject<Projects.DysonNetwork_Develop>("develop")
    .WithReference(passService)
    .WithReference(ringService)
    .WithReference(sphereService);
var insightService = builder.AddProject<Projects.DysonNetwork_Insight>("insight")
    .WithReference(passService)
    .WithReference(ringService)
    .WithReference(sphereService)
    .WithReference(developService);

passService.WithReference(developService).WithReference(driveService);

List<IResourceBuilder<ProjectResource>> services =
    [ringService, passService, driveService, sphereService, developService, insightService];

for (var idx = 0; idx < services.Count; idx++)
{
    var service = services[idx];

    service.WithReference(cache).WithReference(queue);

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
    .WithEnvironment("HTTP_PORTS", "5001")
    .WithHttpEndpoint(port: 5001, targetPort: null, isProxied: false, name: "http");

foreach (var service in services)
    gateway.WithReference(service);

builder.AddDockerComposeEnvironment("docker-compose");

builder.Build().Run();
