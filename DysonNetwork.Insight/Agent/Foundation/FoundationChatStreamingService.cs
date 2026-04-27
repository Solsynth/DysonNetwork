namespace DysonNetwork.Insight.Agent.Foundation;

using System.Text;
using System.Text.Json;
using DysonNetwork.Insight.Agent.Foundation.Models;

public class FoundationChatStreamingService
{
    private readonly IAgentProviderRegistry _providerRegistry;
    private readonly IAgentToolExecutor _toolExecutor;
    private readonly ILogger<FoundationChatStreamingService> _logger;

    public FoundationChatStreamingService(
        IAgentProviderRegistry providerRegistry,
        IAgentToolExecutor toolExecutor,
        ILogger<FoundationChatStreamingService> logger)
    {
        _providerRegistry = providerRegistry;
        _toolExecutor = toolExecutor;
        _logger = logger;
    }

    public async IAsyncEnumerable<StreamingChatEvent> StreamChatAsync(
        IAgentProviderAdapter provider,
        AgentConversation conversation,
        AgentExecutionOptions options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var maxRounds = options.MaxToolRounds;
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
            var toolCalls = new List<AgentToolCall>();
            var hasToolCalls = false;

            var stream = provider.CompleteChatStreamingAsync(currentConversation, options, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);

            while (true)
            {
                AgentStreamEvent? evt = null;
                Exception? streamException = null;
                var hasNext = false;

                try
                {
                    hasNext = await stream.MoveNextAsync();
                    if (hasNext)
                    {
                        evt = stream.Current;
                    }
                }
                catch (Exception ex)
                {
                    streamException = ex;
                }

                if (streamException != null)
                {
                    await stream.DisposeAsync();
                    _logger.LogError(streamException, "Error during streaming chat");
                    yield return new StreamingChatEvent.Error(streamException.Message);
                    yield break;
                }

                if (!hasNext)
                {
                    break;
                }

                switch (evt)
                {
                    case AgentStreamEvent.TextDelta textDelta:
                        textBuilder.Append(textDelta.Delta);
                        yield return new StreamingChatEvent.Text(textDelta.Delta);
                        break;

                    case AgentStreamEvent.ReasoningDelta reasoningDelta:
                        reasoningBuilder.Append(reasoningDelta.Delta);
                        yield return new StreamingChatEvent.Reasoning(reasoningDelta.Delta);
                        break;

                    case AgentStreamEvent.ToolCallStarted toolStarted:
                        hasToolCalls = true;
                        toolCalls.Add(new AgentToolCall { Id = toolStarted.ToolCallId, Name = toolStarted.ToolName, Arguments = "" });
                        yield return new StreamingChatEvent.ToolCallStarted(toolStarted.ToolCallId, toolStarted.ToolName);
                        break;

                    case AgentStreamEvent.ToolCallDelta toolDelta:
                        var existingCall = toolCalls.FirstOrDefault(t => t.Id == toolDelta.ToolCallId);
                        if (existingCall != null)
                        {
                            existingCall.Arguments += toolDelta.ArgumentsDelta ?? "";
                        }
                        yield return new StreamingChatEvent.ToolCallDelta(toolDelta.ToolCallId, toolDelta.ToolName, toolDelta.ArgumentsDelta ?? "");
                        break;

                    case AgentStreamEvent.ToolCallCompleted toolCompleted:
                        var call = toolCalls.FirstOrDefault(t => t.Id == toolCompleted.ToolCallId);
                        if (call != null)
                        {
                            call.Arguments = toolCompleted.Arguments;
                        }
                        break;

                    case AgentStreamEvent.Completed completed:
                        if (completed.InputTokens.HasValue || completed.OutputTokens.HasValue)
                        {
                            yield return new StreamingChatEvent.TokenUsage(completed.InputTokens, completed.OutputTokens);
                        }
                        break;

                    case AgentStreamEvent.Error error:
                        await stream.DisposeAsync();
                        _logger.LogError(error.Exception, "Error during streaming chat");
                        yield return new StreamingChatEvent.Error(error.Exception.Message);
                        yield break;
                }
            }
            await stream.DisposeAsync();

            if (!hasToolCalls || toolCalls.Count == 0)
            {
                yield return new StreamingChatEvent.Finished(textBuilder.ToString(), reasoningBuilder.Length > 0 ? reasoningBuilder.ToString() : null);
                yield break;
            }

            var assistantMessage = new AgentMessage
            {
                Role = AgentMessageRole.Assistant,
                Content = textBuilder.ToString(),
                ReasoningContent = reasoningBuilder.Length > 0 ? reasoningBuilder.ToString() : null,
                ToolCalls = toolCalls
            };
            currentConversation.Messages.Add(assistantMessage);

            foreach (var toolCall in toolCalls)
            {
                toolCall.Arguments = string.IsNullOrWhiteSpace(toolCall.Arguments) ? "{}" : toolCall.Arguments;
                var result = await _toolExecutor.ExecuteToolAsync(toolCall, cancellationToken);
                
                yield return new StreamingChatEvent.ToolResult(toolCall.Id, toolCall.Name, result.Result, result.IsError);
                
                currentConversation.Messages.Add(AgentMessage.FromToolResult(toolCall.Id, result.Result, result.IsError));
            }

            yield return new StreamingChatEvent.ToolRoundCompleted(toolCalls.Count);

        }

