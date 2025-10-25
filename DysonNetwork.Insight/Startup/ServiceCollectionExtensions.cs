using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Insight.Thinking;
using DysonNetwork.Shared.Cache;
using LangChain.Memory;
using LangChain.Serve;
using LangChain.Serve.Abstractions.Repository;
using LangChain.Serve.OpenAI;
using static LangChain.Chains.Chain;
using Message = LangChain.Providers.Message;
using MessageRole = LangChain.Providers.MessageRole;
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
        var modelProvider = new ThinkingProvider(configuration);
        services.AddSingleton(modelProvider);

        services.AddCustomNameGenerator(async messages =>
        {
            var template =
                @"You will be given conversation between User and Assistant. Your task is to give name to this conversation using maximum 3 words
Conversation:
{chat_history}
Your name: ";
            var conversationBufferMemory = await ConvertToConversationBuffer(messages);
            var chain = LoadMemory(conversationBufferMemory, "chat_history")
                        | Template(template)
                        | LLM(modelProvider.GetModel());

            return await chain.RunAsync("text") ?? string.Empty;
        });

        return services;
    }

    private static async Task<ConversationBufferMemory> ConvertToConversationBuffer(
        IReadOnlyCollection<StoredMessage> list
        )
    {
        var conversationBufferMemory = new ConversationBufferMemory
        {
            Formatter =
            {
                HumanPrefix = "User",
                AiPrefix = "Assistant",
            }
        };
        List<Message> converted = list
            .Select(x => new Message(x.Content, x.Author == MessageAuthor.User ? MessageRole.Human : MessageRole.Ai))
            .ToList();

        await conversationBufferMemory.ChatHistory.AddMessages(converted);

        return conversationBufferMemory;
    }
}