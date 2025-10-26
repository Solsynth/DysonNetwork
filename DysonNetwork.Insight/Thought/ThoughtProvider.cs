using System.ClientModel;
using System.Text.Json;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Ollama;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI;
using PostPinMode = DysonNetwork.Shared.Proto.PostPinMode;
using PostType = DysonNetwork.Shared.Proto.PostType;

namespace DysonNetwork.Insight.Thought;

public class ThoughtProvider
{
    private readonly PostService.PostServiceClient _postClient;
    private readonly AccountService.AccountServiceClient _accountClient;

    public Kernel Kernel { get; }

    private string? ModelProviderType { get; set; }
    private string? ModelDefault { get; set; }

    public ThoughtProvider(
        IConfiguration configuration,
        PostService.PostServiceClient postClient,
        AccountService.AccountServiceClient accountClient
    )
    {
        _postClient = postClient;
        _accountClient = accountClient;

        Kernel = InitializeThinkingProvider(configuration);
        InitializeHelperFunctions();
    }

    private Kernel InitializeThinkingProvider(IConfiguration configuration)
    {
        var cfg = configuration.GetSection("Thinking");
        ModelProviderType = cfg.GetValue<string>("Provider")?.ToLower();
        ModelDefault = cfg.GetValue<string>("Model");
        var endpoint = cfg.GetValue<string>("Endpoint");
        var apiKey = cfg.GetValue<string>("ApiKey");

        var builder = Kernel.CreateBuilder();

        switch (ModelProviderType)
        {
            case "ollama":
                builder.AddOllamaChatCompletion(ModelDefault!, new Uri(endpoint ?? "http://localhost:11434/api"));
                break;
            case "deepseek":
                var client = new OpenAIClient(
                    new ApiKeyCredential(apiKey!),
                    new OpenAIClientOptions { Endpoint = new Uri(endpoint ?? "https://api.deepseek.com/v1") }
                );
                builder.AddOpenAIChatCompletion(ModelDefault!, client);
                break;
            default:
                throw new IndexOutOfRangeException("Unknown thinking provider: " + ModelProviderType);
        }

        return builder.Build();
    }

    private void InitializeHelperFunctions()
    {
        // Add Solar Network tools plugin
        Kernel.ImportPluginFromFunctions("helper_functions", [
            KernelFunctionFactory.CreateFromMethod(async (string userId) =>
            {
                var request = new GetAccountRequest { Id = userId };
                var response = await _accountClient.GetAccountAsync(request);
                return JsonSerializer.Serialize(response, GrpcTypeHelper.SerializerOptions);
            }, "get_user", "Get a user profile from the Solar Network."),
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
            KernelFunctionFactory.CreateFromMethod(async (
                string? publisherId = null,
                string? realmId = null,
                int pageSize = 10,
                string? pageToken = null,
                string? orderBy = null,
                List<string>? categories = null,
                List<string>? tags = null,
                string? query = null,
                List<int>? types = null,
                string? afterIso = null,
                string? beforeIso = null,
                bool includeReplies = false,
                string? pinned = null,
                bool onlyMedia = false,
                bool shuffle = false
            ) =>
            {
                var request = new ListPostsRequest
                {
                    PublisherId = publisherId,
                    RealmId = realmId,
                    PageSize = pageSize,
                    PageToken = pageToken,
                    OrderBy = orderBy,
                    Query = query,
                    IncludeReplies = includeReplies,
                    Pinned = !string.IsNullOrEmpty(pinned) && int.TryParse(pinned, out int p) ? (PostPinMode)p : default,
                    OnlyMedia = onlyMedia,
                    Shuffle = shuffle
                };
                if (!string.IsNullOrEmpty(afterIso))
                {
                    request.After =
                        Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(DateTimeOffset.Parse(afterIso)
                            .ToUniversalTime());
                }

                if (!string.IsNullOrEmpty(beforeIso))
                {
                    request.Before =
                        Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(DateTimeOffset.Parse(beforeIso)
                            .ToUniversalTime());
                }

                if (categories != null) request.Categories.AddRange(categories);
                if (tags != null) request.Tags.AddRange(tags);
                if (types != null) request.Types_.AddRange(types.Select(t => (PostType)t));
                var response = await _postClient.ListPostsAsync(request);
                return JsonSerializer.Serialize(response.Posts.Select(SnPost.FromProtoValue), GrpcTypeHelper.SerializerOptions);
            }, "list_posts", "Get posts from the Solar Network with customizable filters.")
        ]);
    }

    public PromptExecutionSettings CreatePromptExecutionSettings()
    {
        switch (ModelProviderType)
        {
            case "ollama":
                return new OllamaPromptExecutionSettings
                {
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(
                        options: new FunctionChoiceBehaviorOptions
                        {
                            AllowParallelCalls = true, 
                            AllowConcurrentInvocation = true
                        })
                };
            case "deepseek":
                return new OpenAIPromptExecutionSettings
                {
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(
                        options: new FunctionChoiceBehaviorOptions
                        {
                            AllowParallelCalls = true,
                            AllowConcurrentInvocation = true
                        })
                };
            default:
                throw new InvalidOperationException("Unknown provider: " + ModelProviderType);
        }
    }
}
