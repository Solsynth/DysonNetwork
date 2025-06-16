using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using DysonNetwork.Sphere;
using DysonNetwork.Sphere.Account;
using DysonNetwork.Sphere.Email;
using DysonNetwork.Sphere.Activity;
using DysonNetwork.Sphere.Auth;
using DysonNetwork.Sphere.Auth.OpenId;
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
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Prometheus;
using Prometheus.SystemMetrics;
using Quartz;
using StackExchange.Redis;
using tusdotnet;
using tusdotnet.Stores;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseContentRoot(Directory.GetCurrentDirectory());
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 50 * 1024 * 1024;
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
});

// Configure metrics

// Prometheus
builder.Services.UseHttpClientMetrics();
builder.Services.AddHealthChecks();
builder.Services.AddSystemMetrics();
builder.Services.AddPrometheusEntityFrameworkMetrics();
builder.Services.AddPrometheusAspNetCoreMetrics();
builder.Services.AddPrometheusHttpClientMetrics();

// OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddOtlpExporter();
    });

// Add services to the container.

builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

builder.Services.AddDbContext<AppDatabase>();
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var connection = builder.Configuration.GetConnectionString("FastRetrieve")!;
    return ConnectionMultiplexer.Connect(connection);
});
builder.Services.AddSingleton<IClock>(SystemClock.Instance);
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ICacheService, CacheServiceRedis>();

builder.Services.AddHttpClient();

// Register OIDC services
builder.Services.AddScoped<OidcService, GoogleOidcService>();
builder.Services.AddScoped<OidcService, AppleOidcService>();
builder.Services.AddScoped<OidcService, GitHubOidcService>();
builder.Services.AddScoped<OidcService, MicrosoftOidcService>();
builder.Services.AddScoped<OidcService, DiscordOidcService>();
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

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = !builder.Configuration["BaseUrl"]!.StartsWith("https");
    options.Cookie.IsEssential = true;
});
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
builder.Services.AddScoped<LastActiveFlushHandler>();

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
builder.Services.AddScoped<ActionLogService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<AccountEventService>();
builder.Services.AddScoped<ActionLogService>();
builder.Services.AddScoped<RelationshipService>();
builder.Services.AddScoped<MagicSpellService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AppleOidcService>();
builder.Services.AddScoped<GoogleOidcService>();
builder.Services.AddScoped<AccountUsernameService>();
builder.Services.AddScoped<FileService>();
builder.Services.AddScoped<FileReferenceService>();
builder.Services.AddScoped<FileReferenceMigrationService>();
builder.Services.AddScoped<PublisherService>();
builder.Services.AddScoped<PublisherSubscriptionService>();
builder.Services.AddScoped<ActivityService>();
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

    var lastActiveFlushJob = new JobKey("LastActiveFlush");
    q.AddJob<LastActiveFlushJob>(opts => opts.WithIdentity(lastActiveFlushJob));
    q.AddTrigger(opts => opts
        .ForJob(lastActiveFlushJob)
        .WithIdentity("LastActiveFlushTrigger")
        .WithSimpleSchedule(o => o
            .WithIntervalInMinutes(5)
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

// Configure forwarded headers with known proxies from configuration
{
    var knownProxiesSection = builder.Configuration.GetSection("KnownProxies");
    var forwardedHeadersOptions = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.All };

    if (knownProxiesSection.Exists())
    {
        var proxyAddresses = knownProxiesSection.Get<string[]>();
        if (proxyAddresses != null)
            foreach (var proxy in proxyAddresses)
                if (IPAddress.TryParse(proxy, out var ipAddress))
                    forwardedHeadersOptions.KnownProxies.Add(ipAddress);
    }
    else
    {
        forwardedHeadersOptions.KnownProxies.Add(IPAddress.Any);
        forwardedHeadersOptions.KnownProxies.Add(IPAddress.IPv6Any);
    }

    app.UseForwardedHeaders(forwardedHeadersOptions);
}

app.UseSession();
app.UseCors(opts =>
    opts.SetIsOriginAllowed(_ => true)
        .WithExposedHeaders("*")
        .WithHeaders()
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

app.MapTus("/files/tus", _ => Task.FromResult(TusService.BuildConfiguration(tusDiskStore)));

app.Run();