using DysonNetwork.Drive;
using DysonNetwork.Drive.Startup;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Http;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;
using tusdotnet.Stores;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Configure Kestrel and server options
builder.ConfigureAppKestrel(builder.Configuration, maxRequestBodySize: long.MaxValue);

// Add application services

builder.Services.AddAppServices(builder.Configuration);
builder.Services.AddAppRateLimiting();
builder.Services.AddAppAuthentication();
builder.Services.AddAppSwagger();
builder.Services.AddDysonAuth();
builder.Services.AddAccountService();

builder.Services.AddAppFileStorage(builder.Configuration);

// Add flush handlers and websocket handlers
builder.Services.AddAppFlushHandlers();

// Add business services
builder.Services.AddAppBusinessServices();

// Add scheduled jobs
builder.Services.AddAppScheduledJobs();

var app = builder.Build();

app.MapDefaultEndpoints();

// Run database migrations
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
    await db.Database.MigrateAsync();
}

var tusDiskStore = app.Services.GetRequiredService<TusDiskStore>();
app.ConfigureAppMiddleware(tusDiskStore);

// Configure gRPC
app.ConfigureGrpcServices();

app.Run();
