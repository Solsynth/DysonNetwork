using DysonNetwork.Zone;
using DysonNetwork.Zone.Startup;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Http;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.ConfigureAppKestrel(builder.Configuration);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddControllers();

builder.Services.AddAppServices();
builder.Services.AddAppAuthentication();
builder.Services.AddAppFlushHandlers();
builder.Services.AddAppBusinessServices(builder.Configuration);
builder.Services.AddAppScheduledJobs();

builder.Services.AddDysonAuth();
builder.Services.AddAccountService();
builder.Services.AddSphereService();

builder.AddSwaggerManifest(
    "DysonNetwork.Zone",
    "The zone service in the Solar Network."
);

var app = builder.Build();

app.MapDefaultEndpoints();

// Run database migrations
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
    await db.Database.MigrateAsync();
}


// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

app.ConfigureAppMiddleware(builder.Configuration);

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

app.UseSwaggerManifest("DysonNetwork.Zone");

app.Run();