        _logger.LogWarning("Max tool rounds ({MaxRounds}) reached", maxRounds);
        yield return new StreamingChatEvent.Finished("", null);
    }

    public async Task<AgentChatResponse> CompleteChatAsync(
        IAgentProviderAdapter provider,
        AgentConversation conversation,
        AgentExecutionOptions options,
        CancellationToken cancellationToken = default)
    {
        var maxRounds = options.MaxToolRounds;
        var round = 0;
        var currentConversation = new AgentConversation(conversation.Messages.ToList())
        {
            Tools = conversation.Tools
        };

        while (round < maxRounds)
        {
            round++;
            var response = await provider.CompleteChatAsync(currentConversation, options, cancellationToken);

            if (response.FinishReason != AgentFinishReason.ToolCalls || response.ToolCalls == null || response.ToolCalls.Count == 0)
            {
                return response;
            }

            var assistantMessage = new AgentMessage
            {
                Role = AgentMessageRole.Assistant,
                Content = response.Content,
                ReasoningContent = response.Reasoning,
                ToolCalls = response.ToolCalls
            };
            currentConversation.Messages.Add(assistantMessage);

            foreach (var toolCall in response.ToolCalls)
            {
                toolCall.Arguments = string.IsNullOrWhiteSpace(toolCall.Arguments) ? "{}" : toolCall.Arguments;
                var result = await _toolExecutor.ExecuteToolAsync(toolCall, cancellationToken);
                currentConversation.Messages.Add(AgentMessage.FromToolResult(toolCall.Id, result.Result, result.IsError));
            }
        }

        _logger.LogWarning("Max tool rounds ({MaxRounds}) reached in non-streaming chat", maxRounds);
        return new AgentChatResponse { Content = "", FinishReason = AgentFinishReason.Length };
    }

    public async Task<string> CompletePromptAsync(
        IAgentProviderAdapter provider,
        string prompt,
        AgentExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var conversation = new AgentConversation();
        conversation.AddSystemMessage(prompt);

        var effectiveOptions = options ?? new AgentExecutionOptions
        {
            EnableTools = false,
            AutoInvokeTools = false,
            MaxToolRounds = 1
        };

        var response = await provider.CompleteChatAsync(conversation, effectiveOptions, cancellationToken);
        return response.Content ?? "";
    }

    public async Task<string> CompletePromptWithToolsAsync(
        IAgentProviderAdapter provider,
        string prompt,
        IAgentToolRegistry toolRegistry,
        AgentExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var conversation = new AgentConversation
        {
            Tools = toolRegistry.GetAllDefinitions().ToList()
        };
        conversation.AddSystemMessage(prompt);

        var effectiveOptions = options ?? new AgentExecutionOptions
        {
            EnableTools = true,
            AutoInvokeTools = true,
            MaxToolRounds = 10
        };

        var response = await CompleteChatAsync(provider, conversation, effectiveOptions, cancellationToken);
        return response.Content ?? "";
    }

}

public abstract record StreamingChatEvent
{
    public record Text(string Delta) : StreamingChatEvent;
    public record Reasoning(string Delta) : StreamingChatEvent;
    public record ToolCallStarted(string Id, string Name) : StreamingChatEvent;
    public record ToolCallDelta(string Id, string Name, string? ArgumentsDelta) : StreamingChatEvent;
    public record ToolResult(string Id, string Name, string Result, bool IsError) : StreamingChatEvent;
    public record ToolRoundCompleted(int ToolCount) : StreamingChatEvent;
    public record TokenUsage(int? InputTokens, int? OutputTokens) : StreamingChatEvent;
    public record Finished(string? FinalText, string? FinalReasoning) : StreamingChatEvent;
    public record Error(string Message) : StreamingChatEvent;
}
