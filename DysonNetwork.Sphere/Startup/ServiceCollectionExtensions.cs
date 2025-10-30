using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.GeoIp;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Sphere.Autocompletion;
using DysonNetwork.Sphere.Chat;
using DysonNetwork.Sphere.Chat.Realtime;
using DysonNetwork.Sphere.Discovery;
using DysonNetwork.Sphere.Localization;
using DysonNetwork.Sphere.Poll;
using DysonNetwork.Sphere.Post;
using DysonNetwork.Sphere.Publisher;
using DysonNetwork.Sphere.Sticker;
using DysonNetwork.Sphere.Timeline;
using DysonNetwork.Sphere.Translation;
using DysonNetwork.Sphere.WebReader;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;

namespace DysonNetwork.Sphere.Startup;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppServices(this IServiceCollection services)
    {
        services.AddLocalization(options => options.ResourcesPath = "Resources");

        services.AddDbContext<AppDatabase>();
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddHttpContextAccessor();
        services.AddSingleton<ICacheService, CacheServiceRedis>();

        services.AddHttpClient();

        services
            .AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.NumberHandling =
                    JsonNumberHandling.AllowNamedFloatingPointLiterals;
                options.JsonSerializerOptions.PropertyNamingPolicy =
                    JsonNamingPolicy.SnakeCaseLower;

                options.JsonSerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
            })
            .AddDataAnnotationsLocalization(options =>
            {
                options.DataAnnotationLocalizerProvider = (type, factory) =>
                    factory.Create(typeof(SharedResource));
            })
            .ConfigureApplicationPartManager(opts =>
            {
                var mockingPart = opts.ApplicationParts.FirstOrDefault(a =>
                    a.Name == "DysonNetwork.Pass"
                );
                if (mockingPart != null)
                    opts.ApplicationParts.Remove(mockingPart);
            });
        services.AddRazorPages();

        services.AddGrpc(options =>
        {
            options.EnableDetailedErrors = true;
        });
        services.AddGrpcReflection();

        services.Configure<RequestLocalizationOptions>(options =>
        {
            var supportedCultures = new[] { new CultureInfo("en-US"), new CultureInfo("zh-Hans") };

            options.SupportedCultures = supportedCultures;
            options.SupportedUICultures = supportedCultures;
        });

        services.AddHostedService<BroadcastEventHandler>();

        return services;
    }

    public static IServiceCollection AddAppAuthentication(this IServiceCollection services)
    {
        services.AddAuthorization();
        return services;
    }

    public static IServiceCollection AddAppFlushHandlers(this IServiceCollection services)
    {
        services.AddSingleton<FlushBufferService>();
        services.AddScoped<PostViewFlushHandler>();

        return services;
    }

    public static IServiceCollection AddAppBusinessServices(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.Configure<GeoIpOptions>(configuration.GetSection("GeoIP"));
        services.AddScoped<GeoIpService>();
        services.AddScoped<PublisherService>();
        services.AddScoped<PublisherSubscriptionService>();
        services.AddScoped<TimelineService>();
        services.AddScoped<PostService>();
        services.AddScoped<ChatRoomService>();
        services.AddScoped<ChatService>();
        services.AddScoped<StickerService>();
        services.AddScoped<IRealtimeService, LiveKitRealtimeService>();
        services.AddScoped<WebReaderService>();
        services.AddScoped<WebFeedService>();
        services.AddScoped<DiscoveryService>();
        services.AddScoped<PollService>();
        services.AddScoped<RemoteAccountService>();
        services.AddScoped<RemoteRealmService>();
        services.AddScoped<AutocompletionService>();

        var translationProvider = configuration["Translation:Provider"]?.ToLower();
        switch (translationProvider)
        {
            case "tencent":
                services.AddScoped<ITranslationProvider, TencentTranslation>();
                break;
        }

        return services;
    }
}
