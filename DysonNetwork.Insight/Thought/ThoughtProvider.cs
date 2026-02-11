using System.ComponentModel.DataAnnotations;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using DysonNetwork.Insight.MiChan;
using DysonNetwork.Insight.MiChan.Plugins;
using DysonNetwork.Insight.Thought.Memory;
using DysonNetwork.Insight.Thought.Plugins;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Plugins.Web;
using Microsoft.SemanticKernel.Plugins.Web.Bing;
using Microsoft.SemanticKernel.Plugins.Web.Google;

namespace DysonNetwork.Insight.Thought;

public class ThoughtServiceModel
{
    public string ServiceId { get; set; } = null!;
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public double BillingMultiplier { get; set; }
    public int PerkLevel { get; set; }
}

public class ThoughtProvider
{
    private readonly PostService.PostServiceClient _postClient;
    private readonly AccountService.AccountServiceClient _accountClient;
    private readonly PublisherService.PublisherServiceClient _publisherClient;
    private readonly KernelFactory _kernelFactory;
    private readonly MiChanKernelProvider _miChanKernelProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ThoughtProvider> _logger;
    private readonly AppDatabase _db;
    private readonly MemoryService _memoryService;
    private readonly MiChanConfig _miChanConfig;
    private readonly IServiceProvider _serviceProvider;

    private readonly Dictionary<string, Kernel> _kernels = new();
    private readonly Dictionary<string, ThoughtServiceModel> _serviceModels = new();
    private readonly string _defaultServiceId;

    [Experimental("SKEXP0050")]
    public ThoughtProvider(
        KernelFactory kernelFactory,
        IConfiguration configuration,
        PostService.PostServiceClient postServiceClient,
        AccountService.AccountServiceClient accountServiceClient,
        PublisherService.PublisherServiceClient publisherClient,
        ILogger<ThoughtProvider> logger,
        AppDatabase db,
        MemoryService memoryService,
        MiChanKernelProvider miChanKernelProvider, IServiceProvider serviceProvider, MiChanConfig miChanConfig)
    {
        _logger = logger;
        _kernelFactory = kernelFactory;
        _postClient = postServiceClient;
        _accountClient = accountServiceClient;
        _publisherClient = publisherClient;
        _configuration = configuration;
        _db = db;
        _memoryService = memoryService;
        _miChanKernelProvider = miChanKernelProvider;
        _serviceProvider = serviceProvider;
        _miChanConfig = miChanConfig;

        var cfg = configuration.GetSection("Thinking");
        _defaultServiceId = cfg.GetValue<string>("DefaultService")!;
        var services = cfg.GetSection("Services").GetChildren();

        foreach (var service in services)
        {
            var serviceId = service.Key;
            var serviceModel = new ThoughtServiceModel
            {
                ServiceId = serviceId,
                Provider = service.GetValue<string>("Provider"),
                Model = service.GetValue<string>("Model"),
                BillingMultiplier = service.GetValue("BillingMultiplier", 1.0),
                PerkLevel = service.GetValue("PerkLevel", 0)
            };
            _serviceModels[serviceId] = serviceModel;

            var providerType = service.GetValue<string>("Provider")?.ToLower();
            if (providerType is null) continue;

            var kernel = InitializeThinkingService(serviceId);
            _kernels[serviceId] = kernel;
        }
    }

    [Experimental("SKEXP0050")]
    private Kernel InitializeThinkingService(string serviceId)
    {
        // Create base kernel using factory (no embeddings needed for thought provider)
        var kernel = _kernelFactory.CreateKernel(serviceId, addEmbeddings: false);

        // Add Thought-specific plugins (gRPC clients are already injected)
        kernel.Plugins.AddFromObject(new SnAccountKernelPlugin(_accountClient));
        kernel.Plugins.AddFromObject(new SnPostKernelPlugin(_postClient, _publisherClient));

        // Add helper functions (web search)
        InitializeHelperFunctions(kernel);

        return kernel;
    }

    [Experimental("SKEXP0050")]
    private void InitializeHelperFunctions(Kernel kernel)
    {
        // Add web search plugins if configured
        var bingApiKey = _configuration.GetValue<string>("Thinking:BingApiKey");
        if (!string.IsNullOrEmpty(bingApiKey))
        {
            var bingConnector = new BingConnector(bingApiKey);
            var bing = new WebSearchEnginePlugin(bingConnector);
            kernel.ImportPluginFromObject(bing, "bing");
        }

        var googleApiKey = _configuration.GetValue<string>("Thinking:GoogleApiKey");
        var googleCx = _configuration.GetValue<string>("Thinking:GoogleCx");
        if (!string.IsNullOrEmpty(googleApiKey) && !string.IsNullOrEmpty(googleCx))
        {
            var googleConnector = new GoogleConnector(
                apiKey: googleApiKey,
                searchEngineId: googleCx);
            var google = new WebSearchEnginePlugin(googleConnector);
            kernel.ImportPluginFromObject(google, "google");
        }
    }

    public Kernel? GetKernel(string? serviceId = null)
    {
        serviceId ??= _defaultServiceId;
        return _kernels.GetValueOrDefault(serviceId);
    }

