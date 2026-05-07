using System.Text;
using DysonNetwork.Insight.Agent.Foundation;
using DysonNetwork.Insight.Agent.Foundation.Models;
using DysonNetwork.Insight.Agent.Foundation.Providers;
using DysonNetwork.Insight.MiChan;
using DysonNetwork.Insight.SnChan.Plugins;
using DysonNetwork.Insight.Thought.Memory;
using DysonNetwork.Shared.Proto;
using Grpc.Core;
using NodaTime;

namespace DysonNetwork.Insight.Agent;

public class AgentCompletionGrpcService(
    FoundationChatStreamingService streamingService,
    IServiceProvider serviceProvider,
    IAgentToolRegistry toolRegistry,
    IConfiguration configuration,
    MemoryService memoryService,
    UserProfileService userProfileService,
    MoodService moodService,
    IMiChanFoundationProvider miChanFoundationProvider,
    ISnChanFoundationProvider snChanFoundationProvider,
    MiChanConfig miChanConfig,
    ILogger<AgentCompletionGrpcService> logger
) : DyAgentCompletionService.DyAgentCompletionServiceBase
{
    public override async Task<DyAgentCompletionResponse> Complete(
        DyAgentCompletionRequest request,
        ServerCallContext context)
    {
        if (request.Persona == DyAgentPersona.Unspecified)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "persona is required"));
        if (!Guid.TryParse(request.AccountId, out var accountId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "account_id must be a valid UUID"));
        if (string.IsNullOrWhiteSpace(request.UserMessage))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "user_message is required"));

        var currentUser = await LoadAccountAsync(accountId);
        var conversation = await BuildEphemeralConversationAsync(request, currentUser, accountId, context.CancellationToken);
        var usedVision = false;

        if (request.EnableTools)
        {
            if (request.Persona == DyAgentPersona.Michan)
                toolRegistry.RegisterMiChanPlugins(serviceProvider);
            else
                toolRegistry.RegisterSnChanPlugins(serviceProvider);

            conversation = new AgentConversation(conversation.Messages)
            {
                Tools = toolRegistry.GetAllDefinitions().ToList()
            };
        }

        var (provider, options, modelLabel) = GetProviderAndOptions(request, currentUser, usedVision);
        var response = await streamingService.CompleteChatAsync(provider, conversation, options, context.CancellationToken);
        var content = response.Content ?? string.Empty;

        logger.LogInformation(
            "Agent completion finished for {Persona}, account {AccountId}, model={Model}, usedVision={UsedVision}",
            request.Persona,
            accountId,
            modelLabel,
            usedVision);

        var result = new DyAgentCompletionResponse
        {
            Content = content,
            Model = modelLabel,
            UsedVision = usedVision
        };
        if (!string.IsNullOrWhiteSpace(response.Reasoning))
            result.Reasoning = response.Reasoning;
        return result;
    }

    private async Task<DyAccount> LoadAccountAsync(Guid accountId)
    {
        using var scope = serviceProvider.CreateScope();
        var accountClient = scope.ServiceProvider.GetRequiredService<DyProfileService.DyProfileServiceClient>();
        try
        {
            return await accountClient.GetAccountAsync(new DyGetAccountRequest { Id = accountId.ToString() });
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "account not found"));
        }
    }

    private async Task<AgentConversation> BuildEphemeralConversationAsync(
        DyAgentCompletionRequest request,
        DyAccount currentUser,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var builder = new ConversationBuilder();
        builder.AddSystemMessage(await BuildSystemPromptAsync(request.Persona, currentUser, accountId, request.UserMessage,
            cancellationToken));
        if (!string.IsNullOrWhiteSpace(request.Topic))
            builder.AddSystemMessage("当前请求主题：" + request.Topic);
        if (request.AcceptProposals.Count > 0)
            builder.AddSystemMessage("用户当前允许的提案：" + string.Join(",", request.AcceptProposals));
        if (request.AttachedPostIds.Count > 0)
            builder.AddUserMessage("附加的帖子 ID：" + string.Join(",", request.AttachedPostIds));
        if (request.AttachedMessages.Count > 0)
        {
            foreach (var message in request.AttachedMessages)
            {
                var content = $"附加消息（{message.Role}）：{message.Content}";
                if (string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                    builder.AddAssistantMessage(content);
                else if (string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase))
                    builder.AddSystemMessage(content);
                else
                    builder.AddUserMessage(content);
            }
        }

        builder.AddUserMessage(request.UserMessage);
        return builder.Build();
    }

    private async Task<string> BuildSystemPromptAsync(
        DyAgentPersona persona,
        DyAccount currentUser,
        Guid accountId,
        string userMessage,
        CancellationToken cancellationToken)
    {
        var personality = persona == DyAgentPersona.Michan
            ? PersonalityLoader.LoadPersonality(
                configuration.GetValue<string>("MiChan:PersonalityFile"),
                configuration.GetValue<string>("MiChan:Personality") ?? string.Empty,
                logger)
            : PersonalityLoader.LoadPersonality(
                configuration.GetValue<string>("SnChan:PersonalityFile"),
                configuration.GetValue<string>("SnChan:Personality") ?? string.Empty,
                logger);

        var builder = new StringBuilder();
        builder.AppendLine(personality);
        builder.AppendLine();
        builder.AppendLine("你正在处理一个一次性的 gRPC completion 请求。不要创建、引用或延续 thought sequence。不要声称已经保存记忆或更新档案。");
        builder.AppendLine($"你正在为 {currentUser.Nick} (@{currentUser.Name}) 生成回复，用户 ID 是 {currentUser.Id}。");
        AppendTimeContext(builder, currentUser.Profile?.TimeZone);
        builder.AppendLine("Solar Network 上的 ID 是 UUID，通常很难阅读，所以除非用户要求或必要，否则不要向用户显示 ID。");
        builder.AppendLine();

        if (IsDailyFortuneRequest(persona, userMessage))
        {
            builder.AppendLine("这是每日签到运势的结构化生成请求。优先遵守用户消息中的 JSON schema、语言、签位、去重和长度要求。");
            builder.AppendLine("不要调用热点记忆搜索、情绪或工具；只能基于本次请求提供的资料、用户档案和下方少量近期记忆生成。输出必须保持可解析 JSON。");
            await AppendMiChanUserProfileAsync(builder, accountId, cancellationToken);
            await AppendRecentMiChanMemoriesAsync(builder, accountId, 4, cancellationToken);
            builder.AppendLine();
            return builder.ToString();
        }

        if (persona == DyAgentPersona.Michan)
            await AppendMiChanContextAsync(builder, accountId, userMessage, cancellationToken);

        return builder.ToString();
    }

    private async Task AppendMiChanContextAsync(
        StringBuilder builder,
        Guid accountId,
        string userMessage,
        CancellationToken cancellationToken)
    {
        var userProfile = await userProfileService.GetOrCreateAsync(accountId, "michan", cancellationToken);
        builder.AppendLine("你对该用户的结构化档案（优先级高于零散记忆，回复前先参考）：");
        builder.AppendLine(userProfile.ToPrompt());
        builder.AppendLine();
        builder.AppendLine("重点：优先参考用户对你的态度记忆（attitude:warmth/respect/engagement、attitude_summary、attitude_trend）。");
        builder.AppendLine("- warmth 低时，语气要更稳重、边界更清晰，不要过度热情。");
        builder.AppendLine("- respect 低时，先建立可信度，少做主观推断，多给可验证信息。");
        builder.AppendLine("- engagement 低时，回复应更短更直接，减少连续追问。");
        builder.AppendLine("- 当态度趋势 warming/cooling/mixed 发生变化时，逐步调整交流风格，不要突变。");
        builder.AppendLine();

        var hotMemories = await memoryService.GetHotMemory(accountId, userMessage, 10, "michan", cancellationToken);
        if (hotMemories.Count > 0)
        {
            builder.AppendLine("与你相关的热点记忆（回复前优先复用这些上下文）：");
            foreach (var memory in hotMemories.Take(8))
                builder.AppendLine("- " + memory.ToPrompt());
            builder.AppendLine();
        }
        else
        {
            builder.AppendLine("当前没有命中的热点记忆。遇到需要背景、偏好、长期关系判断的问题时，只能基于本次请求和结构化档案回答。");
            builder.AppendLine();
        }

        var recentMemories = await memoryService.GetRecentMemoriesAsync(accountId, 8, botName: "michan",
            cancellationToken: cancellationToken);
        if (recentMemories.Count > 0)
        {
            builder.AppendLine("最近的新记忆（来自之前的对话或自动行为）：");
            foreach (var memory in recentMemories.Take(8))
                builder.AppendLine("- " + memory.ToPrompt());
            builder.AppendLine();
        }

        var currentMood = await moodService.GetCurrentMoodDescriptionAsync();
        if (!string.IsNullOrWhiteSpace(currentMood))
        {
            builder.AppendLine($"你当前的心情：{currentMood}");
            builder.AppendLine();
        }

        builder.AppendLine("记忆与档案只能作为上下文参考。本次 gRPC completion 不会保存新记忆，也不会更新用户档案或关系分数。");
        builder.AppendLine();
    }

    private static void AppendTimeContext(StringBuilder builder, string? userTimeZone)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var serverZone = DateTimeZoneProviders.Tzdb.GetSystemDefault();
        var serverNow = now.InZone(serverZone);

        builder.AppendLine($"当前时间（服务器时间）: {serverNow:yyyy年MM月dd日 HH:mm:ss}");

        if (!string.IsNullOrEmpty(userTimeZone))
        {
            try
            {
                var tz = DateTimeZoneProviders.Tzdb.GetZoneOrNull(userTimeZone);
                if (tz != null)
                {
                    var local = now.InZone(tz);
                    builder.AppendLine($"用户当地时间: {local:yyyy年MM月dd日 HH:mm:ss} ({userTimeZone})");
                }
                else
                {
                    builder.AppendLine($"（用户时区 {userTimeZone} 无法识别）");
                }
            }
            catch
            {
                builder.AppendLine($"（用户时区 {userTimeZone} 无效）");
            }
        }
        else
        {
            builder.AppendLine("（用户未设置时区）");
        }

        builder.AppendLine();
    }

    private async Task AppendMiChanUserProfileAsync(
        StringBuilder builder,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var userProfile = await userProfileService.GetOrCreateAsync(accountId, "michan", cancellationToken);
        builder.AppendLine("用户结构化档案（用于个性化语气和建议，不要暴露为档案来源）：");
        builder.AppendLine(userProfile.ToPrompt());
        builder.AppendLine();
    }

    private async Task AppendRecentMiChanMemoriesAsync(
        StringBuilder builder,
        Guid accountId,
        int take,
        CancellationToken cancellationToken)
    {
        var recentMemories = await memoryService.GetRecentMemoriesAsync(
            accountId,
            take,
            botName: "michan",
            cancellationToken: cancellationToken);
        if (recentMemories.Count == 0)
            return;

        builder.AppendLine("少量近期记忆（只用于让表达更贴近用户，不要暴露为记忆来源）：");
        foreach (var memory in recentMemories.Take(take))
            builder.AppendLine("- " + memory.ToPrompt());
    }

    private static bool IsDailyFortuneRequest(DyAgentPersona persona, string userMessage)
    {
        return persona == DyAgentPersona.Michan
            && userMessage.Contains("每日签到运势", StringComparison.Ordinal)
            && userMessage.Contains("fortune_report", StringComparison.Ordinal)
            && userMessage.Contains("只输出 JSON", StringComparison.Ordinal);
    }

    private (IAgentProviderAdapter Provider, AgentExecutionOptions Options, string ModelLabel) GetProviderAndOptions(
        DyAgentCompletionRequest request,
        DyAccount currentUser,
        bool usedVision)
    {
        var enableThinking = request.HasThinking ? request.Thinking : true;
        if (request.Persona == DyAgentPersona.Michan)
        {
            var provider = usedVision
                ? miChanFoundationProvider.GetVisionAdapter(currentUser.PerkLevel)
                : miChanFoundationProvider.GetChatAdapter(currentUser.PerkLevel, request.Model);
            var options = usedVision
                ? miChanFoundationProvider.CreateVisionExecutionOptions(
                    reasoningEffort: request.ReasoningEffort,
                    enableThinking: enableThinking)
                : miChanFoundationProvider.CreateExecutionOptions(
                    reasoningEffort: request.ReasoningEffort,
                    enableThinking: enableThinking);
            options = WithCompletionOverrides(WithToolOptions(options, request.EnableTools), request);
            var model = usedVision ? miChanConfig.Vision.VisionThinkingService : request.Model ?? miChanConfig.ThinkingModel.ModelId;
            return (provider, options, model);
        }

        var snProvider = snChanFoundationProvider.GetChatAdapter(request.Model);
        var snOptions = snChanFoundationProvider.CreateExecutionOptions(
            reasoningEffort: request.ReasoningEffort,
            enableThinking: enableThinking);
        snOptions = WithCompletionOverrides(WithToolOptions(snOptions, request.EnableTools), request);
        return (snProvider, snOptions, request.Model ?? snProvider.ProviderId);
    }

    private static AgentExecutionOptions WithToolOptions(AgentExecutionOptions options, bool enableTools) => new()
    {
        Temperature = options.Temperature,
        TopP = options.TopP,
        MaxTokens = options.MaxTokens,
        ReasoningEffort = options.ReasoningEffort,
        EnableThinking = options.EnableThinking,
        EnableTools = enableTools && options.EnableTools,
        AutoInvokeTools = enableTools,
        MaxToolRounds = options.MaxToolRounds,
        AdditionalParameters = options.AdditionalParameters
    };

    private static AgentExecutionOptions WithCompletionOverrides(
        AgentExecutionOptions options,
        DyAgentCompletionRequest request)
    {
        if (request.Persona != DyAgentPersona.Michan ||
            !request.Topic.StartsWith("每日签到运势", StringComparison.Ordinal))
            return ApplyRequestTuning(options, request);

        var tuned = new AgentExecutionOptions
        {
            Temperature = Math.Max(options.Temperature ?? 0.7, 0.95),
            TopP = options.TopP ?? 0.92,
            MaxTokens = options.MaxTokens,
            ReasoningEffort = options.ReasoningEffort,
            EnableThinking = options.EnableThinking,
            EnableTools = options.EnableTools,
            AutoInvokeTools = options.AutoInvokeTools,
            MaxToolRounds = options.MaxToolRounds,
            AdditionalParameters = options.AdditionalParameters
        };

        return ApplyRequestTuning(tuned, request);
    }

    private static AgentExecutionOptions ApplyRequestTuning(
        AgentExecutionOptions options,
        DyAgentCompletionRequest request) => new()
    {
        Temperature = request.HasTemperature ? request.Temperature : options.Temperature,
        TopP = request.HasTopP ? request.TopP : options.TopP,
        MaxTokens = request.HasMaxTokens ? request.MaxTokens : options.MaxTokens,
        ReasoningEffort = options.ReasoningEffort,
        EnableThinking = options.EnableThinking,
        EnableTools = options.EnableTools,
        AutoInvokeTools = options.AutoInvokeTools,
        MaxToolRounds = options.MaxToolRounds,
        AdditionalParameters = options.AdditionalParameters
    };

}
