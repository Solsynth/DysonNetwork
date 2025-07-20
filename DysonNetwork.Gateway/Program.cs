using DysonNetwork.Gateway.Startup;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddGateway(builder.Configuration);
builder.Services.AddControllers();

var app = builder.Build();

app.UseRequestTimeouts();
app.UseCors(opts =>
    opts.SetIsOriginAllowed(_ => true)
        .WithExposedHeaders("*")
        .WithHeaders()
        .AllowCredentials()
        .AllowAnyHeader()
        .AllowAnyMethod()
);

app.MapControllers();
app.MapReverseProxy();

app.Run();
