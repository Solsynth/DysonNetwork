using DysonNetwork.Pass;
using DysonNetwork.Pass.Startup;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Configure Kestrel and server options
builder.ConfigureAppKestrel(builder.Configuration);

// Add application services
builder.Services.AddAppServices(builder.Configuration);
builder.Services.AddAppAuthentication();
builder.Services.AddRingService();
builder.Services.AddDriveService();
builder.Services.AddDevelopService();
builder.Services.AddWalletService();

builder.Services.AddAppFlushHandlers();
builder.Services.AddAppBusinessServices(builder.Configuration);
builder.Services.AddAppScheduledJobs();

builder.AddSwaggerManifest(
    "DysonNetwork.Pass",
    "The authentication and authorization service in the Solar Network."
);

var app = builder.Build();

app.MapDefaultEndpoints();

// Run database migrations
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
    await db.Database.MigrateAsync();
}

// Configure application middleware pipeline
app.ConfigureAppMiddleware(builder.Configuration);

// Configure gRPC
app.ConfigureGrpcServices();

app.UseSwaggerManifest("DysonNetwork.Pass");

app.Run();