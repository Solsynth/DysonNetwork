using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using DysonNetwork.Shared.Cache;
using Microsoft.AspNetCore.RateLimiting;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using tusdotnet.Stores;

namespace DysonNetwork.Drive.Startup;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDatabase>(); // Assuming you'll have an AppDatabase
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddHttpContextAccessor();
        services.AddSingleton<ICacheService, CacheServiceRedis>(); // Uncomment if you have CacheServiceRedis

        services.AddHttpClient();

        // Register gRPC services
        services.AddGrpc(options =>
        {
            options.EnableDetailedErrors = true; // Will be adjusted in Program.cs
            options.MaxReceiveMessageSize = 16 * 1024 * 1024; // 16MB
            options.MaxSendMessageSize = 16 * 1024 * 1024; // 16MB
        });

        // Register gRPC reflection for service discovery
        services.AddGrpc();

        services.AddControllers().AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals;
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;

            options.JsonSerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
        });

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

    public static IServiceCollection AddAppBusinessServices(this IServiceCollection services)
    {
        services.AddScoped<Storage.FileService>();
        services.AddScoped<Storage.FileReferenceService>();
        services.AddScoped<Billing.UsageService>();
        services.AddScoped<Billing.QuotaService>();

        services.AddHostedService<BroadcastEventHandler>();
        
        return services;
    }
}