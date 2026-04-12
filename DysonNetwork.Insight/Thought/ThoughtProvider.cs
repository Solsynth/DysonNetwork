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
    private readonly DyPostService.DyPostServiceClient _postClient;
    private readonly DyAccountService.DyAccountServiceClient _accountClient;
    private readonly DyPublisherService.DyPublisherServiceClient _publisherClient;
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
        DyPostService.DyPostServiceClient postServiceClient,
        DyAccountService.DyAccountServiceClient accountServiceClient,
        DyPublisherService.DyPublisherServiceClient publisherClient,
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
        // The plugin is specific for SN-chan
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

    private record MemoryEntry(string Type, string Content, float Confidence);

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
            conversationBuilder.AppendLine();
            conversationBuilder.AppendLine("请以JSON数组格式输出要保存的记忆：");
            conversationBuilder.AppendLine(@"[{""type"": ""类型"", ""content"": ""内容"", ""confidence"": 0.0-1.0}]");
            conversationBuilder.AppendLine("类型可以是：fact(事实)、user(用户偏好)、context(上下文)、summary(总结)");
            conversationBuilder.AppendLine("confidence表示记忆的可信度(0.0-1.0)，默认为0.7");
            conversationBuilder.AppendLine();
            conversationBuilder.AppendLine("示例：");
            conversationBuilder.AppendLine(@"[{""type"": ""fact"", ""content"": ""用户喜欢猫咪"", ""confidence"": 0.9}, {""type"": ""user"", ""content"": ""用户的工作是程序员"", ""confidence"": 0.8}]");
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

            // Register plugins using centralized extension method
            kernel.AddMiChanPlugins(_serviceProvider);

            var settings = _miChanKernelProvider.CreatePromptExecutionSettings(0.7);

            var result = await kernel.InvokePromptAsync<string>(
                conversationHistory,
                new KernelArguments(settings),
                cancellationToken: cancellationToken
            );

            var response = result?.Trim() ?? "";

            _logger.LogDebug("Agent response for memory storage:\n{Response}", response);

            var memoriesStored = await ParseAndStoreMemoriesAsync(response, accountId, sequenceId, cancellationToken, maxRetries: 2);

            var summary = memoriesStored > 0
                ? $"Stored {memoriesStored} memory(ies). {response}"
                : response;

            _logger.LogInformation(
                "Memory summarization completed for sequence {SequenceId}. Stored {Count} memories. Response: {Response}",
                sequenceId, memoriesStored, response);

            return (true, summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error memorizing sequence {SequenceId}", sequenceId);
            return (false, $"Error: {ex.Message}");
        }
    }

    private async Task<int> ParseAndStoreMemoriesAsync(
        string jsonResponse,
        Guid accountId,
        Guid sequenceId,
        CancellationToken cancellationToken,
        int maxRetries = 2)
    {
        var memoriesStored = 0;
        var attempt = 0;
        var lastError = "";

        while (attempt < maxRetries)
        {
            attempt++;
            try
            {
                var entries = JsonSerializer.Deserialize<List<MemoryEntry>>(jsonResponse, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (entries == null || entries.Count == 0)
                {
                    _logger.LogDebug("No memory entries found in response for sequence {SequenceId}", sequenceId);
                    return memoriesStored;
                }

                foreach (var entry in entries)
                {
                    if (string.IsNullOrEmpty(entry.Type) || string.IsNullOrEmpty(entry.Content))
                        continue;

                    var confidence = entry.Confidence > 0 ? entry.Confidence : 0.7f;

                    await _memoryService.StoreMemoryAsync(
                        type: entry.Type.ToLower(),
                        content: entry.Content,
                        confidence: confidence,
                        accountId: accountId,
                        hot: false);
                    memoriesStored++;
                    _logger.LogInformation("Stored memory from sequence {SequenceId}: type={Type}, content={Content}",
                        sequenceId, entry.Type, entry.Content[..Math.Min(entry.Content.Length, 100)]);
                }

                return memoriesStored;
            }
            catch (JsonException ex)
            {
                lastError = ex.Message;
                _logger.LogWarning(ex, "JSON parsing failed (attempt {Attempt}/{MaxRetries}) for sequence {SequenceId}: {Error}",
                    attempt, maxRetries, sequenceId, lastError);

                if (attempt < maxRetries)
                {
#pragma warning disable SKEXP0050
                    var kernel = _miChanKernelProvider.GetKernel();
#pragma warning restore SKEXP0050
                    kernel.AddMiChanPlugins(_serviceProvider);
                    var settings = _miChanKernelProvider.CreatePromptExecutionSettings(0.7);

                    var retryPrompt = $"JSON解析失败: {lastError}\n\n请修正以下JSON并返回有效的JSON数组格式：\n{jsonResponse}";

                    jsonResponse = await kernel.InvokePromptAsync<string>(
                        retryPrompt,
                        new KernelArguments(settings),
                        cancellationToken: cancellationToken) ?? "";
                }
            }
        }

        _logger.LogError("JSON parsing failed after {MaxRetries} attempts for sequence {SequenceId}. Last error: {Error}",
            maxRetries, sequenceId, lastError);

        return memoriesStored;
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
