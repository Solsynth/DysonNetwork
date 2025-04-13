using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Casbin;
using Casbin.Persist.Adapter.EFCore;
using DysonNetwork.Sphere;
using DysonNetwork.Sphere.Account;
using DysonNetwork.Sphere.Auth;
using DysonNetwork.Sphere.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using tusdotnet;
using tusdotnet.Models;
using File = System.IO.File;

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
builder.Services.AddScoped<FileService>();

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
        .AllowAnyMethod()
);

app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers();

var tusDiskStore = new tusdotnet.Stores.TusDiskStore(
    builder.Configuration.GetSection("Tus").GetValue<string>("StorePath")!
);
app.MapTus("/files/tus", (_) => Task.FromResult<DefaultTusConfiguration>(new()
{
    Store = tusDiskStore,
    Events = new()
    {
        OnAuthorizeAsync = async eventContext =>
        {
            if (eventContext.Intent == IntentType.DeleteFile)
            {
                eventContext.FailRequest(
                    HttpStatusCode.BadRequest,
                    "Deleting files from this endpoint was disabled, please refer to the Dyson Network File API."
                );
                return;
            }

            var httpContext = eventContext.HttpContext;
            var user = httpContext.User;
            if (!user.Identity?.IsAuthenticated ?? true)
            {
                eventContext.FailRequest(HttpStatusCode.Unauthorized);
                return;
            }

            var userId = httpContext.User.FindFirst("user_id")?.Value;
            if (userId == null) return;
            var isSuperuser = httpContext.User.FindFirst("is_superuser")?.Value == "1";
            if (isSuperuser) userId = "super:" + userId;

            var enforcer = httpContext.RequestServices.GetRequiredService<IEnforcer>();
            var allowed = await enforcer.EnforceAsync(userId, "global", "files", "create");
            if (!allowed)
            {
                eventContext.FailRequest(HttpStatusCode.Forbidden);
            }
        },
        OnFileCompleteAsync = async eventContext =>
        {
            var httpContext = eventContext.HttpContext;
            var user = httpContext.User;
            var userId = long.Parse(user.FindFirst("user_id")!.Value);

            var db = httpContext.RequestServices.GetRequiredService<AppDatabase>();
            var account = await db.Accounts.FindAsync(userId);
            if (account is null) return;

            var file = await eventContext.GetFileAsync();
            var metadata = await file.GetMetadataAsync(eventContext.CancellationToken);
            var fileName = metadata.TryGetValue("filename", out var fn) ? fn.GetString(Encoding.UTF8) : "uploaded_file";
            var contentType = metadata.TryGetValue("content-type", out var ct) ? ct.GetString(Encoding.UTF8) : null;
            var fileStream = await file.GetContentAsync(eventContext.CancellationToken);

            var fileService = eventContext.HttpContext.RequestServices.GetRequiredService<FileService>();

            var info = await fileService.AnalyzeFileAsync(account, file.Id, fileStream, fileName, contentType);
#pragma warning disable CS4014
            Task.Run(async () =>
            {
                await fileService.UploadFileToRemoteAsync(info, fileStream, null);
                await tusDiskStore.DeleteFileAsync(file.Id, eventContext.CancellationToken);
            });
#pragma warning restore CS4014
        },
        OnCreateCompleteAsync = eventContext =>
        {
            // var baseUrl = builder.Configuration.GetValue<string>("Storage:BaseUrl")!;
            // eventContext.SetUploadUrl(new Uri($"{baseUrl}/files/{eventContext.FileId}"));
            return Task.CompletedTask;
        }
    }
}));

app.Run();