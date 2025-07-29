using System.Globalization;
using DysonNetwork.Sphere.Activity;
using DysonNetwork.Sphere.Chat;
using DysonNetwork.Sphere.Chat.Realtime;
using DysonNetwork.Sphere.Localization;
using DysonNetwork.Sphere.Post;
using DysonNetwork.Sphere.Publisher;
using DysonNetwork.Sphere.Realm;
using DysonNetwork.Sphere.Sticker;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using StackExchange.Redis;
using System.Text.Json;
using System.Threading.RateLimiting;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.GeoIp;
using DysonNetwork.Sphere.WebReader;
using DysonNetwork.Sphere.Developer;
using DysonNetwork.Sphere.Discovery;

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
        
        services.AddGrpc(options =>
        {
            options.EnableDetailedErrors = true;
        });

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

    public static IServiceCollection AddAppFlushHandlers(this IServiceCollection services)
    {
        services.AddSingleton<FlushBufferService>();
        services.AddScoped<PostViewFlushHandler>();

        return services;
    }

    public static IServiceCollection AddAppBusinessServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<GeoIpOptions>(configuration.GetSection("GeoIP"));
        services.AddScoped<GeoIpService>();
        services.AddScoped<PublisherService>();
        services.AddScoped<PublisherSubscriptionService>();
        services.AddScoped<ActivityService>();
        services.AddScoped<PostService>();
        services.AddScoped<RealmService>();
        services.AddScoped<ChatRoomService>();
        services.AddScoped<ChatService>();
        services.AddScoped<StickerService>();
        services.AddScoped<IRealtimeService, LiveKitRealtimeService>();
        services.AddScoped<WebReaderService>();
        services.AddScoped<WebFeedService>();
        services.AddScoped<DiscoveryService>();
        services.AddScoped<CustomAppService>();
        
        return services;
    }
}