using DysonNetwork.Insight.Agent.Foundation;
using DysonNetwork.Insight.Agent.Foundation.Models;
using DysonNetwork.Insight.Agent.Foundation.Providers;
using DysonNetwork.Insight.MiChan;
using DysonNetwork.Insight.SnChan.Plugins;
using DysonNetwork.Insight.Thought;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Grpc.Core;

namespace DysonNetwork.Insight.Agent;

public class AgentCompletionGrpcService(
    ThoughtService thoughtService,
    FoundationChatStreamingService streamingService,
    IServiceProvider serviceProvider,
    IAgentToolRegistry toolRegistry,
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

        var sequenceId = TryParseOptionalGuid(request.SequenceId, "sequence_id");
        var botName = GetBotName(request.Persona);
        var sequence = await thoughtService.GetOrCreateSequenceAsync(
            accountId,
            sequenceId,
            request.Topic,
            botName);
        if (sequence is null)
            throw new RpcException(new Status(StatusCode.NotFound, "sequence not found for account"));

        var currentUser = await LoadAccountAsync(accountId);
        var userThought = request.Persist
            ? await thoughtService.SaveThoughtAsync(
                sequence,
                [new SnThinkingMessagePart { Type = ThinkingMessagePartType.Text, Text = request.UserMessage }],
                ThinkingThoughtRole.User,
                botName: botName)
            : null;

        var attachedMessages = request.AttachedMessages
            .Select(message => new Dictionary<string, dynamic>
            {
                ["role"] = message.Role,
                ["content"] = message.Content
            })
            .ToList();

        var (conversation, usedVision) = request.Persona == DyAgentPersona.Michan
            ? await thoughtService.BuildMiChanConversationAsync(
                sequence,
                currentUser,
                request.UserMessage,
                request.AttachedPostIds.ToList(),
                attachedMessages,
                request.AcceptProposals.ToList(),
                [],
                userThought?.Id)
            : await thoughtService.BuildSnChanConversationAsync(
                sequence,
                currentUser,
                request.UserMessage,
                request.AttachedPostIds.ToList(),
                attachedMessages,
                request.AcceptProposals.ToList(),
                [],
                userThought?.Id);

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

        if (request.Persist)
        {
            var assistantParts = new List<SnThinkingMessagePart>();
            if (!string.IsNullOrWhiteSpace(content))
                assistantParts.Add(new SnThinkingMessagePart { Type = ThinkingMessagePartType.Text, Text = content });
            if (!string.IsNullOrWhiteSpace(response.Reasoning))
                assistantParts.Add(new SnThinkingMessagePart { Type = ThinkingMessagePartType.Reasoning, Reasoning = response.Reasoning });
            foreach (var toolCall in response.ToolCalls ?? [])
            {
                assistantParts.Add(new SnThinkingMessagePart
                {
                    Type = ThinkingMessagePartType.FunctionCall,
                    FunctionCall = new SnFunctionCall
                    {
                        Id = toolCall.Id,
                        Name = toolCall.Name,
                        Arguments = toolCall.Arguments
                    }
                });
            }

            if (assistantParts.Count > 0)
                await thoughtService.SaveThoughtAsync(sequence, assistantParts, ThinkingThoughtRole.Assistant, modelLabel, botName);
        }

        logger.LogInformation(
            "Agent completion finished for {Persona}, account {AccountId}, sequence {SequenceId}, model={Model}, usedVision={UsedVision}",
            request.Persona,
            accountId,
            sequence.Id,
            modelLabel,
            usedVision);

        var result = new DyAgentCompletionResponse
        {
            Content = content,
            SequenceId = sequence.Id.ToString(),
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

    private static Guid? TryParseOptionalGuid(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (Guid.TryParse(value, out var parsed))
            return parsed;
        throw new RpcException(new Status(StatusCode.InvalidArgument, $"{fieldName} must be a valid UUID"));
    }

    private static string GetBotName(DyAgentPersona persona) => persona switch
    {
        DyAgentPersona.Michan => "michan",
        DyAgentPersona.Snchan => "snchan",
        _ => throw new RpcException(new Status(StatusCode.InvalidArgument, "unsupported persona"))
    };
}
