using Aspire.Hosting.Yarp.Transforms;

var builder = DistributedApplication.CreateBuilder(args);

var database = builder.AddPostgres("database");
var cache = builder.AddConnectionString("cache");
var queue = builder.AddNats("queue").WithJetStream();

var ringService = builder.AddProject<Projects.DysonNetwork_Ring>("ring")
    .WithReference(database)
    .WithReference(queue);
var passService = builder.AddProject<Projects.DysonNetwork_Pass>("pass")
    .WithReference(database)
    .WithReference(cache)
    .WithReference(queue)
    .WithReference(ringService);
var driveService = builder.AddProject<Projects.DysonNetwork_Drive>("drive")
    .WithReference(database)
    .WithReference(cache)
    .WithReference(queue)
    .WithReference(passService);
var sphereService = builder.AddProject<Projects.DysonNetwork_Sphere>("sphere")
    .WithReference(database)
    .WithReference(cache)
    .WithReference(queue)
    .WithReference(passService);
var developService = builder.AddProject<Projects.DysonNetwork_Develop>("develop")
    .WithReference(database)
    .WithReference(cache)
    .WithReference(passService);

var gateway = builder.AddYarp("gateway")
    .WithHostPort(5000)
    .WithConfiguration(yarp =>
    {
        yarp.AddRoute("/ring/{**catch-all}", ringService)
            .WithTransformPathRemovePrefix("/ring")
            .WithTransformPathPrefix("/api");
        yarp.AddRoute("/id/{**catch-all}", passService)
            .WithTransformPathRemovePrefix("/id")
            .WithTransformPathPrefix("/api");
        yarp.AddRoute("/drive/{**catch-all}", driveService)
            .WithTransformPathRemovePrefix("/drive")
            .WithTransformPathPrefix("/api");
        yarp.AddRoute("/sphere/{**catch-all}", sphereService)
            .WithTransformPathRemovePrefix("/sphere")
            .WithTransformPathPrefix("/api");
        yarp.AddRoute("/develop/{**catch-all}", developService)
            .WithTransformPathRemovePrefix("/develop")
            .WithTransformPathPrefix("/api");
    });

builder.Build().Run();