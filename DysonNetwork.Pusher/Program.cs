using DysonNetwork.Pass.Startup;
using DysonNetwork.Pusher;
using DysonNetwork.Pusher.Startup;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel and server options
builder.ConfigureAppKestrel();

// Add application services
builder.Services.AddAppServices(builder.Configuration);
builder.Services.AddAppRateLimiting();
builder.Services.AddAppAuthentication();
builder.Services.AddAppSwagger();

// Add flush handlers and websocket handlers
builder.Services.AddAppFlushHandlers();

// Add business services
builder.Services.AddAppBusinessServices();

// Add scheduled jobs
builder.Services.AddAppScheduledJobs();

builder.Services.AddHostedService<ServiceRegistrationHostedService>();

var app = builder.Build();

// Run database migrations
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
    await db.Database.MigrateAsync();
}

// Configure application middleware pipeline
app.ConfigureAppMiddleware(builder.Configuration);

app.UseMiddleware<DysonNetwork.Shared.Middleware.AuthMiddleware>();

// Configure gRPC
app.ConfigureGrpcServices();

app.Run();