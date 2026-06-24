using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Messager;
using DysonNetwork.Messager.Startup;
using DysonNetwork.Shared.Networking;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.ConfigureAppKestrel(builder.Configuration);

builder.Services.AddAppServices();
builder.Services.AddAppAuthentication();
builder.Services.AddDysonAuth(builder.Configuration);
builder.Services.AddAccountService(builder.Configuration);
builder.Services.AddBladeService(builder.Configuration);
builder.Services.AddRingService(builder.Configuration);
builder.Services.AddDriveService(builder.Configuration);
builder.Services.AddSphereService(builder.Configuration);
builder.Services.AddWalletService(builder.Configuration);
builder.Services.AddDevelopService(builder.Configuration);

builder.Services.AddAppBusinessServices(builder.Configuration);
builder.Services.AddAppScheduledJobs(builder.Configuration);
builder.Services.AddMlsService(builder.Configuration);

builder.AddSwaggerManifest(
    "DysonNetwork.Messager",
    "The real-time messaging service in the Solar Network."
);

var app = builder.Build();

app.MapDefaultEndpoints();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
    await db.Database.MigrateAsync();
}

app.ConfigureAppMiddleware(builder.Configuration);

app.UseSwaggerManifest("DysonNetwork.Messager");

app.Run();
