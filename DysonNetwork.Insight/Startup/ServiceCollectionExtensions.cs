using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Insight.Thinking;
using DysonNetwork.Shared.Cache;
using Microsoft.SemanticKernel;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;

namespace DysonNetwork.Insight.Startup;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDatabase>();
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddHttpContextAccessor();
        services.AddSingleton<ICacheService, CacheServiceRedis>();

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

        // Register gRPC services

        // Register OIDC services
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

    public static IServiceCollection AddAppBusinessServices(this IServiceCollection services)
    {
        return services;
    }

    public static IServiceCollection AddThinkingServices(this IServiceCollection services, IConfiguration configuration)
    {
        var thinkingProvider = new ThinkingProvider(configuration);
        services.AddSingleton(thinkingProvider);

        return services;
    }
}
