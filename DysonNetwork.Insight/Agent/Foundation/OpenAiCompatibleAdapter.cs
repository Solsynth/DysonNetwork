namespace DysonNetwork.Insight.Agent.Foundation;

using System.ClientModel;
using System.Net.Http.Headers;
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
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly Uri _endpoint;
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
        _apiKey = apiKey;
        _modelId = modelId;
        _embeddingModelId = embeddingModelId;
        _toolExecutor = toolExecutor;
        _logger = logger;

        var options = new OpenAIClientOptions();
        if (!string.IsNullOrEmpty(baseUrl))
        {
            options.Endpoint = new Uri(baseUrl);
        }
        else if (RequiresReasoningReplay())
        {
            options.Endpoint = new Uri("https://api.deepseek.com/v1");
        }

        var client = new OpenAIClient(new ApiKeyCredential(apiKey), options);
        _chatClient = client.GetChatClient(modelId);
        _endpoint = options.Endpoint ?? new Uri("https://api.openai.com/v1");
        _httpClient = new HttpClient
        {
            BaseAddress = _endpoint
        };

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
        if (RequiresReasoningReplay())
        {
            return await CompleteChatWithReasoningReplayAsync(conversation, options, cancellationToken);
        }

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
        if (RequiresReasoningReplay())
        {
            await foreach (var evt in CompleteChatStreamingWithReasoningReplayAsync(conversation, options, cancellationToken))
            {
                yield return evt;
            }

            yield break;
        }

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

    private async Task<AgentChatResponse> CompleteChatWithReasoningReplayAsync(
        AgentConversation conversation,
        AgentExecutionOptions? options,
        CancellationToken cancellationToken)
    {
        var maxRounds = options?.MaxToolRounds ?? 10;
        var round = 0;
        var currentConversation = new AgentConversation(conversation.Messages.ToList())
        {
            Tools = conversation.Tools
        };

        while (round < maxRounds)
        {
            round++;
            var response = await SendRawChatCompletionAsync(currentConversation, options, false, cancellationToken);

            if (response.ToolCalls == null || response.ToolCalls.Count == 0 || response.FinishReason != AgentFinishReason.ToolCalls)
            {
                return response;
            }

            currentConversation.Messages.Add(new AgentMessage
            {
                Role = AgentMessageRole.Assistant,
                Content = response.Content,
                ReasoningContent = response.Reasoning,
                ToolCalls = response.ToolCalls
            });

            if (_toolExecutor == null)
            {
                return response;
            }

            foreach (var toolCall in response.ToolCalls)
            {
                toolCall.Arguments = NormalizeToolArguments(toolCall.Arguments);
                var result = await _toolExecutor.ExecuteToolAsync(toolCall, cancellationToken);
                currentConversation.Messages.Add(AgentMessage.FromToolResult(toolCall.Id, result.Result, result.IsError));
            }
        }

        _logger?.LogWarning("Max tool rounds ({MaxRounds}) reached in raw streaming chat", maxRounds);
        return new AgentChatResponse { Content = "", FinishReason = AgentFinishReason.Length };
    }

    private async IAsyncEnumerable<AgentStreamEvent> CompleteChatStreamingWithReasoningReplayAsync(
        AgentConversation conversation,
        AgentExecutionOptions? options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var maxRounds = options?.MaxToolRounds ?? 10;
        var round = 0;
        var currentConversation = new AgentConversation(conversation.Messages.ToList())
        {
            Tools = conversation.Tools
        };

        while (round < maxRounds)
        {
            round++;
            var textBuilder = new StringBuilder();
            var reasoningBuilder = new StringBuilder();
            var toolCalls = new List<StreamingToolCallAccumulator>();
            var hasToolCalls = false;
            AgentFinishReason finishReason = AgentFinishReason.Stop;
            int? inputTokens = null;
            int? outputTokens = null;

            await foreach (var update in StreamRawChatCompletionAsync(currentConversation, options, cancellationToken))
            {
                switch (update)
                {
                    case RawStreamUpdate.TextDelta textDelta:
                        textBuilder.Append(textDelta.Delta);
                        yield return new AgentStreamEvent.TextDelta(textDelta.Delta);
                        break;

                    case RawStreamUpdate.ReasoningDelta reasoningDelta:
                        reasoningBuilder.Append(reasoningDelta.Delta);
                        yield return new AgentStreamEvent.ReasoningDelta(reasoningDelta.Delta);
                        break;

                    case RawStreamUpdate.ToolCallDelta toolDelta:
                        hasToolCalls = true;
                        var toolCall = toolCalls.FirstOrDefault(t => t.Index == toolDelta.Index);
                        if (toolCall == null)
                        {
                            toolCall = new StreamingToolCallAccumulator
                            {
                                Index = toolDelta.Index
                            };
                            toolCalls.Add(toolCall);
                        }

                        if (!string.IsNullOrWhiteSpace(toolDelta.Id))
                        {
                            toolCall.Id = toolDelta.Id!;
                        }

                        if (!string.IsNullOrWhiteSpace(toolDelta.Name))
                        {
                            toolCall.Name = toolDelta.Name!;
                        }

                        if (!toolCall.Started &&
                            !string.IsNullOrWhiteSpace(toolCall.Id) &&
                            !string.IsNullOrWhiteSpace(toolCall.Name))
                        {
                            toolCall.Started = true;
                            yield return new AgentStreamEvent.ToolCallStarted(toolCall.Id, toolCall.Name);
                        }

                        if (!string.IsNullOrEmpty(toolDelta.ArgumentsDelta))
                        {
                            toolCall.Arguments.Append(toolDelta.ArgumentsDelta);
                            if (toolCall.Started)
                            {
                                yield return new AgentStreamEvent.ToolCallDelta(toolCall.Id, toolCall.Name, toolDelta.ArgumentsDelta);
                            }
                        }

                        break;

                    case RawStreamUpdate.Completed completed:
                        finishReason = completed.FinishReason;
                        inputTokens = completed.InputTokens;
                        outputTokens = completed.OutputTokens;
                        break;
                }
            }

            if (!hasToolCalls || toolCalls.Count == 0)
            {
                yield return new AgentStreamEvent.Completed(finishReason, inputTokens, outputTokens);
                yield break;
            }

            var finalizedToolCalls = toolCalls
                .Select(toolCall =>
                {
                    var normalizedArguments = NormalizeToolArguments(toolCall.Arguments.ToString());
                    return new AgentToolCall
                    {
                        Id = toolCall.Id,
                        Name = toolCall.Name,
                        Arguments = normalizedArguments
                    };
                })
                .ToList();

            foreach (var toolCall in finalizedToolCalls)
            {
                yield return new AgentStreamEvent.ToolCallCompleted(toolCall.Id, toolCall.Name, toolCall.Arguments);
            }

            currentConversation.Messages.Add(new AgentMessage
            {
                Role = AgentMessageRole.Assistant,
                Content = textBuilder.ToString(),
                ReasoningContent = reasoningBuilder.Length > 0 ? reasoningBuilder.ToString() : null,
                ToolCalls = finalizedToolCalls
            });

            if (options?.AutoInvokeTools == true && _toolExecutor != null)
            {
                foreach (var toolCall in finalizedToolCalls)
                {
                    var result = await _toolExecutor.ExecuteToolAsync(toolCall, cancellationToken);
                    yield return new AgentStreamEvent.ToolResultReady(toolCall.Id, toolCall.Name, result.Result, result.IsError);
                    currentConversation.Messages.Add(AgentMessage.FromToolResult(toolCall.Id, result.Result, result.IsError));
                }
            }
            else
            {
                yield return new AgentStreamEvent.Completed(AgentFinishReason.ToolCalls, inputTokens, outputTokens);
                yield break;
            }
        }

        _logger?.LogWarning("Max tool rounds ({MaxRounds}) reached in raw streaming chat", maxRounds);
        yield return new AgentStreamEvent.Completed(AgentFinishReason.Length, null, null);
    }

    private async Task<AgentChatResponse> SendRawChatCompletionAsync(
        AgentConversation conversation,
        AgentExecutionOptions? options,
        bool stream,
        CancellationToken cancellationToken)
    {
        using var request = BuildRawChatRequestMessage(conversation, options, stream);
        using var response = await _httpClient.SendAsync(
            request,
            stream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead,
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {body}");
        }

        using var document = JsonDocument.Parse(body);
        return ParseRawCompletionResponse(document.RootElement);
    }

    private async IAsyncEnumerable<RawStreamUpdate> StreamRawChatCompletionAsync(
        AgentConversation conversation,
        AgentExecutionOptions? options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var request = BuildRawChatRequestMessage(conversation, options, true);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {errorBody}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        string? line;
        AgentFinishReason finishReason = AgentFinishReason.Stop;
        int? inputTokens = null;
        int? outputTokens = null;

        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var payload = line["data:".Length..].Trim();
            if (payload == "[DONE]")
            {
                break;
            }

            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (TryGetUsage(root, out var usageInputTokens, out var usageOutputTokens))
            {
                inputTokens = usageInputTokens;
                outputTokens = usageOutputTokens;
            }

            if (!root.TryGetProperty("choices", out var choicesElement) || choicesElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var choice in choicesElement.EnumerateArray())
            {
                if (choice.TryGetProperty("delta", out var deltaElement))
                {
                    foreach (var textDelta in ReadContentDeltas(deltaElement, "content"))
                    {
                        yield return new RawStreamUpdate.TextDelta(textDelta);
                    }

                    foreach (var reasoningDelta in ReadContentDeltas(deltaElement, "reasoning_content"))
                    {
                        yield return new RawStreamUpdate.ReasoningDelta(reasoningDelta);
                    }

                    if (deltaElement.TryGetProperty("tool_calls", out var toolCallsElement) &&
                        toolCallsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var toolCallElement in toolCallsElement.EnumerateArray())
                        {
                            var index = toolCallElement.TryGetProperty("index", out var indexElement) && indexElement.TryGetInt32(out var parsedIndex)
                                ? parsedIndex
                                : 0;
                            var id = toolCallElement.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
                            string? name = null;
                            string? argumentsDelta = null;

                            if (toolCallElement.TryGetProperty("function", out var functionElement))
                            {
                                if (functionElement.TryGetProperty("name", out var functionNameElement))
                                {
                                    name = DenormalizeToolNameFromProvider(functionNameElement.GetString() ?? "");
                                }

                                if (functionElement.TryGetProperty("arguments", out var argumentsElement))
                                {
                                    argumentsDelta = argumentsElement.GetString();
                                }
                            }

                            yield return new RawStreamUpdate.ToolCallDelta(index, id, name, argumentsDelta);
                        }
                    }
                }

                if (choice.TryGetProperty("finish_reason", out var finishReasonElement) &&
                    finishReasonElement.ValueKind == JsonValueKind.String)
                {
                    finishReason = ParseRawFinishReason(finishReasonElement.GetString());
                }
            }
        }

        yield return new RawStreamUpdate.Completed(finishReason, inputTokens, outputTokens);
    }

    private HttpRequestMessage BuildRawChatRequestMessage(
        AgentConversation conversation,
        AgentExecutionOptions? options,
        bool stream)
    {
        var payload = BuildRawChatRequestPayload(conversation, options, stream);
        var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        if (stream)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        }

        return request;
    }

    private Dictionary<string, object?> BuildRawChatRequestPayload(
        AgentConversation conversation,
        AgentExecutionOptions? options,
        bool stream)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = _modelId,
            ["messages"] = BuildRawMessages(conversation.Messages),
            ["stream"] = stream
        };

        if (options?.Temperature.HasValue == true)
        {
            payload["temperature"] = options.Temperature.Value;
        }

        if (options?.MaxTokens.HasValue == true)
        {
            payload["max_tokens"] = options.MaxTokens.Value;
        }

        if (options?.EnableTools == true && conversation.Tools != null && conversation.Tools.Count > 0)
        {
            payload["tools"] = BuildRawTools(conversation.Tools);
            payload["tool_choice"] = "auto";
        }

        if (options?.AdditionalParameters != null)
        {
            foreach (var pair in options.AdditionalParameters)
            {
                payload[pair.Key] = pair.Value;
            }
        }

        return payload;
    }

    private List<object> BuildRawMessages(IEnumerable<AgentMessage> messages)
    {
        var result = new List<object>();

        foreach (var message in messages)
        {
            switch (message.Role)
            {
                case AgentMessageRole.System:
                    result.Add(new Dictionary<string, object?>
                    {
                        ["role"] = "system",
                        ["content"] = message.Content ?? ""
                    });
                    break;

                case AgentMessageRole.User:
                    result.Add(new Dictionary<string, object?>
                    {
                        ["role"] = "user",
                        ["content"] = BuildRawUserContent(message)
                    });
                    break;

                case AgentMessageRole.Assistant:
                    var assistantMessage = new Dictionary<string, object?>
                    {
                        ["role"] = "assistant"
                    };

                    if (!string.IsNullOrWhiteSpace(message.Content) || message.ToolCalls == null || message.ToolCalls.Count == 0)
                    {
                        assistantMessage["content"] = message.Content ?? "";
                    }
                    else
                    {
                        assistantMessage["content"] = null;
                    }

                    if (!string.IsNullOrWhiteSpace(message.ReasoningContent))
                    {
                        assistantMessage["reasoning_content"] = message.ReasoningContent;
                    }

                    if (message.ToolCalls != null && message.ToolCalls.Count > 0)
                    {
                        assistantMessage["tool_calls"] = message.ToolCalls.Select(toolCall => new Dictionary<string, object?>
                        {
                            ["id"] = toolCall.Id,
                            ["type"] = "function",
                            ["function"] = new Dictionary<string, object?>
                            {
                                ["name"] = NormalizeToolNameForProvider(toolCall.Name),
                                ["arguments"] = NormalizeToolArguments(toolCall.Arguments)
                            }
                        }).ToList();
                    }

                    result.Add(assistantMessage);
                    break;

                case AgentMessageRole.Tool:
                    result.Add(new Dictionary<string, object?>
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = message.ToolCallId ?? "",
                        ["content"] = message.ToolResultContent ?? ""
                    });
                    break;
            }
        }

        return result;
    }

    private object BuildRawUserContent(AgentMessage message)
    {
        if (message.ContentParts == null || message.ContentParts.Count == 0)
        {
            return message.Content ?? "";
        }

        return message.ContentParts.Select(part => part.Type switch
        {
            AgentContentPartType.Text => new Dictionary<string, object?>
            {
                ["type"] = "text",
                ["text"] = part.Text ?? ""
            },
            AgentContentPartType.ImageUrl => new Dictionary<string, object?>
            {
                ["type"] = "image_url",
                ["image_url"] = new Dictionary<string, object?>
                {
                    ["url"] = part.ImageUrl
                }
            },
            AgentContentPartType.ImageData => new Dictionary<string, object?>
            {
                ["type"] = "image_url",
                ["image_url"] = new Dictionary<string, object?>
                {
                    ["url"] = BuildDataUrl(part.ImageData, part.ImageMediaType)
                }
            },
            AgentContentPartType.FileUrl => new Dictionary<string, object?>
            {
                ["type"] = "text",
                ["text"] = $"[Attached File URL] {part.FileName ?? "file"} ({part.FileMediaType ?? "unknown"}) {part.FileUrl}"
            },
            AgentContentPartType.FileData => new Dictionary<string, object?>
            {
                ["type"] = "text",
                ["text"] = $"[Attached File] {part.FileName ?? "file"} ({part.FileMediaType ?? "unknown"})"
            },
            _ => new Dictionary<string, object?>
            {
                ["type"] = "text",
                ["text"] = part.Text ?? ""
            }
        }).ToList<object>();
    }

    private List<object> BuildRawTools(IEnumerable<AgentToolDefinition> tools)
    {
        return tools.Select(tool =>
        {
            JsonElement? parameters = null;
            if (!string.IsNullOrWhiteSpace(tool.ParametersJsonSchema))
            {
                parameters = JsonDocument.Parse(tool.ParametersJsonSchema).RootElement.Clone();
            }

            return (object)new Dictionary<string, object?>
            {
                ["type"] = "function",
                ["function"] = new Dictionary<string, object?>
                {
                    ["name"] = NormalizeToolNameForProvider(tool.Name),
                    ["description"] = tool.Description,
                    ["parameters"] = parameters
                }
            };
        }).ToList();
    }

    private AgentChatResponse ParseRawCompletionResponse(JsonElement root)
    {
        var response = new AgentChatResponse();

        if (TryGetUsage(root, out var inputTokens, out var outputTokens))
        {
            response.InputTokens = inputTokens;
            response.OutputTokens = outputTokens;
        }

        if (!root.TryGetProperty("choices", out var choicesElement) || choicesElement.ValueKind != JsonValueKind.Array || choicesElement.GetArrayLength() == 0)
        {
            return response;
        }

        var firstChoice = choicesElement[0];
        if (firstChoice.TryGetProperty("finish_reason", out var finishReasonElement))
        {
            response.FinishReason = ParseRawFinishReason(finishReasonElement.GetString());
        }

        if (!firstChoice.TryGetProperty("message", out var messageElement))
        {
            return response;
        }

        response.Content = ReadMessageContent(messageElement, "content");
        response.Reasoning = ReadMessageContent(messageElement, "reasoning_content");

        if (messageElement.TryGetProperty("tool_calls", out var toolCallsElement) && toolCallsElement.ValueKind == JsonValueKind.Array)
        {
            response.ToolCalls = toolCallsElement.EnumerateArray()
                .Select(ParseRawToolCall)
                .Where(toolCall => toolCall != null)
                .Cast<AgentToolCall>()
                .ToList();
        }

        return response;
    }

    private static AgentToolCall? ParseRawToolCall(JsonElement toolCallElement)
    {
        if (!toolCallElement.TryGetProperty("function", out var functionElement))
        {
            return null;
        }

        var arguments = functionElement.TryGetProperty("arguments", out var argumentsElement)
            ? argumentsElement.GetString()
            : null;
        var name = functionElement.TryGetProperty("name", out var nameElement)
            ? nameElement.GetString()
            : null;

        return new AgentToolCall
        {
            Id = toolCallElement.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? "" : "",
            Name = DenormalizeToolNameFromProvider(name ?? ""),
            Arguments = NormalizeToolArguments(arguments)
        };
    }

    private static IEnumerable<string> ReadContentDeltas(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var propertyValue))
        {
            yield break;
        }

        if (propertyValue.ValueKind == JsonValueKind.String)
        {
            var value = propertyValue.GetString();
            if (!string.IsNullOrEmpty(value))
            {
                yield return value;
            }

            yield break;
        }

        if (propertyValue.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in propertyValue.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var value = item.GetString();
                if (!string.IsNullOrEmpty(value))
                {
                    yield return value;
                }

                continue;
            }

            if (item.ValueKind == JsonValueKind.Object &&
                item.TryGetProperty("text", out var textElement) &&
                textElement.ValueKind == JsonValueKind.String)
            {
                var value = textElement.GetString();
                if (!string.IsNullOrEmpty(value))
                {
                    yield return value;
                }
            }
        }
    }

    private static string ReadMessageContent(JsonElement element, string propertyName)
    {
        return string.Concat(ReadContentDeltas(element, propertyName));
    }

    private static bool TryGetUsage(JsonElement root, out int? inputTokens, out int? outputTokens)
    {
        inputTokens = null;
        outputTokens = null;

        if (!root.TryGetProperty("usage", out var usageElement) || usageElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (usageElement.TryGetProperty("prompt_tokens", out var promptTokensElement) &&
            promptTokensElement.TryGetInt32(out var promptTokens))
        {
            inputTokens = promptTokens;
        }

        if (usageElement.TryGetProperty("completion_tokens", out var completionTokensElement) &&
            completionTokensElement.TryGetInt32(out var completionTokens))
        {
            outputTokens = completionTokens;
        }

        return inputTokens.HasValue || outputTokens.HasValue;
    }

    private static AgentFinishReason ParseRawFinishReason(string? finishReason) => finishReason?.ToLowerInvariant() switch
    {
        "stop" => AgentFinishReason.Stop,
        "tool_calls" => AgentFinishReason.ToolCalls,
        "length" => AgentFinishReason.Length,
        "content_filter" => AgentFinishReason.ContentFilter,
        _ => AgentFinishReason.Unknown
    };

    private static string? BuildDataUrl(byte[]? bytes, string? mediaType)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return null;
        }

        return $"data:{mediaType ?? "application/octet-stream"};base64,{Convert.ToBase64String(bytes)}";
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
            if (RequiresReasoningReplay())
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

    private bool RequiresReasoningReplay()
    {
        return ProviderId.StartsWith("deepseek", StringComparison.OrdinalIgnoreCase);
    }

    private abstract record RawStreamUpdate
    {
        public sealed record TextDelta(string Delta) : RawStreamUpdate;
        public sealed record ReasoningDelta(string Delta) : RawStreamUpdate;
        public sealed record ToolCallDelta(int Index, string? Id, string? Name, string? ArgumentsDelta) : RawStreamUpdate;
        public sealed record Completed(AgentFinishReason FinishReason, int? InputTokens, int? OutputTokens) : RawStreamUpdate;
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
