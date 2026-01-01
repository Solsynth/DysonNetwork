using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Http;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Messager;
using DysonNetwork.Messager.Startup;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.ConfigureAppKestrel(builder.Configuration);

builder.Services.AddAppServices();
builder.Services.AddAppAuthentication();
builder.Services.AddDysonAuth();
builder.Services.AddAccountService();
builder.Services.AddRingService();
builder.Services.AddDriveService();

builder.Services.AddAppBusinessServices(builder.Configuration);
builder.Services.AddAppScheduledJobs();

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
