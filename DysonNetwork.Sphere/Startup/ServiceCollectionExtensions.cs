using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Geometry;
using DysonNetwork.Sphere.ActivityPub;
using DysonNetwork.Sphere.Autocompletion;
using DysonNetwork.Sphere.Localization;
using DysonNetwork.Sphere.Poll;
using DysonNetwork.Sphere.Post;
using DysonNetwork.Sphere.Publisher;
using DysonNetwork.Sphere.Sticker;
using DysonNetwork.Sphere.Timeline;
using DysonNetwork.Sphere.Translation;

using NodaTime;
using NodaTime.Serialization.SystemTextJson;

namespace DysonNetwork.Sphere.Startup;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddAppServices()
        {
            services.AddLocalization(options => options.ResourcesPath = "Resources");

            services.AddDbContext<AppDatabase>();
            services.AddHttpContextAccessor();

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
            services.AddHostedService<ActivityPubDeliveryWorker>();

            return services;
        }

        public IServiceCollection AddAppAuthentication()
        {
            services.AddAuthorization();
            return services;
        }

        public IServiceCollection AddAppFlushHandlers()
        {
            services.AddSingleton<FlushBufferService>();
            services.AddScoped<PostViewFlushHandler>();

            return services;
        }

        public IServiceCollection AddAppBusinessServices(IConfiguration configuration
        )
        {
            services.Configure<GeoOptions>(configuration.GetSection("GeoIP"));
            services.Configure<ActivityPubDeliveryOptions>(configuration.GetSection("ActivityPubDelivery"));
            services.AddScoped<GeoService>();
            services.AddScoped<PublisherService>();
            services.AddScoped<PublisherSubscriptionService>();
            services.AddScoped<TimelineService>();
            services.AddScoped<PostService>();
            services.AddScoped<PollService>();
            services.AddScoped<StickerService>();
            services.AddScoped<AutocompletionService>();
            services.AddScoped<ActivityPubKeyService>();
            services.AddScoped<ActivityPubSignatureService>();
            services.AddScoped<ActivityPubActivityHandler>();
            services.AddScoped<ActivityPubDeliveryService>();
            services.AddScoped<ActivityPubDiscoveryService>();
            services.AddScoped<ActivityPubObjectFactory>();
            services.AddSingleton<ActivityPubQueueService>();

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
}
