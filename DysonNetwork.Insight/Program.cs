using DysonNetwork.Insight;
using DysonNetwork.Insight.Startup;
using DysonNetwork.Shared.Http;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.ConfigureAppKestrel(builder.Configuration);

builder.Services.AddDbContext<AppDatabase>();

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