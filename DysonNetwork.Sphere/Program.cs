using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Http;
using DysonNetwork.Shared.PageData;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Shared.Stream;
using DysonNetwork.Sphere;
using DysonNetwork.Sphere.PageData;
using DysonNetwork.Sphere.Startup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel and server options
builder.ConfigureAppKestrel(builder.Configuration);

// Add metrics and telemetry
builder.Services.AddAppMetrics();

// Add application services
builder.Services.AddRegistryService(builder.Configuration);
builder.Services.AddStreamConnection(builder.Configuration);
builder.Services.AddAppServices(builder.Configuration);
builder.Services.AddAppRateLimiting();
builder.Services.AddAppAuthentication();
builder.Services.AddAppSwagger();
builder.Services.AddDysonAuth();
builder.Services.AddAccountService();
builder.Services.AddRingService();
builder.Services.AddDriveService();

// Add flush handlers and websocket handlers
builder.Services.AddAppFlushHandlers();

// Add business services
builder.Services.AddAppBusinessServices(builder.Configuration);

// Add scheduled jobs
builder.Services.AddAppScheduledJobs();

builder.Services.AddTransient<IPageDataProvider, PostPageData>();

var app = builder.Build();

// Run database migrations
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
    await db.Database.MigrateAsync();
}

// Configure application middleware pipeline
app.ConfigureAppMiddleware(builder.Configuration);

app.MapGatewayProxy();

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(app.Environment.ContentRootPath, "wwwroot", "dist"))
});
    
app.MapPages(Path.Combine(app.Environment.WebRootPath, "dist", "index.html"));

app.Run();