using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Insight.Reader;
using DysonNetwork.Insight.Services;
using DysonNetwork.Insight.Thought;
using DysonNetwork.Insight.MiChan;
using DysonNetwork.Insight.MiChan.KernelBuilding;
using DysonNetwork.Insight.MiChan.Plugins;
using DysonNetwork.Insight.SnChan;
using DysonNetwork.Insight.Thought.Memory;
using DysonNetwork.Insight.SnDoc;
using DysonNetwork.Insight.Agent.Models;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Pagination;
using DysonNetwork.Shared.Localization;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.SemanticKernel;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using SemanticKernelBuilder = DysonNetwork.Insight.Agent.KernelBuilding.SemanticKernelBuilder;

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
            services.AddHttpClient("WebReader", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(3);
                client.MaxResponseContentBufferSize = 10 * 1024 * 1024; // 10MB
                client.DefaultRequestHeaders.Add("User-Agent", "facebookexternalhit/1.1");
            });
            services.AddHttpClient("DuckDuckGo", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            });

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
            services.AddControllers().AddPaginationValidationFilter().AddJsonOptions(options =>
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

            return services;
        }

        public IServiceCollection AddThinkingServices(IConfiguration configuration)
        {
            // Shared kernel factory for AI service creation
            services.AddSingleton<KernelFactory>();

            var freeQuotaConfig = configuration.GetSection("Thinking:FreeQuota").Get<FreeQuotaConfig>() ?? new FreeQuotaConfig();
            services.AddSingleton(freeQuotaConfig);

            services.AddScoped<ThoughtProvider>();
            services.AddScoped<ThoughtService>();
            services.AddScoped<FreeQuotaService>();
            services.AddScoped<Reader.WebFeedService>();
            services.AddScoped<Reader.WebReaderService>();

            // Register token counting service for accurate AI token counting
            services.AddSingleton<TokenCountingService>();

            return services;
        }

        public IServiceCollection AddSnChanServices(IConfiguration configuration)
        {
            var snChanConfig = configuration.GetSection("SnChan").Get<SnChanConfig>() ?? new SnChanConfig();
            services.AddSingleton(snChanConfig);

            // Always register SnChanModelSelector - it checks UseModelSelection internally
            services.AddSingleton<SnChanModelSelector>();

            // Register SnChan API client for bot operations
            services.AddSingleton<SnChanApiClient>();

            return services;
        }

        public IServiceCollection AddMiChanServices(IConfiguration configuration)
        {
            var miChanConfig = configuration.GetSection("MiChan").Get<MiChanConfig>() ?? new MiChanConfig();
            services.AddSingleton(miChanConfig);

            // Register model selection configuration
            services.Configure<ModelSelectionConfig>(options =>
            {
                // Copy mappings from MiChan config if using model selection
                if (miChanConfig.UseModelSelection && miChanConfig.ModelSelection?.Mappings != null)
                {
                    options.Mappings = miChanConfig.ModelSelection.Mappings.Select(m => new ModelUseCaseMapping
                    {
                        UseCase = m.UseCase,
                        ModelId = m.ModelId,
                        MinPerkLevel = m.MinPerkLevel,
                        MaxPerkLevel = m.MaxPerkLevel,
                        IsDefault = m.IsDefault,
                        Priority = m.Priority,
                        DisplayName = m.DisplayName,
                        Description = m.Description,
                        Enabled = m.Enabled
                    }).ToList();
                    options.DefaultModelId = miChanConfig.ModelSelection.DefaultModelId;
                    options.AllowUserOverride = miChanConfig.ModelSelection.AllowUserOverride;
                }
            });

            // Register model selector service for PerkLevel-based model selection
            services.AddSingleton<IModelSelector, ModelSelector>();

            // Always register MiChan services for dependency injection (needed by ThoughtController)
            // Core services
            services.AddSingleton<KernelFactory>();
            services.AddSingleton<SolarNetworkApiClient>();

            // Kernel building infrastructure
            services.AddSingleton<SemanticKernelBuilder>();
            services.AddSingleton<DysonNetwork.Insight.Agent.KernelBuilding.IKernelBuilder>(sp => sp.GetRequiredService<SemanticKernelBuilder>());

            services.AddSingleton<MiChanKernelProvider>();
            services.AddSingleton<GeneralKernelProvider>();
            services.AddSingleton<IKernelProvider>(sp => sp.GetRequiredService<GeneralKernelProvider>());

            // Plugins
            services.AddSingleton<PostPlugin>();
            services.AddSingleton<AccountPlugin>();
            services.AddSingleton<WebSearchPlugin>();
            services.AddScoped<MemoryPlugin>();
            services.AddScoped<UserProfilePlugin>();
            services.AddScoped<ScheduledTaskPlugin>();
            services.AddScoped<ConversationPlugin>();
            services.AddScoped<MoodPlugin>();
            services.AddScoped<FitnessPlugin>();

            // Memory and behavior services
            services.AddScoped<ScheduledTaskService>();
            services.AddScoped<ScheduledTaskJob>();
            services.AddSingleton<EmbeddingService>();
            services.AddScoped<MemoryService>();
            services.AddScoped<InteractiveHistoryService>();
            services.AddScoped<UserProfileService>();
            services.AddSingleton<PostAnalysisService>();
            services.AddScoped<MiChanAutonomousBehavior>();
            services.AddScoped<MoodService>();

            // SnDoc services
            services.AddScoped<SnDocService>();
            services.AddScoped<SnDocPlugin>();

            services.AddHostedService<MiChanSequenceUnificationHostedService>();

            // Only start the hosted service when enabled
            if (miChanConfig.Enabled)
                services.AddHostedService<MiChanService>();

            return services;
        }
    }
}
