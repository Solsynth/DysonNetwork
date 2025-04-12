using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Casbin;
using Casbin.Persist.Adapter.EFCore;
using DysonNetwork.Sphere;
using DysonNetwork.Sphere.Account;
using DysonNetwork.Sphere.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddDbContext<AppDatabase>();
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;

    options.JsonSerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
});
builder.Services.AddHttpContextAccessor();

// Casbin permissions

var casbinDbContext = new CasbinDbContext<int>(
    new DbContextOptionsBuilder<CasbinDbContext<int>>()
        .UseNpgsql(builder.Configuration.GetConnectionString("Guard"))
        .Options
);
var casbinEfcore = new EFCoreAdapter<int>(casbinDbContext);
casbinDbContext.Database.EnsureCreated();
var casbinEncofcer = new Enforcer("Casbin.conf", casbinEfcore);
casbinEncofcer.LoadPolicy();

builder.Services.AddSingleton<IEnforcer>(casbinEncofcer);
builder.Services.AddSingleton<IAuthorizationHandler, CasbinAuthorizationHandler>();

// Other pipelines

builder.Services.AddCors();
builder.Services.AddAuthorization();
builder.Services.AddAuthentication("Bearer").AddJwtBearer(options =>
{
    var publicKey = File.ReadAllText(builder.Configuration["Jwt:PublicKeyPath"]!);
    var rsa = RSA.Create();
    rsa.ImportFromPem(publicKey);
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = "solar-network",
        IssuerSigningKey = new RsaSecurityKey(rsa)
    };
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Version = "v1",
        Title = "Solar Network API",
        Description = "An open-source social network",
        TermsOfService = new Uri("https://solsynth.dev/terms"),
        License = new OpenApiLicense
        {
            Name = "APGLv3",
            Url = new Uri("https://www.gnu.org/licenses/agpl-3.0.html")
        }
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Please enter a valid token",
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        BearerFormat = "JWT",
        Scheme = "Bearer"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            []
        }
    });
});
builder.Services.AddOpenApi();

builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<AuthService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment()) app.MapOpenApi();

app.UseSwagger();
app.UseSwaggerUI();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.All
});

app.UseCors(opts =>
    opts.SetIsOriginAllowed(_ => true)
        .AllowCredentials()
        .AllowAnyHeader()
        .AllowAnyMethod());

app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers();

app.Run();