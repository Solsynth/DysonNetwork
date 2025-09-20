using Aspire.Hosting.Yarp.Transforms;

var builder = DistributedApplication.CreateBuilder(args);

// Database was configured separately in each service.
// var database = builder.AddPostgres("database");

var cache = builder.AddRedis("cache");
var queue = builder.AddNats("queue").WithJetStream();

var ringService = builder.AddProject<Projects.DysonNetwork_Ring>("ring")
    .WithReference(queue)
    .WithHttpHealthCheck()
    .WithEndpoint(5001, 5001, "https", name: "grpc");
var passService = builder.AddProject<Projects.DysonNetwork_Pass>("pass")
    .WithReference(cache)
    .WithReference(queue)
    .WithReference(ringService)
    .WithHttpHealthCheck()
    .WithEndpoint(5001, 5001, "https", name: "grpc");
var driveService = builder.AddProject<Projects.DysonNetwork_Drive>("drive")
    .WithReference(cache)
    .WithReference(queue)
    .WithReference(passService)
    .WithReference(ringService)
    .WithHttpHealthCheck()
    .WithEndpoint(5001, 5001, "https", name: "grpc");
var sphereService = builder.AddProject<Projects.DysonNetwork_Sphere>("sphere")
    .WithReference(cache)
    .WithReference(queue)
    .WithReference(passService)
    .WithReference(ringService)
    .WithReference(driveService)
    .WithHttpHealthCheck()
    .WithEndpoint(5001, 5001, "https", name: "grpc");
var developService = builder.AddProject<Projects.DysonNetwork_Develop>("develop")
    .WithReference(cache)
    .WithReference(passService)
    .WithReference(ringService)
    .WithHttpHealthCheck()
    .WithEndpoint(5001, 5001, "https", name: "grpc");

// Extra double-ended references
ringService.WithReference(passService);

builder.AddYarp("gateway")
    .WithHostPort(5000)
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

builder.AddDockerComposeEnvironment("docker-compose");

builder.Build().Run();
