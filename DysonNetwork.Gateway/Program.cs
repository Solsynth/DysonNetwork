using DysonNetwork.Gateway.Startup;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseContentRoot(Directory.GetCurrentDirectory());
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = long.MaxValue;
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
});

// Add services to the container.
builder.Services.AddGateway(builder.Configuration);
builder.Services.AddControllers();

var app = builder.Build();

app.UseRequestTimeouts();
app.UseCors(opts =>
    opts.SetIsOriginAllowed(_ => true)
        .WithExposedHeaders("*")
        .WithHeaders("*")
        .AllowCredentials()
        .AllowAnyHeader()
        .AllowAnyMethod()
);

app.MapControllers();
app.MapReverseProxy();

app.Run();
