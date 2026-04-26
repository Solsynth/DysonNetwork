namespace DysonNetwork.Insight.Agent.Foundation;

using System.ClientModel;
using System.Text;
using System.Text.Json;
using DysonNetwork.Insight.Agent.Foundation.Models;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;

#pragma warning disable OPENAI001 

public class OpenAiCompatibleAdapter : IAgentProviderAdapter
{
    private const string ToolNameDotEscape = "__dot__";

    private readonly ChatClient _chatClient;
    private readonly EmbeddingClient? _embeddingClient;
    private readonly IAgentToolExecutor? _toolExecutor;
    private readonly ILogger<OpenAiCompatibleAdapter>? _logger;
    private readonly string _modelId;
    private readonly string? _embeddingModelId;

    public string ProviderId { get; }

    public OpenAiCompatibleAdapter(
        string providerId,
        string modelId,
        string apiKey,
        string? baseUrl = null,
        string? embeddingModelId = null,
        IAgentToolExecutor? toolExecutor = null,
        ILogger<OpenAiCompatibleAdapter>? logger = null)
    {
        ProviderId = providerId;
        _modelId = modelId;
        _embeddingModelId = embeddingModelId;
        _toolExecutor = toolExecutor;
        _logger = logger;

        var options = new OpenAIClientOptions();
        if (!string.IsNullOrEmpty(baseUrl))
        {
            options.Endpoint = new Uri(baseUrl);
        }

        var client = new OpenAIClient(new ApiKeyCredential(apiKey), options);
        _chatClient = client.GetChatClient(modelId);

        if (!string.IsNullOrEmpty(embeddingModelId))
        {
            _embeddingClient = client.GetEmbeddingClient(embeddingModelId);
        }
    }

    public async Task<AgentChatResponse> CompleteChatAsync(
        AgentConversation conversation,
        AgentExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var chatMessages = ConvertMessages(conversation.Messages);
        var chatOptions = BuildChatOptions(conversation.Tools, options);

        var completion = await _chatClient.CompleteChatAsync(chatMessages, chatOptions, cancellationToken);

        return ConvertResponse(completion.Value);
    }

