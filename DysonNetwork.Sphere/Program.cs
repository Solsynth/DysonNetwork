using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Casbin;
using Casbin.Persist.Adapter.EFCore;
using DysonNetwork.Sphere;
using DysonNetwork.Sphere.Account;
using DysonNetwork.Sphere.Auth;
using DysonNetwork.Sphere.Post;
using DysonNetwork.Sphere.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using Quartz;
using tusdotnet;
using tusdotnet.Models;
using File = System.IO.File;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseContentRoot(Directory.GetCurrentDirectory());
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = long.MaxValue);

// Add services to the container.

builder.Services.AddDbContext<AppDatabase>();
builder.Services.AddMemoryCache();

builder.Services.AddHttpClient();
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;

    options.JsonSerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
});
builder.Services.AddRazorPages();

// Casbin permissions

var casbinDbContext = new CasbinDbContext<int>(
    new DbContextOptionsBuilder<CasbinDbContext<int>>()
        .UseNpgsql(builder.Configuration.GetConnectionString("Guard"))
        .Options
);
var casbinEfcore = new EFCoreAdapter<int>(casbinDbContext);
casbinDbContext.Database.EnsureCreated();
var casbinEncofcer = new Enforcer("Casbin.conf", casbinEfcore);
casbinEncofcer.EnableCache(true);
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
builder.Services.AddScoped<PublisherService>();
builder.Services.AddScoped<PostService>();

// Timed task

builder.Services.AddQuartz(q =>
{
    var appDatabaseRecyclingJob = new JobKey("AppDatabaseRecycling");
    q.AddJob<AppDatabaseRecyclingJob>(opts => opts.WithIdentity(appDatabaseRecyclingJob));
    q.AddTrigger(opts => opts
        .ForJob(appDatabaseRecyclingJob)
        .WithIdentity("AppDatabaseRecyclingTrigger")
        .WithCronSchedule("0 0 0 * * ?"));

    var cloudFilesRecyclingJob = new JobKey("CloudFilesUnusedRecycling");
    q.AddJob<CloudFileUnusedRecyclingJob>(opts => opts.WithIdentity(cloudFilesRecyclingJob));
    q.AddTrigger(opts => opts
        .ForJob(cloudFilesRecyclingJob)
        .WithIdentity("CloudFilesUnusedRecyclingTrigger")
        .WithSimpleSchedule(o => o.WithIntervalInHours(1).RepeatForever())
    );
});
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);


var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
    db.Database.Migrate();
}

app.MapOpenApi();

app.UseSwagger();
app.UseSwaggerUI();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.All
});

app.UseCors(opts =>
    opts.SetIsOriginAllowed(_ => true)
        .WithExposedHeaders("X-Total")
        .WithHeaders("X-Total")
        .AllowCredentials()
        .AllowAnyHeader()
        .AllowAnyMethod()
);

app.UseHttpsRedirection();
app.UseAuthorization();
app.UseMiddleware<UserInfoMiddleware>();

app.MapControllers();
app.MapStaticAssets();
app.MapRazorPages();

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

            var jsonOptions = httpContext.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value
                .JsonSerializerOptions;
            var infoJson = JsonSerializer.Serialize(info, jsonOptions);
            eventContext.HttpContext.Response.Headers.Append("X-FileInfo", infoJson);

#pragma warning disable CS4014
            Task.Run(async () =>
            {
                using var scope = eventContext.HttpContext.RequestServices
                    .GetRequiredService<IServiceScopeFactory>()
                    .CreateScope();
                // Keep the service didn't be disposed
                var fs = scope.ServiceProvider.GetRequiredService<FileService>();
                // Keep the file stream opened
                var fileData = await tusDiskStore.GetFileAsync(file.Id, CancellationToken.None);
                var newStream = await fileData.GetContentAsync(CancellationToken.None);
                await fs.UploadFileToRemoteAsync(info, newStream, null);
                await tusDiskStore.DeleteFileAsync(file.Id, CancellationToken.None);
            });
#pragma warning restore CS4014
        },
    }
}));

app.Run();