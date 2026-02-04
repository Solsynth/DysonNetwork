using System.Globalization;
using Microsoft.AspNetCore.RateLimiting;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Geometry;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Wallet.Localization;
using DysonNetwork.Wallet.Payment;
using DysonNetwork.Wallet.Payment.PaymentHandlers;

namespace DysonNetwork.Wallet.Startup;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddLocalization(options => options.ResourcesPath = "Resources");

        services.AddDbContext<AppDatabase>();
        services.AddHttpContextAccessor();

        services.AddHttpClient();

        // Register gRPC services
        services.AddGrpc(options =>
        {
            options.EnableDetailedErrors = true; // Will be adjusted in Program.cs
            options.MaxReceiveMessageSize = 16 * 1024 * 1024; // 16MB
            options.MaxSendMessageSize = 16 * 1024 * 1024; // 16MB
        });
        services.AddGrpcReflection();

        services.AddRingService();

        services.AddControllers().AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals;
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;

            options.JsonSerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
        }).AddDataAnnotationsLocalization(options =>
        {
            options.DataAnnotationLocalizerProvider = (type, factory) =>
                factory.Create(typeof(NotificationResource));
        });
        services.AddRazorPages();

        // Configure rate limiting
        services.AddRateLimiter(options =>
        {
            options.AddFixedWindowLimiter("captcha", opt =>
            {
                opt.Window = TimeSpan.FromMinutes(1);
                opt.PermitLimit = 5; // 5 attempts per minute
                opt.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
                opt.QueueLimit = 2;
            });
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

    public static IServiceCollection AddAppFlushHandlers(this IServiceCollection services)
    {
        services.AddSingleton<FlushBufferService>();

        return services;
    }

    public static IServiceCollection AddAppBusinessServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<DysonNetwork.Shared.Localization.ILocalizationService, DysonNetwork.Shared.Localization.JsonLocalizationService>(sp =>
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var resourceNamespace = "DysonNetwork.Wallet.Resources.Locales";
            return new DysonNetwork.Shared.Localization.JsonLocalizationService(assembly, resourceNamespace);
        });
        
        services.Configure<GeoOptions>(configuration.GetSection("GeoIP"));
        services.AddScoped<GeoService>();

        // Register Wallet services
        services.AddScoped<WalletService>();
        services.AddScoped<PaymentService>();
        services.AddScoped<SubscriptionService>();
        services.AddScoped<AfdianPaymentHandler>();

        services.AddHostedService<BroadcastEventHandler>();

        return services;
    }
}
