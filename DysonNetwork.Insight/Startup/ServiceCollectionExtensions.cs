using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Insight.Reader;
using DysonNetwork.Insight.Thought;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Registry;
using Microsoft.SemanticKernel;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;

namespace DysonNetwork.Insight.Startup;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddAppServices()
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

        public IServiceCollection AddAppAuthentication()
        {
            services.AddAuthorization();
            return services;
        }

        public IServiceCollection AddAppFlushHandlers()
        {
            services.AddSingleton<FlushBufferService>();

            return services;
        }

        public IServiceCollection AddAppBusinessServices()
        {
            return services;
        }

        public IServiceCollection AddThinkingServices(IConfiguration configuration)
        {
            services.AddSingleton<ThoughtProvider>();
            services.AddScoped<ThoughtService>();
            services.AddScoped<WebFeedService>();
            services.AddScoped<WebReaderService>();

            return services;
        }
    }
}