    public async IAsyncEnumerable<AgentStreamEvent> CompleteChatStreamingAsync(
        AgentConversation conversation,
        AgentExecutionOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var chatMessages = ConvertMessages(conversation.Messages);
        var chatOptions = BuildChatOptions(conversation.Tools, options);
        var maxRounds = options?.MaxToolRounds ?? 10;
        var round = 0;

        var currentMessages = chatMessages.ToList();
        var currentOptions = chatOptions;

        while (round < maxRounds)
        {
            round++;
            var hasToolCalls = false;
            var toolCalls = new List<StreamingToolCallAccumulator>();
            var textBuilder = new StringBuilder();
            ChatFinishReason finishReason = ChatFinishReason.Stop;
            int? inputTokens = null;
            int? outputTokens = null;

            await foreach (var update in _chatClient.CompleteChatStreamingAsync(currentMessages, currentOptions, cancellationToken))
            {
                if (update.ContentUpdate.Count > 0)
                {
                    foreach (var part in update.ContentUpdate)
                    {
                        if (!string.IsNullOrEmpty(part.Text))
                        {
                            textBuilder.Append(part.Text);
                            yield return new AgentStreamEvent.TextDelta(part.Text);
                        }
                    }
                }

                if (update.ToolCallUpdates != null)
                {
                    foreach (var toolUpdate in update.ToolCallUpdates)
                    {
                        hasToolCalls = true;

                        var toolCall = toolCalls.FirstOrDefault(t => t.Index == toolUpdate.Index);
                        if (toolCall == null)
                        {
                            toolCall = new StreamingToolCallAccumulator
                            {
                                Index = toolUpdate.Index,
                                Id = toolUpdate.ToolCallId ?? "",
                                Name = DenormalizeToolNameFromProvider(toolUpdate.FunctionName ?? "")
                            };
                            toolCalls.Add(toolCall);
                        }

                        if (!string.IsNullOrEmpty(toolUpdate.ToolCallId))
                        {
                            toolCall.Id = toolUpdate.ToolCallId;
                        }

                        if (!string.IsNullOrEmpty(toolUpdate.FunctionName))
                        {
                            toolCall.Name = DenormalizeToolNameFromProvider(toolUpdate.FunctionName);
                        }

                        if (!toolCall.Started &&
                            !string.IsNullOrEmpty(toolCall.Id) &&
                            !string.IsNullOrEmpty(toolCall.Name))
                        {
                            toolCall.Started = true;
                            yield return new AgentStreamEvent.ToolCallStarted(toolCall.Id, toolCall.Name);

                            if (toolCall.Arguments.Length > 0)
                            {
                                yield return new AgentStreamEvent.ToolCallDelta(
                                    toolCall.Id,
                                    toolCall.Name,
                                    toolCall.Arguments.ToString());
                            }
                        }

                        if (toolUpdate.FunctionArgumentsUpdate != null)
                        {
                            var argsUpdate = toolUpdate.FunctionArgumentsUpdate.ToString() ?? "";
                            toolCall.Arguments.Append(argsUpdate);

                            if (toolCall.Started)
                            {
                                yield return new AgentStreamEvent.ToolCallDelta(toolCall.Id, toolCall.Name, argsUpdate);
                            }
                        }
                    }
                }

                if (update.FinishReason.HasValue)
                {
                    finishReason = update.FinishReason.Value;
                }

                if (update.Usage != null)
                {
                    inputTokens = update.Usage.InputTokenCount;
                    outputTokens = update.Usage.OutputTokenCount;
                }
            }

            if (!hasToolCalls || toolCalls.Count == 0)
            {
                yield return new AgentStreamEvent.Completed(
                    ConvertFinishReason(finishReason),
                    inputTokens,
                    outputTokens);
                yield break;
            }

            var toolCallMessages = new List<ChatToolCall>();
            foreach (var toolCall in toolCalls)
            {
                var normalizedArgs = NormalizeToolArguments(toolCall.Arguments.ToString());
                yield return new AgentStreamEvent.ToolCallCompleted(toolCall.Id, toolCall.Name, normalizedArgs);
                toolCallMessages.Add(ChatToolCall.CreateFunctionToolCall(
                    toolCall.Id,
                    NormalizeToolNameForProvider(toolCall.Name),
                    BinaryData.FromString(normalizedArgs)));
            }

            var assistantMessage = new AssistantChatMessage(toolCallMessages);
            if (textBuilder.Length > 0)
            {
                assistantMessage.Content.Add(ChatMessageContentPart.CreateTextPart(textBuilder.ToString()));
            }
            currentMessages.Add(assistantMessage);

            if (options?.AutoInvokeTools == true && _toolExecutor != null)
            {
                foreach (var toolCall in toolCalls)
                {
                    var agentToolCall = new AgentToolCall
                    {
                        Id = toolCall.Id,
                        Name = toolCall.Name,
                        Arguments = NormalizeToolArguments(toolCall.Arguments.ToString())
                    };
                    var result = await _toolExecutor.ExecuteToolAsync(agentToolCall, cancellationToken);

                    yield return new AgentStreamEvent.ToolResultReady(toolCall.Id, toolCall.Name, result.Result, result.IsError);
                    currentMessages.Add(new ToolChatMessage(toolCall.Id, result.Result));
                }
            }
            else
            {
                yield return new AgentStreamEvent.Completed(AgentFinishReason.ToolCalls, inputTokens, outputTokens);
                yield break;
            }
        }

        _logger?.LogWarning("Max tool rounds ({MaxRounds}) reached in streaming chat", maxRounds);
        yield return new AgentStreamEvent.Completed(AgentFinishReason.Length, null, null);
    }

    public async Task<AgentEmbeddingResponse> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (_embeddingClient == null)
        {
            throw new InvalidOperationException($"Embedding is not configured for provider '{ProviderId}'");
        }

        var embedding = await _embeddingClient.GenerateEmbeddingAsync(text, cancellationToken: cancellationToken);
        var vector = embedding.Value.ToFloats();

