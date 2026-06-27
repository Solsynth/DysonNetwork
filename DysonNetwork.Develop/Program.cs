using DysonNetwork.Develop;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Develop.Startup;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.ConfigureAppKestrel(builder.Configuration);

builder.Services.AddAppServices(builder.Configuration);
builder.Services.AddAppAuthentication();
builder.Services.AddDysonAuth(builder.Configuration);
builder.Services.AddSphereService(builder.Configuration);
builder.Services.AddAccountService(builder.Configuration);
builder.Services.AddDriveService(builder.Configuration);

builder.AddSwaggerManifest(
    "DysonNetwork.Develop",
    "The developer portal in the Solar Network."
);

var app = builder.Build();

app.MapDefaultEndpoints();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
    await db.Database.MigrateAsync();
}

app.ConfigureAppMiddleware(builder.Configuration);

app.UseSwaggerManifest("DysonNetwork.Develop");

app.Run();