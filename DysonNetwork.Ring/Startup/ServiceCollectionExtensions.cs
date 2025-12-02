using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using DysonNetwork.Ring.Connection;
using DysonNetwork.Ring.Email;
using DysonNetwork.Ring.Notification;
using DysonNetwork.Ring.Services;
using DysonNetwork.Shared.Cache;
using Microsoft.AspNetCore.RateLimiting;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;

namespace DysonNetwork.Ring.Startup;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppServices(this IServiceCollection services, IConfiguration configuration)
    {
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

        // Register gRPC services
        services.AddScoped<RingServiceGrpc>();

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
        services.AddSingleton<WebSocketService>();
        services.AddScoped<EmailService>();
        services.AddScoped<PushService>();
        
        // Register QueueService as a singleton since it's thread-safe
        services.AddSingleton<QueueService>();
        
        // Register the background service
        services.AddHostedService<QueueBackgroundService>();

        return services;
    }
}
