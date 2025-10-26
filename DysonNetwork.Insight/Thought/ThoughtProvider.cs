using System.ClientModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Ollama;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI;
using PostType = DysonNetwork.Shared.Proto.PostType;
using Microsoft.SemanticKernel.Plugins.Web;
using Microsoft.SemanticKernel.Plugins.Web.Bing;
using Microsoft.SemanticKernel.Plugins.Web.Google;
using NodaTime.Serialization.Protobuf;
using NodaTime.Text;

namespace DysonNetwork.Insight.Thought;

public class ThoughtProvider
{
    private readonly PostService.PostServiceClient _postClient;
    private readonly AccountService.AccountServiceClient _accountClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ThoughtProvider> _logger;

    public Kernel Kernel { get; }

    private string? ModelProviderType { get; set; }
    private string? ModelDefault { get; set; }

    [Experimental("SKEXP0050")]
    public ThoughtProvider(
        IConfiguration configuration,
        PostService.PostServiceClient postServiceClient,
        AccountService.AccountServiceClient accountServiceClient,
        ILogger<ThoughtProvider> logger
    )
    {
        _logger = logger;
        _postClient = postServiceClient;
        _accountClient = accountServiceClient;
        _configuration = configuration;

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

    [Experimental("SKEXP0050")]
    private void InitializeHelperFunctions()
    {
        // Add Solar Network tools plugin
        Kernel.ImportPluginFromFunctions("solar_network", [
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
                }, "search_posts",
                "Search posts by query from the Solar Network. The input query is will be used to search with title, description and body content"),
            KernelFunctionFactory.CreateFromMethod(async (
                    string? orderBy = null,
                    string? afterIso = null,
                    string? beforeIso = null
                ) =>
                {
                    _logger.LogInformation("Begin building request to list post from sphere...");

                    var request = new ListPostsRequest
                    {
                        PageSize = 20,
                        OrderBy = orderBy,
                    };
                    if (!string.IsNullOrEmpty(afterIso))
                        try
                        {
                            request.After = InstantPattern.General.Parse(afterIso).Value.ToTimestamp();
                        }
                        catch (Exception)
                        {
                            _logger.LogWarning("Invalid afterIso format: {AfterIso}", afterIso);
                        }
                    if (!string.IsNullOrEmpty(beforeIso))
                        try
                        {
                            request.Before = InstantPattern.General.Parse(beforeIso).Value.ToTimestamp();
                        }
                        catch (Exception)
                        {
                            _logger.LogWarning("Invalid beforeIso format: {BeforeIso}", beforeIso);
                        }

                    _logger.LogInformation("Request built, {Request}", request);

                    var response = await _postClient.ListPostsAsync(request);

                    var data = response.Posts.Select(SnPost.FromProtoValue);
                    _logger.LogInformation("Sphere service returned posts: {Posts}", data);
                    return JsonSerializer.Serialize(data, GrpcTypeHelper.SerializerOptions);
                }, "list_posts",
                "Get posts from the Solar Network.\n" +
                "Parameters:\n" +
                "orderBy (optional, string: order by published date, accept asc or desc)\n" +
                "afterIso (optional, string: ISO date for posts after this date)\n" +
                "beforeIso (optional, string: ISO date for posts before this date)"
            )
        ]);

        // Add web search plugins if configured
        var bingApiKey = _configuration.GetValue<string>("Thinking:BingApiKey");
        if (!string.IsNullOrEmpty(bingApiKey))
        {
            var bingConnector = new BingConnector(bingApiKey);
            var bing = new WebSearchEnginePlugin(bingConnector);
            Kernel.ImportPluginFromObject(bing, "bing");
        }

        var googleApiKey = _configuration.GetValue<string>("Thinking:GoogleApiKey");
        var googleCx = _configuration.GetValue<string>("Thinking:GoogleCx");
        if (!string.IsNullOrEmpty(googleApiKey) && !string.IsNullOrEmpty(googleCx))
        {
            var googleConnector = new GoogleConnector(
                apiKey: googleApiKey,
                searchEngineId: googleCx);
            var google = new WebSearchEnginePlugin(googleConnector);
            Kernel.ImportPluginFromObject(google, "google");
        }
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