    public string GetServiceId(string? serviceId = null)
    {
        return serviceId ?? _defaultServiceId;
    }

    public IEnumerable<string> GetAvailableServices()
    {
        return _kernels.Keys;
    }

    public IEnumerable<ThoughtServiceModel> GetAvailableServicesInfo()
    {
        return _serviceModels.Values;
    }

    public ThoughtServiceModel? GetServiceInfo(string? serviceId)
    {
        serviceId ??= _defaultServiceId;
        return _serviceModels.GetValueOrDefault(serviceId);
    }

    public string GetDefaultServiceId()
    {
        return _defaultServiceId;
    }

#pragma warning disable SKEXP0050
    public PromptExecutionSettings CreatePromptExecutionSettings(string? serviceId = null)
    {
        serviceId ??= _defaultServiceId;
        return _kernelFactory.CreatePromptExecutionSettings(serviceId);
    }
#pragma warning restore SKEXP0050

    [Experimental("SKEXP0050")]
    public async Task<(bool success, string summary)> MemorizeSequenceAsync(
        Guid sequenceId,
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting memory summarization for sequence {SequenceId}", sequenceId);

            var sequence = await _db.ThinkingSequences
                .FirstOrDefaultAsync(s => s.Id == sequenceId && s.AccountId == accountId, cancellationToken);

            if (sequence == null)
            {
                _logger.LogWarning("Sequence {SequenceId} not found for account {AccountId}", sequenceId, accountId);
                return (false, "Error: Sequence not found");
            }

            var thoughts = await _db.ThinkingThoughts
                .Where(t => t.SequenceId == sequenceId)
                .OrderBy(t => t.CreatedAt)
                .ToListAsync(cancellationToken);

            if (!thoughts.Any())
            {
                _logger.LogWarning("Sequence {SequenceId} has no thoughts to summarize", sequenceId);
                return (false, "Error: No thoughts in sequence");
            }

            var personality = PersonalityLoader.LoadPersonality(_miChanConfig.PersonalityFile, _miChanConfig.Personality, _logger);
            
            var conversationBuilder = new StringBuilder();
            conversationBuilder.AppendLine(personality);
            conversationBuilder.AppendLine($"以下是你与用户 {accountId} 对话历史。请阅读并判断有什么重要信息、关键事实或用户偏好值得记住。");
            conversationBuilder.AppendLine("如果有值得记住的信息，请使用 store_memory 工具保存记忆。");
            conversationBuilder.AppendLine();
            conversationBuilder.AppendLine("=== 对话历史 ===");
            conversationBuilder.AppendLine();

            foreach (var thought in thoughts)
            {
                var role = thought.Role switch
                {
                    ThinkingThoughtRole.User => "用户",
                    ThinkingThoughtRole.Assistant => "助手",
                    _ => thought.Role.ToString()
                };

                var content = ExtractThoughtContent(thought);
                if (string.IsNullOrEmpty(content)) continue;
                conversationBuilder.AppendLine($"[{role}]:");
                conversationBuilder.AppendLine(content);
                conversationBuilder.AppendLine();
            }

            var conversationHistory = conversationBuilder.ToString();

            var kernel = _miChanKernelProvider.GetKernel();

            // Register plugins (only if not already registered)
            var postPlugin = _serviceProvider.GetRequiredService<PostPlugin>();
            var accountPlugin = _serviceProvider.GetRequiredService<AccountPlugin>();
            var memoryPlugin = _serviceProvider.GetRequiredService<MemoryPlugin>();

            if (!kernel.Plugins.Contains("post"))
                kernel.Plugins.AddFromObject(postPlugin, "post");
            if (!kernel.Plugins.Contains("account"))
                kernel.Plugins.AddFromObject(accountPlugin, "account");
            if  (!kernel.Plugins.Contains("memory"))
                kernel.Plugins.AddFromObject(memoryPlugin, "memory");

            var settings = _miChanKernelProvider.CreatePromptExecutionSettings(0.7);

            var result = await kernel.InvokePromptAsync<string>(
                conversationHistory,
                new KernelArguments(settings),
                cancellationToken: cancellationToken
            );

            var response = result?.Trim() ?? "";

            _logger.LogInformation(
                "Memory summarization completed for sequence {SequenceId}. Response: {Response}",
                sequenceId, response);

            return (true, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error memorizing sequence {SequenceId}", sequenceId);
            return (false, $"Error: {ex.Message}");
        }
    }

    private static string ExtractThoughtContent(SnThinkingThought thought)
    {
        var content = new StringBuilder();
        foreach (var part in thought.Parts)
        {
            switch (part.Type)
            {
                case ThinkingMessagePartType.Text when !string.IsNullOrEmpty(part.Text):
                    content.AppendLine(part.Text);
                    break;
                case ThinkingMessagePartType.FunctionCall when part.FunctionCall != null:
                    content.AppendLine($"[功能调用: {part.FunctionCall.PluginName}.{part.FunctionCall.Name}]");
                    break;
                case ThinkingMessagePartType.FunctionResult when part.FunctionResult != null:
                    content.AppendLine($"[功能结果: {part.FunctionResult.FunctionName}]");
                    break;
            }
        }

        return content.ToString().Trim();
    }
}