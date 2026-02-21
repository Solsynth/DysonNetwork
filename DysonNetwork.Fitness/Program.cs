using DysonNetwork.Fitness;
using DysonNetwork.Fitness.Startup;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.ConfigureAppKestrel(builder.Configuration);

builder.Services.AddAppServices(builder.Configuration);
builder.Services.AddAppAuthentication();
builder.Services.AddDysonAuth();
builder.Services.AddSphereService();
builder.Services.AddAccountService();

// Add scheduled jobs
// builder.Services.AddAppScheduledJobs();

builder.AddSwaggerManifest(
    "DysonNetwork.Fitness",
    "The fitness tracking service in the Solar Network."
);

var app = builder.Build();

app.MapDefaultEndpoints();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
    await db.Database.MigrateAsync();
}

app.ConfigureAppMiddleware(builder.Configuration);

app.UseSwaggerManifest("DysonNetwork.Fitness");

app.Run();
