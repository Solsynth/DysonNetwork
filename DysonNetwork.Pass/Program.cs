using DysonNetwork.Pass;
using DysonNetwork.Pass.Auth;
using DysonNetwork.Pass.Pages.Data;
using DysonNetwork.Pass.Startup;
using DysonNetwork.Shared.Http;
using DysonNetwork.Shared.PageData;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel and server options
builder.ConfigureAppKestrel(builder.Configuration);

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
builder.Services.AddDevelopService();

// Add flush handlers and websocket handlers
builder.Services.AddAppFlushHandlers();

// Add business services
builder.Services.AddAppBusinessServices(builder.Configuration);

// Add scheduled jobs
builder.Services.AddAppScheduledJobs();

builder.Services.AddTransient<IPageDataProvider, VersionPageData>();
builder.Services.AddTransient<IPageDataProvider, CaptchaPageData>();
builder.Services.AddTransient<IPageDataProvider, AccountPageData>();

var app = builder.Build();

// Run database migrations
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
    await db.Database.MigrateAsync();

    _ = Task.Run(async () =>
    {
        var migrationScope = app.Services.CreateScope();
        var migrationAuthService = migrationScope.ServiceProvider.GetRequiredService<AuthService>();
        await migrationAuthService.MigrateDeviceIdToClient();
    });
}

// Configure application middleware pipeline
app.ConfigureAppMiddleware(builder.Configuration, builder.Environment.ContentRootPath);

app.MapGatewayProxy();

app.MapPages(Path.Combine(builder.Environment.WebRootPath, "dist", "index.html"));

// Configure gRPC
app.ConfigureGrpcServices();

app.Run();