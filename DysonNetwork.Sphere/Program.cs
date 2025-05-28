using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using DysonNetwork.Sphere;
using DysonNetwork.Sphere.Account;
using DysonNetwork.Sphere.Email;
using DysonNetwork.Sphere.Activity;
using DysonNetwork.Sphere.Auth;
using DysonNetwork.Sphere.Chat;
using DysonNetwork.Sphere.Chat.Realtime;
using DysonNetwork.Sphere.Connection;
using DysonNetwork.Sphere.Connection.Handlers;
using DysonNetwork.Sphere.Localization;
using DysonNetwork.Sphere.Permission;
using DysonNetwork.Sphere.Post;
using DysonNetwork.Sphere.Publisher;
using DysonNetwork.Sphere.Realm;
using DysonNetwork.Sphere.Sticker;
using DysonNetwork.Sphere.Storage;
using DysonNetwork.Sphere.Storage.Handlers;
using DysonNetwork.Sphere.Wallet;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using Prometheus;
using Prometheus.DotNetRuntime;
using Prometheus.SystemMetrics;
using Quartz;
using StackExchange.Redis;
using tusdotnet;
using tusdotnet.Models;
using tusdotnet.Models.Configuration;
using tusdotnet.Stores;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseContentRoot(Directory.GetCurrentDirectory());
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = long.MaxValue;
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
});

// Configure metrics

builder.Services.UseHttpClientMetrics();
builder.Services.AddHealthChecks();
builder.Services.AddSystemMetrics();
builder.Services.AddPrometheusEntityFrameworkMetrics();
builder.Services.AddPrometheusAspNetCoreMetrics();
builder.Services.AddPrometheusHttpClientMetrics();

// Add services to the container.

builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

builder.Services.AddDbContext<AppDatabase>();
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var connection = builder.Configuration.GetConnectionString("FastRetrieve")!;
    return ConnectionMultiplexer.Connect(connection);
});
builder.Services.AddSingleton<ICacheService, CacheServiceRedis>();

builder.Services.AddHttpClient();
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;

    options.JsonSerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
}).AddDataAnnotationsLocalization(options =>
{
    options.DataAnnotationLocalizerProvider = (type, factory) =>
        factory.Create(typeof(SharedResource));
});
builder.Services.AddRazorPages();

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[]
    {
        new CultureInfo("en-US"),
        new CultureInfo("zh-Hans"),
    };

    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
});

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
builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = AuthConstants.SchemeName;
        options.DefaultChallengeScheme = AuthConstants.SchemeName;
    })
    .AddScheme<DysonTokenAuthOptions, DysonTokenAuthHandler>(AuthConstants.SchemeName, _ => { });

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

var tusStorePath = builder.Configuration.GetSection("Tus").GetValue<string>("StorePath")!;
Directory.CreateDirectory(tusStorePath);
var tusDiskStore = new TusDiskStore(tusStorePath);

builder.Services.AddSingleton(tusDiskStore);

builder.Services.AddSingleton<FlushBufferService>();
builder.Services.AddScoped<ActionLogFlushHandler>();
builder.Services.AddScoped<MessageReadReceiptFlushHandler>();
builder.Services.AddScoped<ActionLogService>();

// The handlers for websocket
builder.Services.AddScoped<IWebSocketPacketHandler, MessageReadHandler>();
builder.Services.AddScoped<IWebSocketPacketHandler, MessageTypingHandler>();

