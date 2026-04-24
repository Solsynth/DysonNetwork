namespace DysonNetwork.Insight.Agent.Foundation;

public class AgentEmbeddingResponse
{
    public IReadOnlyList<float> Embedding { get; init; } = Array.Empty<float>();
    public int Dimensions { get; init; }
    public int InputTokens { get; init; }
}
