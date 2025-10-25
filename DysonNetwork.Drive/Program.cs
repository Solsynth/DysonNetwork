using DysonNetwork.Drive;
using DysonNetwork.Drive.Startup;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Http;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Configure Kestrel and server options
builder.ConfigureAppKestrel(builder.Configuration, maxRequestBodySize: long.MaxValue);

// Add application services

builder.Services.AddAppServices(builder.Configuration);
builder.Services.AddAppAuthentication();
builder.Services.AddDysonAuth();
builder.Services.AddAccountService();

builder.Services.AddAppFileStorage(builder.Configuration);

builder.Services.AddAppFlushHandlers();
builder.Services.AddAppBusinessServices();
builder.Services.AddAppScheduledJobs();

builder.AddSwaggerManifest(
    "DysonNetwork.Drive",
    "The file upload and storage service in the Solar Network."
);

var app = builder.Build();

app.MapDefaultEndpoints();

// Run database migrations
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
    await db.Database.MigrateAsync();
}

app.ConfigureAppMiddleware();

// Configure gRPC
app.ConfigureGrpcServices();

app.UseSwaggerManifest("DysonNetwork.Drive");

app.Run();
