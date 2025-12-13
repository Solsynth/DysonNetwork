using DysonNetwork.Develop;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Http;
using DysonNetwork.Develop.Startup;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("develop");

builder.Services.Configure<ServiceRegistrationOptions>(opts => { opts.Name = "develop"; });

builder.ConfigureAppKestrel(builder.Configuration);

builder.Services.AddAppServices(builder.Configuration);
builder.Services.AddAppAuthentication();
builder.Services.AddDysonAuth();

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