using DysonNetwork.Insight.Agent.Foundation;
using DysonNetwork.Insight.Agent.Foundation.Models;
using DysonNetwork.Insight.Agent.Foundation.Providers;
using DysonNetwork.Insight.MiChan;
using DysonNetwork.Insight.SnChan.Plugins;
using DysonNetwork.Shared.Proto;
using Grpc.Core;

namespace DysonNetwork.Insight.Agent;

public class AgentCompletionGrpcService(
    FoundationChatStreamingService streamingService,
    IServiceProvider serviceProvider,
    IAgentToolRegistry toolRegistry,
    IConfiguration configuration,
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
        var conversation = BuildEphemeralConversation(request, currentUser);
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

    private AgentConversation BuildEphemeralConversation(DyAgentCompletionRequest request, DyAccount currentUser)
    {
        var builder = new ConversationBuilder();
        builder.AddSystemMessage(BuildSystemPrompt(request.Persona, currentUser));
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

    private string BuildSystemPrompt(DyAgentPersona persona, DyAccount currentUser)
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

        return $$"""
{{personality}}

你正在处理一个一次性的 gRPC completion 请求。不要创建、引用或延续 thought sequence。不要声称已经保存记忆或更新档案。
你正在为 {{currentUser.Nick}} (@{{currentUser.Name}}) 生成回复，用户 ID 是 {{currentUser.Id}}。
Solar Network 上的 ID 是 UUID，通常很难阅读，所以除非用户要求或必要，否则不要向用户显示 ID。
""";
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
            options = WithToolOptions(options, request.EnableTools);
            var model = usedVision ? miChanConfig.Vision.VisionThinkingService : request.Model ?? miChanConfig.ThinkingModel.ModelId;
            return (provider, options, model);
        }

        var snProvider = snChanFoundationProvider.GetChatAdapter(request.Model);
        var snOptions = snChanFoundationProvider.CreateExecutionOptions(
            reasoningEffort: request.ReasoningEffort,
            enableThinking: enableThinking);
        snOptions = WithToolOptions(snOptions, request.EnableTools);
        return (snProvider, snOptions, request.Model ?? snProvider.ProviderId);
    }

    private static AgentExecutionOptions WithToolOptions(AgentExecutionOptions options, bool enableTools) => new()
    {
        Temperature = options.Temperature,
        MaxTokens = options.MaxTokens,
        ReasoningEffort = options.ReasoningEffort,
        EnableThinking = options.EnableThinking,
        EnableTools = enableTools && options.EnableTools,
        AutoInvokeTools = enableTools,
        MaxToolRounds = options.MaxToolRounds,
        AdditionalParameters = options.AdditionalParameters
    };

}
