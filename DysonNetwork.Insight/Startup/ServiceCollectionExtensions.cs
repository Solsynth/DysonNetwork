using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Insight.Reader;
using DysonNetwork.Insight.Thought;
using DysonNetwork.Insight.MiChan;
using DysonNetwork.Insight.MiChan.Plugins;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Proto;
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
            // Shared kernel factory for AI service creation
            services.AddSingleton<KernelFactory>();
            
            services.AddSingleton<ThoughtProvider>();
            services.AddScoped<ThoughtService>();
            services.AddScoped<Reader.WebFeedService>();
            services.AddScoped<Reader.WebReaderService>();

            return services;
        }

        public IServiceCollection AddMiChanServices(IConfiguration configuration)
        {
            var miChanConfig = configuration.GetSection("MiChan").Get<MiChanConfig>() ?? new MiChanConfig();
            services.AddSingleton(miChanConfig);

            // Always register MiChan services for dependency injection (needed by ThoughtController)
            // Core services
            services.AddSingleton<KernelFactory>();
            services.AddSingleton<SolarNetworkApiClient>();
            services.AddSingleton<MiChanKernelProvider>();
            
            // Plugins
            services.AddSingleton<ChatPlugin>();
            services.AddSingleton<PostPlugin>();
            services.AddSingleton<NotificationPlugin>();
            services.AddSingleton<AccountPlugin>();
            
            // Memory and behavior services
            services.AddSingleton<EmbeddingService>();
            services.AddSingleton<MiChanMemoryService>();
            services.AddSingleton<PostAnalysisService>();
            services.AddSingleton<MiChanAutonomousBehavior>();
            
            // Only start the hosted service when enabled
            if (miChanConfig.Enabled)
                services.AddHostedService<MiChanService>();

            return services;
        }
    }
}
