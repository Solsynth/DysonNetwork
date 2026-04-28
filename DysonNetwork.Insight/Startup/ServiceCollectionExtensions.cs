using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Insight.Reader;
using DysonNetwork.Insight.Services;
using DysonNetwork.Insight.Thought;
using DysonNetwork.Insight.MiChan;
using DysonNetwork.Insight.MiChan.Plugins;
using DysonNetwork.Insight.SnChan;
using DysonNetwork.Insight.SnChan.Plugins;
using DysonNetwork.Insight.Thought.Memory;
using DysonNetwork.Insight.SnDoc;
using DysonNetwork.Insight.Agent.Models;
using DysonNetwork.Insight.Agent.Foundation;
using DysonNetwork.Insight.Agent.Foundation.Providers;
using DysonNetwork.Insight.Thought.Voice;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Pagination;
using DysonNetwork.Shared.Localization;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
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
            services.AddHttpClient("WebReader", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(3);
                client.MaxResponseContentBufferSize = 10 * 1024 * 1024;
                client.DefaultRequestHeaders.Add("User-Agent", "facebookexternalhit/1.1");
            });
            services.AddHttpClient("DuckDuckGo", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            });

            services.AddGrpc(options =>
            {
                options.EnableDetailedErrors = true;
                options.MaxReceiveMessageSize = 16 * 1024 * 1024;
                options.MaxSendMessageSize = 16 * 1024 * 1024;
            });
            services.AddGrpcReflection();

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
            services.AddAgentFoundation();
            services.AddAgentFoundationProviders(configuration);
            services.AddMiChanFoundationProvider();
            services.AddSnChanFoundationProvider();

            var thinkingConfig = configuration.GetSection("Thinking").Get<ThinkingConfig>() ?? new ThinkingConfig();
            services.AddSingleton(thinkingConfig);
            services.AddSingleton<ModelRegistry>();

            var freeQuotaConfig = configuration.GetSection("Thinking:FreeQuota").Get<FreeQuotaConfig>() ?? new FreeQuotaConfig();
            services.AddSingleton(freeQuotaConfig);

            services.AddScoped<ThoughtProvider>();
            services.AddScoped<ThoughtService>();
            services.AddScoped<FreeQuotaService>();
            services.AddScoped<Reader.WebFeedService>();
            services.AddScoped<Reader.WebReaderService>();
            services.AddScoped<ThinkingVoiceService>();
            services.AddHostedService<SequenceSummaryRefreshHostedService>();
            services.AddHostedService<ThoughtPartBackfillHostedService>();
            services.AddHostedService<ThinkingVoiceCleanupHostedService>();

            services.AddSingleton<TokenCountingService>();

            return services;
        }

        public IServiceCollection AddSnChanServices(IConfiguration configuration)
        {
            var snChanConfig = configuration.GetSection("SnChan").Get<SnChanConfig>() ?? new SnChanConfig();
            services.AddSingleton(snChanConfig);

            services.AddSingleton<SnChanModelSelector>();
            services.AddSingleton<SnChanApiClient>();
            services.AddSingleton<SnChanPublisherService>();
            services.AddScoped<SnChanPostPlugin>();
            services.AddScoped<SnChanSwaggerPlugin>();
            services.AddScoped<SnChanMoodPlugin>();
            services.AddScoped<SnChanUserProfilePlugin>();
            services.AddScoped<SnChanMemoryPlugin>();

            if (snChanConfig.ReplyMonitoring.Enabled)
            {
                services.AddScoped<SnChanReplyMonitorService>();
                services.AddScoped<SnChanReplyMonitorJob>();
            }

            if (snChanConfig.DynamicMood.Enabled)
            {
                services.AddScoped<SnChanMoodService>();
            }

            if (snChanConfig.Diary.Enabled)
            {
                services.AddScoped<SnChanDiaryJob>();
            }

            return services;
        }

        public IServiceCollection AddMiChanServices(IConfiguration configuration)
        {
            var miChanConfig = configuration.GetSection("MiChan").Get<MiChanConfig>() ?? new MiChanConfig();
            services.AddSingleton(miChanConfig);

            services.Configure<ModelSelectionConfig>(options =>
            {
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

            services.AddSingleton<IModelSelector, ModelSelector>();
            services.AddSingleton<SolarNetworkApiClient>();

            services.AddSingleton<PostPlugin>();
            services.AddSingleton<AccountPlugin>();
            services.AddSingleton<WebSearchPlugin>();
            services.AddScoped<MemoryPlugin>();
            services.AddScoped<SequenceMemoryPlugin>();
            services.AddScoped<UserProfilePlugin>();
            services.AddScoped<ScheduledTaskPlugin>();
            services.AddScoped<ConversationPlugin>();
            services.AddScoped<MoodPlugin>();
            services.AddScoped<FitnessPlugin>();

            services.AddScoped<ScheduledTaskService>();
            services.AddScoped<ScheduledTaskJob>();
            services.AddScoped<MemoryService>();
            services.AddScoped<EmbeddingService>();
            services.AddScoped<InteractiveHistoryService>();
            services.AddScoped<UserProfileService>();
            services.AddSingleton<PostAnalysisService>();
            services.AddScoped<MiChanAutonomousBehavior>();
            services.AddScoped<MoodService>();

            services.AddScoped<SnDocService>();
            services.AddScoped<SnDocPlugin>();

            services.AddHostedService<MiChanSequenceUnificationHostedService>();

            if (miChanConfig.Enabled)
                services.AddHostedService<MiChanService>();

            return services;
        }
    }
}
