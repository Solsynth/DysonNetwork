using System.Globalization;
using DysonNetwork.Sphere.Account;
using DysonNetwork.Sphere.Activity;
using DysonNetwork.Sphere.Auth;
using DysonNetwork.Sphere.Auth.OpenId;
using DysonNetwork.Sphere.Chat;
using DysonNetwork.Sphere.Chat.Realtime;
using DysonNetwork.Sphere.Connection;
using DysonNetwork.Sphere.Connection.Handlers;
using DysonNetwork.Sphere.Email;
using DysonNetwork.Sphere.Localization;
using DysonNetwork.Sphere.Permission;
using DysonNetwork.Sphere.Post;
using DysonNetwork.Sphere.Publisher;
using DysonNetwork.Sphere.Realm;
using DysonNetwork.Sphere.Sticker;
using DysonNetwork.Sphere.Storage;
using DysonNetwork.Sphere.Storage.Handlers;
using DysonNetwork.Sphere.Wallet;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using StackExchange.Redis;
using System.Text.Json;
using System.Threading.RateLimiting;
using DysonNetwork.Sphere.Connection.WebReader;
using DysonNetwork.Sphere.Wallet.PaymentHandlers;
using tusdotnet.Stores;

namespace DysonNetwork.Sphere.Startup;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddLocalization(options => options.ResourcesPath = "Resources");

        services.AddDbContext<AppDatabase>();
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var connection = configuration.GetConnectionString("FastRetrieve")!;
            return ConnectionMultiplexer.Connect(connection);
        });
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddHttpContextAccessor();
        services.AddSingleton<ICacheService, CacheServiceRedis>();

        services.AddHttpClient();

        // Register OIDC services
        services.AddScoped<OidcService, GoogleOidcService>();
        services.AddScoped<OidcService, AppleOidcService>();
        services.AddScoped<OidcService, GitHubOidcService>();
        services.AddScoped<OidcService, MicrosoftOidcService>();
        services.AddScoped<OidcService, DiscordOidcService>();
        services.AddScoped<OidcService, AfdianOidcService>();
        services.AddScoped<GoogleOidcService>();
        services.AddScoped<AppleOidcService>();
        services.AddScoped<GitHubOidcService>();
        services.AddScoped<MicrosoftOidcService>();
        services.AddScoped<DiscordOidcService>();
        services.AddScoped<AfdianOidcService>();

        services.AddControllers().AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;

            options.JsonSerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
        }).AddDataAnnotationsLocalization(options =>
        {
            options.DataAnnotationLocalizerProvider = (type, factory) =>
                factory.Create(typeof(SharedResource));
        });
        services.AddRazorPages();

        services.Configure<RequestLocalizationOptions>(options =>
        {
            var supportedCultures = new[]
            {
                new CultureInfo("en-US"),
                new CultureInfo("zh-Hans"),
            };

            options.SupportedCultures = supportedCultures;
            options.SupportedUICultures = supportedCultures;
        });

        return services;
    }

    public static IServiceCollection AddAppRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(o => o.AddFixedWindowLimiter(policyName: "fixed", opts =>
        {
            opts.Window = TimeSpan.FromMinutes(1);
            opts.PermitLimit = 120;
            opts.QueueLimit = 2;
            opts.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        }));

        return services;
    }

    public static IServiceCollection AddAppAuthentication(this IServiceCollection services)
    {
        services.AddCors();
        services.AddAuthorization();
        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = AuthConstants.SchemeName;
                options.DefaultChallengeScheme = AuthConstants.SchemeName;
            })
            .AddScheme<DysonTokenAuthOptions, DysonTokenAuthHandler>(AuthConstants.SchemeName, _ => { });

        return services;
    }

    public static IServiceCollection AddAppSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
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
        services.AddOpenApi();

        return services;
    }

    public static IServiceCollection AddAppFileStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var tusStorePath = configuration.GetSection("Tus").GetValue<string>("StorePath")!;
        Directory.CreateDirectory(tusStorePath);
        var tusDiskStore = new TusDiskStore(tusStorePath);

        services.AddSingleton(tusDiskStore);

        return services;
    }

    public static IServiceCollection AddAppFlushHandlers(this IServiceCollection services)
    {
        services.AddSingleton<FlushBufferService>();
        services.AddScoped<ActionLogFlushHandler>();
        services.AddScoped<MessageReadReceiptFlushHandler>();
        services.AddScoped<LastActiveFlushHandler>();
        services.AddScoped<PostViewFlushHandler>();

        // The handlers for websocket
        services.AddScoped<IWebSocketPacketHandler, MessageReadHandler>();
        services.AddScoped<IWebSocketPacketHandler, MessageTypingHandler>();

        return services;
    }

    public static IServiceCollection AddAppBusinessServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<CompactTokenService>();
        services.AddScoped<RazorViewRenderer>();
        services.Configure<GeoIpOptions>(configuration.GetSection("GeoIP"));
        services.AddScoped<GeoIpService>();
        services.AddScoped<WebSocketService>();
        services.AddScoped<EmailService>();
        services.AddScoped<PermissionService>();
        services.AddScoped<ActionLogService>();
        services.AddScoped<AccountService>();
        services.AddScoped<AccountEventService>();
        services.AddScoped<ActionLogService>();
        services.AddScoped<RelationshipService>();
        services.AddScoped<MagicSpellService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<AuthService>();
        services.AddScoped<AccountUsernameService>();
        services.AddScoped<FileService>();
        services.AddScoped<FileReferenceService>();
        services.AddScoped<FileReferenceMigrationService>();
        services.AddScoped<PublisherService>();
        services.AddScoped<PublisherSubscriptionService>();
        services.AddScoped<ActivityService>();
        services.AddScoped<PostService>();
        services.AddScoped<RealmService>();
        services.AddScoped<ChatRoomService>();
        services.AddScoped<ChatService>();
        services.AddScoped<StickerService>();
        services.AddScoped<WalletService>();
        services.AddScoped<SubscriptionService>();
        services.AddScoped<PaymentService>();
        services.AddScoped<IRealtimeService, LivekitRealtimeService>();
        services.AddScoped<WebReaderService>();
        services.AddScoped<AfdianPaymentHandler>();

        return services;
    }
}
