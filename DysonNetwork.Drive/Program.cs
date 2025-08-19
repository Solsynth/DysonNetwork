using DysonNetwork.Drive;
using DysonNetwork.Drive.Pages.Data;
using DysonNetwork.Drive.Startup;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Http;
using DysonNetwork.Shared.PageData;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Shared.Stream;
using Microsoft.EntityFrameworkCore;
using tusdotnet.Stores;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel and server options
builder.ConfigureAppKestrel(builder.Configuration, maxRequestBodySize: long.MaxValue);

// Add application services
builder.Services.AddRegistryService(builder.Configuration);
builder.Services.AddStreamConnection(builder.Configuration);
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

builder.Services.AddTransient<IPageDataProvider, VersionPageData>();

var app = builder.Build();

// Run database migrations
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
    await db.Database.MigrateAsync();
}

var tusDiskStore = app.Services.GetRequiredService<TusDiskStore>();

// Configure application middleware pipeline
app.ConfigureAppMiddleware(tusDiskStore, builder.Environment.ContentRootPath);

app.MapGatewayProxy();

app.MapPages(Path.Combine(app.Environment.WebRootPath, "dist", "index.html"));

// Configure gRPC
app.ConfigureGrpcServices();

app.Run();