// Services
builder.Services.AddScoped<CompactTokenService>();
builder.Services.AddScoped<RazorViewRenderer>();
builder.Services.Configure<GeoIpOptions>(builder.Configuration.GetSection("GeoIP"));
builder.Services.AddScoped<GeoIpService>();
builder.Services.AddScoped<WebSocketService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<PermissionService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<AccountEventService>();
builder.Services.AddScoped<ActionLogService>();
builder.Services.AddScoped<RelationshipService>();
builder.Services.AddScoped<MagicSpellService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<FileService>();
builder.Services.AddScoped<PublisherService>();
builder.Services.AddScoped<PublisherSubscriptionService>();
builder.Services.AddScoped<ActivityService>();
builder.Services.AddScoped<ActivityReaderService>();
builder.Services.AddScoped<PostService>();
builder.Services.AddScoped<RealmService>();
builder.Services.AddScoped<ChatRoomService>();
builder.Services.AddScoped<ChatService>();
builder.Services.AddScoped<StickerService>();
builder.Services.AddScoped<WalletService>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<IRealtimeService, LivekitRealtimeService>();

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

    var actionLogFlushJob = new JobKey("ActionLogFlush");
    q.AddJob<ActionLogFlushJob>(opts => opts.WithIdentity(actionLogFlushJob));
    q.AddTrigger(opts => opts
        .ForJob(actionLogFlushJob)
        .WithIdentity("ActionLogFlushTrigger")
        .WithSimpleSchedule(o => o
            .WithIntervalInMinutes(5)
            .RepeatForever())
    );

    var readReceiptFlushJob = new JobKey("ReadReceiptFlush");
    q.AddJob<ReadReceiptFlushJob>(opts => opts.WithIdentity(readReceiptFlushJob));
    q.AddTrigger(opts => opts
        .ForJob(readReceiptFlushJob)
        .WithIdentity("ReadReceiptFlushTrigger")
        .WithSimpleSchedule(o => o
            .WithIntervalInSeconds(60)
            .RepeatForever())
    );
});
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);


var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
    await db.Database.MigrateAsync();
}

app.MapMetrics();
app.MapOpenApi();

app.UseSwagger();
app.UseSwaggerUI();

app.UseRequestLocalization();

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
app.UseMiddleware<PermissionMiddleware>();

app.MapControllers().RequireRateLimiting("fixed");
app.MapStaticAssets().RequireRateLimiting("fixed");
app.MapRazorPages().RequireRateLimiting("fixed");

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
            if (httpContext.Items["CurrentUser"] is not Account user)
            {
                eventContext.FailRequest(HttpStatusCode.Unauthorized);
                return;
            }

            if (!user.IsSuperuser)
            {
                using var scope = httpContext.RequestServices.CreateScope();
                var pm = scope.ServiceProvider.GetRequiredService<PermissionService>();
                var allowed = await pm.HasPermissionAsync($"user:{user.Id}", "global", "files.create");
                if (!allowed)
                    eventContext.FailRequest(HttpStatusCode.Forbidden);
            }
        },
        OnFileCompleteAsync = async eventContext =>
        {
            using var scope = eventContext.HttpContext.RequestServices.CreateScope();
            var services = scope.ServiceProvider;

            var httpContext = eventContext.HttpContext;
            if (httpContext.Items["CurrentUser"] is not Account user) return;

            var file = await eventContext.GetFileAsync();
            var metadata = await file.GetMetadataAsync(eventContext.CancellationToken);
            var fileName = metadata.TryGetValue("filename", out var fn)
                ? fn.GetString(Encoding.UTF8)
                : "uploaded_file";
            var contentType = metadata.TryGetValue("content-type", out var ct) ? ct.GetString(Encoding.UTF8) : null;

            var fileStream = await file.GetContentAsync(eventContext.CancellationToken);

            var fileService = services.GetRequiredService<FileService>();
            var info = await fileService.ProcessNewFileAsync(user, file.Id, fileStream, fileName, contentType);

            using var finalScope = eventContext.HttpContext.RequestServices.CreateScope();
            var jsonOptions = finalScope.ServiceProvider.GetRequiredService<IOptions<JsonOptions>>().Value
                .JsonSerializerOptions;
            var infoJson = JsonSerializer.Serialize(info, jsonOptions);
            eventContext.HttpContext.Response.Headers.Append("X-FileInfo", infoJson);

            // Dispose the stream after all processing is complete
            await fileStream.DisposeAsync();
        }
    }
}));

app.Run();
