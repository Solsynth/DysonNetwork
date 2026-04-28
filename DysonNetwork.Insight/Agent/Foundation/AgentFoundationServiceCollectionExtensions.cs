namespace DysonNetwork.Insight.Agent.Foundation;

using DysonNetwork.Insight.Agent.Foundation.Models;
using DysonNetwork.Insight.Agent.Foundation.Providers;
using DysonNetwork.Insight.Agent.Models;
using DysonNetwork.Insight.MiChan;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

public static class AgentFoundationServiceCollectionExtensions
{
    public static IServiceCollection AddAgentFoundation(this IServiceCollection services)
    {
        services.AddSingleton<AgentToolRegistry>();
        services.AddSingleton<IAgentToolRegistry>(sp => sp.GetRequiredService<AgentToolRegistry>());
        services.AddSingleton<IAgentToolExecutor>(sp => sp.GetRequiredService<AgentToolRegistry>());
        services.AddSingleton<AgentProviderRegistry>();
        services.AddSingleton<IAgentProviderRegistry>(sp => sp.GetRequiredService<AgentProviderRegistry>());
        services.AddSingleton<AgentFoundationEmbeddingService>();
        services.AddSingleton<IAgentRuntimeSelector, AgentRuntimeSelector>();
        services.AddSingleton<FoundationChatStreamingService>();
        services.AddSingleton<AgentChatClientFactory>();

        return services;
    }

    public static IServiceCollection AddAgentFoundationProviders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var thinkingConfig = configuration.GetSection("Thinking");
        var servicesConfig = thinkingConfig.GetSection("Services");

        var providerRegistry = new Dictionary<string, (string Model, string ApiMode, string? Endpoint, string? ApiKey, string? EmbeddingModel)>();

        foreach (var serviceConfig in servicesConfig.GetChildren())
        {
            var serviceId = serviceConfig.Key;
            var provider = serviceConfig.GetValue<string>("Provider")?.ToLower() ?? "openrouter";
            var model = serviceConfig.GetValue<string>("Model");
            var apiMode = serviceConfig.GetValue<string>("ApiMode")?.Trim().ToLowerInvariant() ?? "chat";
            var endpoint = serviceConfig.GetValue<string>("Endpoint");
            var apiKey = serviceConfig.GetValue<string>("ApiKey");

            if (string.IsNullOrEmpty(model)) continue;

            var providerId = $"{provider}:{apiMode}:{model}";
            providerRegistry[providerId] = (model, apiMode, endpoint, apiKey, null);
        }

        var embeddingConfig = thinkingConfig.GetSection("Embeddings");
        if (!embeddingConfig.Exists())
        {
            embeddingConfig = configuration.GetSection("Embeddings");
        }
        var embeddingModel = embeddingConfig.GetValue<string>("Model");
        if (!string.IsNullOrEmpty(embeddingModel))
        {
            if (!embeddingModel.Contains('/'))
            {
                var embeddingServiceConfig = configuration.GetSection($"Thinking:Services:{embeddingModel}");
                var embeddingProvider = embeddingServiceConfig.GetValue<string>("Provider")?.ToLower() ?? "openrouter";
                var embeddingActualModel = embeddingServiceConfig.GetValue<string>("Model") ?? embeddingModel;
                var embeddingEndpoint = embeddingServiceConfig.GetValue<string>("Endpoint");
                var embeddingApiKey = embeddingServiceConfig.GetValue<string>("ApiKey");

                var embeddingProviderId = $"{embeddingProvider}:chat:{embeddingActualModel}";
                providerRegistry[embeddingProviderId] = (embeddingActualModel, "chat", embeddingEndpoint, embeddingApiKey, embeddingActualModel);
            }
            else
            {
                var embeddingProvider = embeddingConfig.GetValue<string>("Provider")?.ToLower() ?? "openrouter";
                var embeddingProviderId = $"{embeddingProvider}:chat:{embeddingModel}";
                var embeddingApiKey = embeddingConfig.GetValue<string>("ApiKey");
                var embeddingEndpoint = embeddingConfig.GetValue<string>("Endpoint");
                providerRegistry[embeddingProviderId] = (embeddingModel, "chat", embeddingEndpoint, embeddingApiKey, embeddingModel);
            }
        }

