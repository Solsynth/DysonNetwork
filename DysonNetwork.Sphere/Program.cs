using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Http;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Sphere;
using DysonNetwork.Sphere.Startup;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.Configure<ServiceRegistrationOptions>(opts => { opts.Name = "sphere"; });

// Configure Kestrel and server options
builder.ConfigureAppKestrel(builder.Configuration);

// Add application services

builder.Services.AddAppServices();
builder.Services.AddAppAuthentication();
builder.Services.AddDysonAuth();

builder.Services.AddAppFlushHandlers();
builder.Services.AddAppBusinessServices(builder.Configuration);
builder.Services.AddAppScheduledJobs();

builder.AddSwaggerManifest(
    "DysonNetwork.Sphere",
    "The social network service in the Solar Network."
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

app.UseSwaggerManifest("DysonNetwork.Sphere");

app.Run();