        return new AgentEmbeddingResponse
        {
            Embedding = vector.ToArray(),
            Dimensions = vector.Length,
            InputTokens = 0
        };
    }

    public async Task<IReadOnlyList<AgentEmbeddingResponse>> GenerateEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        if (_embeddingClient == null)
        {
            throw new InvalidOperationException($"Embedding is not configured for provider '{ProviderId}'");
        }

        var embeddings = await _embeddingClient.GenerateEmbeddingsAsync(texts, cancellationToken: cancellationToken);

        return embeddings.Value.Select(e =>
        {
            var vector = e.ToFloats();
            return new AgentEmbeddingResponse
            {
                Embedding = vector.ToArray(),
                Dimensions = vector.Length,
                InputTokens = 0
            };
        }).ToList();
    }

    private List<ChatMessage> ConvertMessages(List<AgentMessage> messages)
    {
        var result = new List<ChatMessage>();

        foreach (var msg in messages)
        {
            switch (msg.Role)
            {
                case AgentMessageRole.System:
                    result.Add(new SystemChatMessage(msg.Content ?? ""));
                    break;

                case AgentMessageRole.User:
                    if (msg.ContentParts != null && msg.ContentParts.Any())
                    {
                        var parts = new List<ChatMessageContentPart>();
                        foreach (var part in msg.ContentParts)
                        {
                            switch (part.Type)
                            {
                                case AgentContentPartType.Text:
                                    parts.Add(ChatMessageContentPart.CreateTextPart(part.Text ?? ""));
                                    break;
                                case AgentContentPartType.ImageUrl:
                                    parts.Add(ChatMessageContentPart.CreateImagePart(new Uri(part.ImageUrl!)));
                                    break;
                                case AgentContentPartType.ImageData:
                                    parts.Add(ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(part.ImageData!), part.ImageMediaType));
                                    break;
                                case AgentContentPartType.FileUrl:
                                    if (!string.IsNullOrWhiteSpace(part.FileUrl))
                                    {
                                        parts.Add(ChatMessageContentPart.CreateTextPart(
                                            $"[Attached File URL] {part.FileName ?? "file"} ({part.FileMediaType ?? "unknown"}) {part.FileUrl}"));
                                    }

                                    break;
                                case AgentContentPartType.FileData:
                                    if (part.FileData != null)
                                    {
                                        try
                                        {
                                            parts.Add(ChatMessageContentPart.CreateFilePart(
                                                BinaryData.FromBytes(part.FileData),
                                                part.FileName ?? "attachment",
                                                part.FileMediaType));
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger?.LogWarning(ex,
                                                "Failed to create inline file part for {FileName}; falling back to text notice.",
                                                part.FileName ?? "attachment");
                                            parts.Add(ChatMessageContentPart.CreateTextPart(
                                                $"[Attached File] {part.FileName ?? "file"} ({part.FileMediaType ?? "unknown"})"));
                                        }
                                    }

                                    break;
                            }
                        }
                        result.Add(new UserChatMessage(parts));
                    }
                    else
                    {
                        result.Add(new UserChatMessage(msg.Content ?? ""));
                    }
                    break;

                case AgentMessageRole.Assistant:
                    if (msg.ToolCalls != null && msg.ToolCalls.Count > 0)
                    {
                        var toolCalls = msg.ToolCalls
                            .Select(tc => ChatToolCall.CreateFunctionToolCall(
                                tc.Id,
                                NormalizeToolNameForProvider(tc.Name),
                                BinaryData.FromString(tc.Arguments)))
                            .ToList();
                        var assistantMsg = new AssistantChatMessage(toolCalls);
                        if (!string.IsNullOrEmpty(msg.Content))
                        {
                            assistantMsg.Content.Add(ChatMessageContentPart.CreateTextPart(msg.Content));
                        }
                        result.Add(assistantMsg);
                    }
                    else
                    {
                        result.Add(new AssistantChatMessage(msg.Content ?? ""));
                    }
                    break;

                case AgentMessageRole.Tool:
                    result.Add(new ToolChatMessage(msg.ToolCallId ?? "", msg.ToolResultContent ?? ""));
                    break;
            }
        }

        return result;
    }

    private ChatCompletionOptions BuildChatOptions(List<AgentToolDefinition>? tools, AgentExecutionOptions? options)
    {
        var chatOptions = new ChatCompletionOptions();

        if (options?.Temperature.HasValue == true)
        {
            chatOptions.Temperature = (float)options.Temperature.Value;
        }

        if (options?.MaxTokens.HasValue == true)
        {
            chatOptions.MaxOutputTokenCount = options.MaxTokens.Value;
        }

        if (!string.IsNullOrWhiteSpace(options?.ReasoningEffort))
        {
            if (ProviderId.StartsWith("deepseek", StringComparison.OrdinalIgnoreCase))
            {
                _logger?.LogInformation(
                    "Skipping reasoning effort for provider '{ProviderId}' to avoid reasoning_content replay requirement.",
                    ProviderId);
            }
            else if (TryMapReasoningEffort(options.ReasoningEffort!, out var reasoningEffortLevel))
            {
                chatOptions.ReasoningEffortLevel = reasoningEffortLevel;
            }
            else
            {
                _logger?.LogWarning(
                    "Ignoring unsupported reasoning effort value '{ReasoningEffort}'. Expected low|medium|high.",
                    options.ReasoningEffort);
            }
        }

        if (options?.EnableTools == true && tools != null && tools.Count > 0)
        {
            foreach (var tool in tools)
            {
                ChatTool chatTool;
                if (string.IsNullOrEmpty(tool.ParametersJsonSchema))
                {
                    chatTool = ChatTool.CreateFunctionTool(
                        NormalizeToolNameForProvider(tool.Name),
                        tool.Description);
                }
                else
                {
                    chatTool = ChatTool.CreateFunctionTool(
                        NormalizeToolNameForProvider(tool.Name),
                        tool.Description,
                        BinaryData.FromString(tool.ParametersJsonSchema));
                }

                chatOptions.Tools.Add(chatTool);
            }
        }

        return chatOptions;
    }

    private static bool TryMapReasoningEffort(string value, out ChatReasoningEffortLevel level)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "low":
                level = ChatReasoningEffortLevel.Low;
                return true;
            case "medium":
                level = ChatReasoningEffortLevel.Medium;
                return true;
            case "high":
                level = ChatReasoningEffortLevel.High;
                return true;
            default:
                level = default;
                return false;
        }
    }

    private AgentChatResponse ConvertResponse(ChatCompletion completion)
    {
        var response = new AgentChatResponse
        {
            Content = completion.Content.Count > 0 ? completion.Content[0].Text ?? "" : "",
            FinishReason = ConvertFinishReason(completion.FinishReason),
            InputTokens = completion.Usage?.InputTokenCount,
            OutputTokens = completion.Usage?.OutputTokenCount
        };

        if (completion.ToolCalls.Count > 0)
        {
            response.ToolCalls = completion.ToolCalls
                .Select(tc => new AgentToolCall
                {
                    Id = tc.Id,
                    Name = DenormalizeToolNameFromProvider(tc.FunctionName),
                    Arguments = NormalizeToolArguments(tc.FunctionArguments?.ToString())
                })
                .ToList();
        }

        return response;
    }

    private static AgentFinishReason ConvertFinishReason(ChatFinishReason reason) => reason switch
    {
        ChatFinishReason.Stop => AgentFinishReason.Stop,
        ChatFinishReason.ToolCalls => AgentFinishReason.ToolCalls,
        ChatFinishReason.Length => AgentFinishReason.Length,
        ChatFinishReason.ContentFilter => AgentFinishReason.ContentFilter,
        _ => AgentFinishReason.Unknown
    };

    private static string NormalizeToolNameForProvider(string name)
    {
        return string.IsNullOrEmpty(name)
            ? name
            : name.Replace(".", ToolNameDotEscape, StringComparison.Ordinal);
    }

    private static string DenormalizeToolNameFromProvider(string name)
    {
        return string.IsNullOrEmpty(name)
            ? name
            : name.Replace(ToolNameDotEscape, ".", StringComparison.Ordinal);
    }

    private static string NormalizeToolArguments(string? arguments)
    {
        return string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments;
    }

    private sealed class StreamingToolCallAccumulator
    {
        public int Index { get; init; }
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public StringBuilder Arguments { get; } = new();
        public bool Started { get; set; }
    }
}

#pragma warning restore OPENAI001
