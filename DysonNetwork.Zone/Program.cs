using DysonNetwork.Zone;
using DysonNetwork.Zone.Startup;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Http;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Zone.Publication;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("zone");

builder.Services.Configure<ServiceRegistrationOptions>(opts => { opts.Name = "zone"; });

builder.ConfigureAppKestrel(builder.Configuration);

builder.Services.AddRazorPages();
builder.Services.AddControllers();

builder.Services.AddAppServices();
builder.Services.AddAppAuthentication();
builder.Services.AddAppFlushHandlers();
builder.Services.AddAppBusinessServices(builder.Configuration);
builder.Services.AddAppScheduledJobs();

builder.Services.AddDysonAuth();

builder.Services.Configure<RouteOptions>(options => { options.LowercaseUrls = true; });

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

app.UseMiddleware<PublicationSiteMiddleware>();

app.UseStaticFiles();
app.UseRouting();
app.UseStatusCodePagesWithReExecute("/Error/{0}");
app.ConfigureAppMiddleware(builder.Configuration);
app.MapRazorPages();

app.UseSwaggerManifest("DysonNetwork.Zone");

app.Run();
