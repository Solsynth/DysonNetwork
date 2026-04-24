namespace DysonNetwork.Insight.Agent.Foundation;

using DysonNetwork.Insight.Agent.Foundation.Models;

public interface IAgentProviderAdapter
{
    string ProviderId { get; }

    Task<AgentChatResponse> CompleteChatAsync(
        AgentConversation conversation,
        AgentExecutionOptions? options = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<AgentStreamEvent> CompleteChatStreamingAsync(
        AgentConversation conversation,
        AgentExecutionOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<AgentEmbeddingResponse> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentEmbeddingResponse>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);
}
