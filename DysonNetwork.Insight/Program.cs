using DysonNetwork.Insight;
using DysonNetwork.Insight.Startup;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.ConfigureAppKestrel(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddAppServices();
builder.Services.AddAppAuthentication();
builder.Services.AddAppFlushHandlers();
builder.Services.AddAppBusinessServices();
builder.Services.AddAppScheduledJobs();

builder.Services.AddDysonAuth();
builder.Services.AddAccountService();
builder.Services.AddSphereService();
builder.Services.AddThinkingServices(builder.Configuration);

builder.AddSwaggerManifest(
    "DysonNetwork.Insight",
    "The insight service in the Solar Network."
);

var app = builder.Build();

app.MapDefaultEndpoints();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
    await db.Database.MigrateAsync();
}

app.ConfigureAppMiddleware(builder.Configuration);

app.UseSwaggerManifest("DysonNetwork.Insight");

app.Run();
