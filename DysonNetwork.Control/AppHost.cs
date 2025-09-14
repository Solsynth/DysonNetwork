var builder = DistributedApplication.CreateBuilder(args);

var database = builder.AddPostgres("database");
var cache = builder.AddConnectionString("cache");
var queue = builder.AddNats("queue").WithJetStream();

var ring = builder.AddProject<Projects.DysonNetwork_Ring>("ring")
    .WithReference(database)
    .WithReference(queue);
var pass = builder.AddProject<Projects.DysonNetwork_Pass>("pass")
    .WithReference(database)
    .WithReference(cache)
    .WithReference(queue)
    .WithReference(ring);
builder.AddProject<Projects.DysonNetwork_Drive>("drive")
    .WithReference(database)
    .WithReference(cache)
    .WithReference(queue)
    .WithReference(pass);
builder.AddProject<Projects.DysonNetwork_Sphere>("sphere")
    .WithReference(database)
    .WithReference(cache)
    .WithReference(queue)
    .WithReference(pass);
builder.AddProject<Projects.DysonNetwork_Develop>("develop")
    .WithReference(database)
    .WithReference(cache)
    .WithReference(pass);

builder.Build().Run();