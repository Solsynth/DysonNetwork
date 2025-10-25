using LangChain.Providers;
using LangChain.Providers.Ollama;

namespace DysonNetwork.Insight.Thinking;

public class ThinkingProvider
{
    public readonly Provider Provider;
    public readonly string? ModelProviderType;
    public readonly string? ModelDefault;

    public ThinkingProvider(IConfiguration configuration)
    {
        var cfg = configuration.GetSection("Thinking");
        ModelProviderType = cfg.GetValue<string>("Provider")?.ToLower();
        switch (ModelProviderType)
        {
            case "ollama":
                var endpoint = cfg.GetValue<string>("Endpoint");
                Provider = new OllamaProvider(endpoint ?? "http://localhost:11434/api");
                break;
            default:
                throw new IndexOutOfRangeException("Unknown thinking provider: " + ModelProviderType);
        }

        ModelDefault = cfg.GetValue<string>("Model");
    }

    public ChatModel GetModel(string? name = null)
    {
        return ModelProviderType switch
        {
            "ollama" => new OllamaChatModel((Provider as OllamaProvider)!, (name ?? ModelDefault)!),
            _ => throw new IndexOutOfRangeException("Unknown thinking provider: " + ModelProviderType),
        };
    }
}