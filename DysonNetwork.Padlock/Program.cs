using DysonNetwork.Padlock;
using DysonNetwork.Padlock.Permission;
using DysonNetwork.Padlock.Startup;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.ConfigureAppKestrel(builder.Configuration);

builder.Services.AddAppServices(builder.Configuration);
builder.Services.AddAppAuthentication();
builder.Services.AddBladeService();
builder.Services.AddRingService();
builder.Services.AddDriveService();
builder.Services.AddDevelopService();
builder.Services.AddWalletService();

builder.Services.AddAppFlushHandlers();
builder.Services.AddAppBusinessServices(builder.Configuration);

builder.AddSwaggerManifest(
    "DysonNetwork.Padlock",
    "The authentication and authorization service in the Solar Network."
);

var app = builder.Build();

app.MapDefaultEndpoints();
app.UseMiddleware<LocalPermissionMiddleware>();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
    await db.Database.MigrateAsync();
}

app.ConfigureAppMiddleware(builder.Configuration);
app.ConfigureGrpcServices();

app.UseSwaggerManifest("DysonNetwork.Padlock");

app.Run();
