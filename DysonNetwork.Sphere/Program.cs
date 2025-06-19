using DysonNetwork.Sphere;
using DysonNetwork.Sphere.Startup;
using Microsoft.EntityFrameworkCore;
using tusdotnet.Stores;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel and server options
builder.ConfigureAppKestrel();

// Add metrics and telemetry
builder.Services.AddAppMetrics();

// Add application services
builder.Services.AddAppServices(builder.Configuration);
builder.Services.AddAppRateLimiting();
builder.Services.AddAppAuthentication();
builder.Services.AddAppSwagger();

// Add file storage
builder.Services.AddAppFileStorage(builder.Configuration);

// Add flush handlers and websocket handlers
builder.Services.AddAppFlushHandlers();

// Add business services
builder.Services.AddAppBusinessServices(builder.Configuration);

// Add scheduled jobs
builder.Services.AddAppScheduledJobs();

var app = builder.Build();

// Run database migrations
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
    await db.Database.MigrateAsync();
}

// Get the TusDiskStore instance
var tusDiskStore = app.Services.GetRequiredService<TusDiskStore>();

// Configure application middleware pipeline
app.ConfigureAppMiddleware(builder.Configuration, tusDiskStore);

app.Run();