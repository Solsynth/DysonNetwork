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
            var toolCalls = new List<(string Id, string Name, string Arguments)>();
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

                        if (!string.IsNullOrEmpty(toolUpdate.ToolCallId))
                        {
                            var existingCall = toolCalls.FirstOrDefault(t => t.Id == toolUpdate.ToolCallId);
                            if (existingCall == default)
                            {
                                var args = toolUpdate.FunctionArgumentsUpdate?.ToString() ?? "";
                                var newCall = (toolUpdate.ToolCallId, toolUpdate.FunctionName ?? "", args);
                                toolCalls.Add(newCall);
                                yield return new AgentStreamEvent.ToolCallStarted(toolUpdate.ToolCallId, toolUpdate.FunctionName ?? "");
                            }
                            else if (toolUpdate.FunctionArgumentsUpdate != null)
                            {
                                var index = toolCalls.IndexOf(existingCall);
                                var argsUpdate = toolUpdate.FunctionArgumentsUpdate.ToString() ?? "";
                                toolCalls[index] = (existingCall.Id, existingCall.Name, existingCall.Arguments + argsUpdate);
                                yield return new AgentStreamEvent.ToolCallDelta(toolUpdate.ToolCallId, toolUpdate.FunctionName ?? "", argsUpdate);
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
            foreach (var (id, name, args) in toolCalls)
            {
                toolCallMessages.Add(ChatToolCall.CreateFunctionToolCall(id, name, BinaryData.FromString(args)));
            }

            var assistantMessage = new AssistantChatMessage(toolCallMessages);
            if (textBuilder.Length > 0)
            {
                assistantMessage.Content.Add(ChatMessageContentPart.CreateTextPart(textBuilder.ToString()));
            }
            currentMessages.Add(assistantMessage);

            if (options?.AutoInvokeTools == true && _toolExecutor != null)
            {
                foreach (var (id, name, args) in toolCalls)
                {
                    var toolCall = new AgentToolCall { Id = id, Name = name, Arguments = args };
                    var result = await _toolExecutor.ExecuteToolAsync(toolCall, cancellationToken);

                    yield return new AgentStreamEvent.ToolResultReady(id, name, result.Result, result.IsError);
                    currentMessages.Add(new ToolChatMessage(id, result.Result));
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
                            .Select(tc => ChatToolCall.CreateFunctionToolCall(tc.Id, tc.Name, BinaryData.FromString(tc.Arguments)))
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

        if (options?.EnableTools == true && tools != null && tools.Count > 0)
        {
            foreach (var tool in tools)
            {
                ChatTool chatTool;
                if (string.IsNullOrEmpty(tool.ParametersJsonSchema))
                {
                    chatTool = ChatTool.CreateFunctionTool(tool.Name, tool.Description);
                }
                else
                {
                    chatTool = ChatTool.CreateFunctionTool(
                        tool.Name,
                        tool.Description,
                        BinaryData.FromString(tool.ParametersJsonSchema));
                }

                chatOptions.Tools.Add(chatTool);
            }
        }

        return chatOptions;
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
                    Name = tc.FunctionName,
                    Arguments = tc.FunctionArguments?.ToString() ?? ""
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
}

#pragma warning restore OPENAI001
