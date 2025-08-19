using DysonNetwork.Develop;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Http;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Develop.Startup;
using DysonNetwork.Shared.Stream;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.ConfigureAppKestrel(builder.Configuration);

builder.Services.AddRegistryService(builder.Configuration);
builder.Services.AddStreamConnection(builder.Configuration);
builder.Services.AddAppServices(builder.Configuration);
builder.Services.AddAppAuthentication();
builder.Services.AddAppSwagger();
builder.Services.AddDysonAuth();
builder.Services.AddPublisherService();
builder.Services.AddAccountService();
builder.Services.AddDriveService();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
    await db.Database.MigrateAsync();
}

app.ConfigureAppMiddleware(builder.Configuration);

app.Run();