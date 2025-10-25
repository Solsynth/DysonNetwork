using DysonNetwork.Shared.Proto;
using Microsoft.SemanticKernel;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace DysonNetwork.Insight.Thinking;

public class ThinkingProvider
{
    private readonly Kernel _kernel;
    private readonly PostService.PostServiceClient _postClient;
    private readonly AccountService.AccountServiceClient _accountClient;

    public Kernel Kernel => _kernel;
    public string? ModelProviderType { get; private set; }
    public string? ModelDefault { get; private set; }

    public ThinkingProvider(
        IConfiguration configuration,
        PostService.PostServiceClient postClient,
        AccountService.AccountServiceClient accountClient
    )
    {
        _postClient = postClient;
        _accountClient = accountClient;

        _kernel = InitializeThinkingProvider(configuration);
        InitializeHelperFunctions();
    }

    private Kernel InitializeThinkingProvider(IConfiguration configuration)
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

        return builder.Build();
    }

    private void InitializeHelperFunctions()
    {
        // Add Solar Network tools plugin
        _kernel.ImportPluginFromFunctions("helper_functions", [
            KernelFunctionFactory.CreateFromMethod(async (string userId) =>
            {
                var request = new GetAccountRequest { Id = userId };
                var response = await _accountClient.GetAccountAsync(request);
                return JsonSerializer.Serialize(response, GrpcTypeHelper.SerializerOptions);
            }, "get_user_profile", "Get a user profile from the Solar Network."),
            KernelFunctionFactory.CreateFromMethod(async (string postId) =>
            {
                var request = new GetPostRequest { Id = postId };
                var response = await _postClient.GetPostAsync(request);
                return JsonSerializer.Serialize(response, GrpcTypeHelper.SerializerOptions);
            }, "get_post", "Get a single post by ID from the Solar Network."),
            KernelFunctionFactory.CreateFromMethod(async (string query) =>
            {
                var request = new SearchPostsRequest { Query = query, PageSize = 10 };
                var response = await _postClient.SearchPostsAsync(request);
                return JsonSerializer.Serialize(response.Posts, GrpcTypeHelper.SerializerOptions);
            }, "search_posts", "Search posts by query from the Solar Network."),
            KernelFunctionFactory.CreateFromMethod(async () =>
            {
                var request = new ListPostsRequest { PageSize = 10 };
                var response = await _postClient.ListPostsAsync(request);
                return JsonSerializer.Serialize(response.Posts, GrpcTypeHelper.SerializerOptions);
            }, "get_recent_posts", "Get recent posts from the Solar Network.")
        ]);
    }
}
