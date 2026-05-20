using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var isDev = builder.Environment.IsDevelopment();

var cache = builder.AddRedis("Cache");
var queue = builder.AddNats("Queue").WithJetStream();

var ringService = builder.AddProject<Projects.DysonNetwork_Ring>("ring");
var padlockService = builder.AddProject<Projects.DysonNetwork_Padlock>("padlock")
    .WithReference(ringService);
var passService = builder.AddProject<Projects.DysonNetwork_Passport>("passport")
    .WithReference(ringService)
    .WithReference(padlockService);
var sphereService = builder.AddProject<Projects.DysonNetwork_Sphere>("sphere")
    .WithReference(passService)
    .WithReference(ringService)
    .WithReference(padlockService);
var developService = builder.AddProject<Projects.DysonNetwork_Develop>("develop")
    .WithReference(passService)
    .WithReference(ringService)
    .WithReference(sphereService)
    .WithReference(padlockService);
var insightService = builder.AddProject<Projects.DysonNetwork_Insight>("insight")
    .WithReference(passService)
    .WithReference(ringService)
    .WithReference(sphereService)
    .WithReference(developService)
    .WithReference(padlockService);
var messagerService = builder.AddProject<Projects.DysonNetwork_Messager>("messager")
    .WithReference(passService)
    .WithReference(ringService)
    .WithReference(sphereService)
    .WithReference(developService)
    .WithReference(padlockService);
var walletService = builder.AddProject<Projects.DysonNetwork_Wallet>("wallet")
    .WithReference(passService)
    .WithReference(ringService)
    .WithReference(padlockService);

var bladeService = builder.AddExternalService("blade", "http://localhost:7001");

passService.WithReference(developService).WithReference(walletService);
padlockService.WithReference(walletService);
ringService.WithReference(padlockService);

List<IResourceBuilder<ProjectResource>> services =
[
    ringService,
    passService,
    sphereService,
    developService,
    insightService,
    messagerService,
    walletService,
    padlockService
];

for (var idx = 0; idx < services.Count; idx++)
{
    var service = services[idx];

    service.WithReference(cache).WithReference(queue).WithReference(bladeService);

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

builder.AddDockerComposeEnvironment("docker-compose");

builder.Build().Run();
