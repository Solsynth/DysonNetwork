using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Insight.Reader;
using DysonNetwork.Insight.Services;
using DysonNetwork.Insight.Thought;
using DysonNetwork.Insight.MiChan;
using DysonNetwork.Insight.MiChan.Plugins;
using DysonNetwork.Insight.Thought.Memory;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Localization;
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
            // Register localization service
            services.AddSingleton<DysonNetwork.Shared.Localization.ILocalizationService, DysonNetwork.Shared.Localization.JsonLocalizationService>(sp =>
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceNamespace = "DysonNetwork.Insight.Resources.Locales";
                return new DysonNetwork.Shared.Localization.JsonLocalizationService(assembly, resourceNamespace);
            });

            // Register Ring service for push notifications
            services.AddRingService();

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
            
            // Register token counting service for accurate AI token counting
            services.AddSingleton<TokenCountingService>();

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
            services.AddSingleton<GeneralKernelProvider>();
            services.AddSingleton<IKernelProvider>(sp => sp.GetRequiredService<GeneralKernelProvider>());
            
            // Plugins
            services.AddSingleton<PostPlugin>();
            services.AddSingleton<AccountPlugin>();
            services.AddSingleton<MemoryPlugin>();
            services.AddScoped<ScheduledTaskPlugin>();
            services.AddScoped<ConversationPlugin>();
            
            // Memory and behavior services
            services.AddScoped<ScheduledTaskService>();
            services.AddScoped<ScheduledTaskJob>();
            services.AddSingleton<EmbeddingService>();
            services.AddSingleton<MemoryService>();
            services.AddSingleton<InteractiveHistoryService>();
            services.AddSingleton<PostAnalysisService>();
            services.AddSingleton<MiChanAutonomousBehavior>();
            
            // Only start the hosted service when enabled
            if (miChanConfig.Enabled)
                services.AddHostedService<MiChanService>();

            return services;
        }
    }
}
