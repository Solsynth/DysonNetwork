using DysonNetwork.Pass;
using DysonNetwork.Pass.Pages.Data;
using DysonNetwork.Pass.Startup;
using DysonNetwork.Shared.Http;
using DysonNetwork.Shared.PageData;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel and server options
builder.ConfigureAppKestrel();

// Add metrics and telemetry
builder.Services.AddAppMetrics();

// Add application services
builder.Services.AddRegistryService(builder.Configuration);
builder.Services.AddAppServices(builder.Configuration);
builder.Services.AddAppRateLimiting();
builder.Services.AddAppAuthentication();
builder.Services.AddAppSwagger();
builder.Services.AddPusherService();
builder.Services.AddDriveService();

// Add flush handlers and websocket handlers
builder.Services.AddAppFlushHandlers();

// Add business services
builder.Services.AddAppBusinessServices(builder.Configuration);

// Add scheduled jobs
builder.Services.AddAppScheduledJobs();

builder.Services.AddTransient<IPageDataProvider, VersionPageData>();
builder.Services.AddTransient<IPageDataProvider, CaptchaPageData>();

var app = builder.Build();

// Run database migrations
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
    await db.Database.MigrateAsync();
}

// Configure application middleware pipeline
app.ConfigureAppMiddleware(builder.Configuration, builder.Environment.ContentRootPath);

app.MapPages(Path.Combine(builder.Environment.WebRootPath, "dist", "index.html"));

// Configure gRPC
app.ConfigureGrpcServices();

app.Run();