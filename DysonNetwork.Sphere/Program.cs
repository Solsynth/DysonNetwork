using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Casbin;
using Casbin.Persist.Adapter.EFCore;
using DysonNetwork.Sphere;
using DysonNetwork.Sphere.Account;
using DysonNetwork.Sphere.Activity;
using DysonNetwork.Sphere.Auth;
using DysonNetwork.Sphere.Connection;
using DysonNetwork.Sphere.Permission;
using DysonNetwork.Sphere.Post;
using DysonNetwork.Sphere.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using Quartz;
using tusdotnet;
using tusdotnet.Models;
using tusdotnet.Models.Configuration;
using File = System.IO.File;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseContentRoot(Directory.GetCurrentDirectory());
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = long.MaxValue);

// Add services to the container.

builder.Services.AddDbContext<AppDatabase>();

builder.Services.AddHttpClient();
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;

    options.JsonSerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
});
builder.Services.AddRazorPages();

// Other pipelines

builder.Services.AddRateLimiter(o => o.AddFixedWindowLimiter(policyName: "fixed", opts =>
{
    opts.Window = TimeSpan.FromMinutes(1);
    opts.PermitLimit = 120;
    opts.QueueLimit = 2;
    opts.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
}));
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

builder.Services.AddScoped<WebSocketService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<PermissionService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<RelationshipService>();
builder.Services.AddScoped<MagicSpellService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<FileService>();
builder.Services.AddScoped<PublisherService>();
builder.Services.AddScoped<ActivityService>();
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
    await db.Database.MigrateAsync();
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

app.UseWebSockets();
app.UseRateLimiter();
app.UseHttpsRedirection();
app.UseAuthorization();
app.UseMiddleware<UserInfoMiddleware>();
app.UseMiddleware<PermissionMiddleware>();

app.MapControllers().RequireRateLimiting("fixed");
app.MapStaticAssets().RequireRateLimiting("fixed");
app.MapRazorPages().RequireRateLimiting("fixed");

var tusDiskStore = new tusdotnet.Stores.TusDiskStore(
    builder.Configuration.GetSection("Tus").GetValue<string>("StorePath")!
);
app.MapTus("/files/tus", _ => Task.FromResult<DefaultTusConfiguration>(new()
{
    Store = tusDiskStore,
    Events = new Events
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
            var user = httpContext.Items["CurrentUser"] as Account;
            if (user is null)
            {
                eventContext.FailRequest(HttpStatusCode.Unauthorized);
                return;
            }

            var userId = httpContext.User.FindFirst("user_id")?.Value;
            if (userId == null) return;

            var pm = httpContext.RequestServices.GetRequiredService<PermissionService>();
            var allowed = await pm.HasPermissionAsync($"user:{userId}", "global", "files.create");
            if (!allowed)
            {
                eventContext.FailRequest(HttpStatusCode.Forbidden);
            }
        },
        OnFileCompleteAsync = async eventContext =>
        {
            var httpContext = eventContext.HttpContext;
            if (httpContext.Items["CurrentUser"] is not Account user) return;

            var file = await eventContext.GetFileAsync();
            var metadata = await file.GetMetadataAsync(eventContext.CancellationToken);
            var fileName = metadata.TryGetValue("filename", out var fn) ? fn.GetString(Encoding.UTF8) : "uploaded_file";
            var contentType = metadata.TryGetValue("content-type", out var ct) ? ct.GetString(Encoding.UTF8) : null;
            var fileStream = await file.GetContentAsync(eventContext.CancellationToken);

            var fileService = eventContext.HttpContext.RequestServices.GetRequiredService<FileService>();

            var info = await fileService.ProcessNewFileAsync(user, file.Id, fileStream, fileName, contentType);

            var jsonOptions = httpContext.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value
                .JsonSerializerOptions;
            var infoJson = JsonSerializer.Serialize(info, jsonOptions);
            eventContext.HttpContext.Response.Headers.Append("X-FileInfo", infoJson);
        },
    }
}));

app.Run();