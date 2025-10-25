using Microsoft.SemanticKernel;

namespace DysonNetwork.Insight.Thinking;

public class ThinkingProvider
{
    public readonly Kernel Kernel;
    public readonly string? ModelProviderType;
    public readonly string? ModelDefault;

    public ThinkingProvider(IConfiguration configuration)
    {
        var cfg = configuration.GetSection("Thinking");
        ModelProviderType = cfg.GetValue<string>("Provider")?.ToLower();
        ModelDefault = cfg.GetValue<string>("Model");
        var endpoint = cfg.GetValue<string>("Endpoint");

        var builder = Kernel.CreateBuilder();

        switch (ModelProviderType)
        {
            case "ollama":
                builder.AddOllamaChatCompletion(ModelDefault!, new Uri(endpoint ?? "http://localhost:11434/api"));
                break;
            default:
                throw new IndexOutOfRangeException("Unknown thinking provider: " + ModelProviderType);
        }

        Kernel = builder.Build();

        // Add Solar Network tools plugin
        Kernel.ImportPluginFromFunctions("helper_functions", [
            KernelFunctionFactory.CreateFromMethod(async (string userId) =>
            {
                // MOCK: simulate fetching user profile
                await Task.Delay(100);
                return $"{{\"userId\":\"{userId}\",\"name\":\"MockUser\",\"bio\":\"Loves music and tech.\"}}";
            }, "get_user_profile", "Get a user profile from the Solar Network."),
            KernelFunctionFactory.CreateFromMethod(async (string topic) =>
            {
                // MOCK: simulate fetching recent posts
                await Task.Delay(200);
                return
                    $"[{{\"postId\":\"p1\",\"topic\":\"{topic}\",\"content\":\"Mock post content 1.\"}}, {{\"postId\":\"p2\",\"topic\":\"{topic}\",\"content\":\"Mock post content 2.\"}}]";
            }, "get_recent_posts", "Get recent posts from the Solar Network.")
        ]);
    }
}