        services.AddSingleton<IAgentProviderAdapter[]>(sp =>
        {
            var toolExecutor = sp.GetService<IAgentToolExecutor>();
            var logger = sp.GetService<ILogger<OpenAiCompatibleAdapter>>();
            var adapters = new List<IAgentProviderAdapter>();

            foreach (var (providerId, (model, apiMode, endpoint, apiKey, embeddingModel)) in providerRegistry)
            {
                var effectiveApiKey = apiKey;
                if (string.IsNullOrEmpty(effectiveApiKey))
                {
                    var parts = providerId.Split(':');
                    var provider = parts[0];
                    effectiveApiKey = GetDefaultApiKey(configuration, provider);
                }

                if (string.IsNullOrEmpty(effectiveApiKey)) continue;

                var finalEndpoint = endpoint ?? GetDefaultEndpoint(providerId.Split(':')[0]);

                var adapter = new OpenAiCompatibleAdapter(
                    providerId,
                    model,
                    effectiveApiKey,
                    apiMode,
                    finalEndpoint,
                    embeddingModel,
                    toolExecutor,
                    logger);

                adapters.Add(adapter);
            }

            return adapters.ToArray();
        });

        services.AddHostedService<AgentFoundationInitializationService>();

        return services;
    }

    public static IServiceCollection AddMiChanFoundationProvider(this IServiceCollection services)
    {
        services.AddSingleton<IMiChanFoundationProvider, MiChanFoundationProvider>();
        services.AddSingleton<IMiChanClientProvider, MiChanClientProvider>();
        services.AddSingleton<IAgentClientProvider>(sp => sp.GetRequiredService<IMiChanClientProvider>());
        return services;
    }

    public static IServiceCollection AddSnChanFoundationProvider(this IServiceCollection services)
    {
        services.AddSingleton<ISnChanFoundationProvider, SnChanFoundationProvider>();
        services.AddSingleton<ISnChanClientProvider, SnChanClientProvider>();
        services.AddSingleton<IAgentClientProvider>(sp => sp.GetRequiredService<ISnChanClientProvider>());
        return services;
    }

    private static string? GetDefaultApiKey(IConfiguration configuration, string provider)
    {
        return provider.ToLower() switch
        {
            "openrouter" => configuration.GetValue<string>("Thinking:OpenRouterApiKey"),
            "deepseek" => configuration.GetValue<string>("Thinking:DeepSeekApiKey"),
            "mimo" => configuration.GetValue<string>("Thinking:MimoApiKey"),
            "xiaomimimo" => configuration.GetValue<string>("Thinking:MimoApiKey"),
            "aliyun" => configuration.GetValue<string>("Thinking:AliyunApiKey"),
            "bigmodel" => configuration.GetValue<string>("Thinking:BigModelApiKey"),
            "longcat" => configuration.GetValue<string>("Thinking:LongcatApiKey"),
            _ => configuration.GetValue<string>($"Thinking:{provider}ApiKey")
        };
    }

    private static string? GetDefaultEndpoint(string provider)
    {
        return provider.ToLower() switch
        {
            "openrouter" => "https://openrouter.ai/api/v1",
            "deepseek" => "https://api.deepseek.com/v1",
            "mimo" => "https://api.xiaomimimo.com/v1",
            "xiaomimimo" => "https://api.xiaomimimo.com/v1",
            "aliyun" => "https://dashscope.aliyuncs.com/compatible-mode/v1",
            "bigmodel" => "https://open.bigmodel.cn/api/paas/v4",
            "longcat" => "https://api.longcat.chat/openai",
            _ => null
        };
    }
}
