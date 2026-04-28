using System.Text;
using System.Text.Json;
using DysonNetwork.Insight.Agent.Foundation;
using DysonNetwork.Insight.Agent.Foundation.Providers;
using DysonNetwork.Insight.Agent.Models;
using DysonNetwork.Insight.MiChan;
using DysonNetwork.Insight.Thought.Memory;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;

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
    private readonly IConfiguration _configuration;
    private readonly ILogger<ThoughtProvider> _logger;
    private readonly AppDatabase _db;
    private readonly MemoryService _memoryService;
    private readonly MiChanConfig _miChanConfig;
    private readonly IServiceProvider _serviceProvider;
    private readonly FoundationChatStreamingService _streamingService;
    private readonly IMiChanFoundationProvider _miChanFoundationProvider;
    private readonly ModelRegistry _modelRegistry;

    private readonly Dictionary<string, ThoughtServiceModel> _serviceModels = new();
    private readonly ModelConfiguration _defaultModel;

    public ThoughtProvider(
        IConfiguration configuration,
        DyPostService.DyPostServiceClient postServiceClient,
        DyAccountService.DyAccountServiceClient accountServiceClient,
        DyPublisherService.DyPublisherServiceClient publisherClient,
        ILogger<ThoughtProvider> logger,
        AppDatabase db,
        MemoryService memoryService,
        IServiceProvider serviceProvider,
        MiChanConfig miChanConfig,
        FoundationChatStreamingService streamingService,
        IMiChanFoundationProvider miChanFoundationProvider,
        ModelRegistry modelRegistry)
    {
        _logger = logger;
        _postClient = postServiceClient;
        _accountClient = accountServiceClient;
        _publisherClient = publisherClient;
        _configuration = configuration;
        _db = db;
        _memoryService = memoryService;
        _serviceProvider = serviceProvider;
        _miChanConfig = miChanConfig;
        _streamingService = streamingService;
        _miChanFoundationProvider = miChanFoundationProvider;
        _modelRegistry = modelRegistry;

        var cfg = configuration.GetSection("Thinking");
        var defaultServiceId = cfg.GetValue<string>("DefaultService") ?? "deepseek-chat";
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
        }

        _defaultModel = new ModelConfiguration
        {
            ModelId = defaultServiceId,
            Temperature = cfg.GetValue<double?>("DefaultTemperature") ?? 0.7,
            EnableFunctions = true
        };
    }

    public string GetServiceId(string? serviceId = null)
    {
        return serviceId ?? _defaultModel.ModelId;
    }

    public IEnumerable<string> GetAvailableServices()
    {
        return _serviceModels.Keys;
    }

    public IEnumerable<ThoughtServiceModel> GetAvailableServicesInfo()
    {
        return _serviceModels.Values;
    }

    public ThoughtServiceModel? GetServiceInfo(string? serviceId)
    {
        serviceId ??= _defaultModel.ModelId;
        return _serviceModels.GetValueOrDefault(serviceId);
    }

    public string GetDefaultServiceId()
    {
        return _defaultModel.ModelId;
    }

    public ModelConfiguration GetDefaultModel() => _defaultModel;

    private record MemoryEntry(string Type, string Content, float Confidence);

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

            var thoughtIds = thoughts.Select(t => t.Id).ToList();
            if (thoughtIds.Count > 0)
            {
                var rows = await _db.ThinkingThoughtParts
                    .Where(p => thoughtIds.Contains(p.ThoughtId))
                    .OrderBy(p => p.PartIndex)
                    .ToListAsync(cancellationToken);

                var grouped = rows
                    .GroupBy(p => p.ThoughtId)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(row => new SnThinkingMessagePart
                        {
                            Type = row.Type,
                            Text = row.Text,
                            Metadata = row.Metadata,
                            Files = row.Files,
                            FunctionCall = row.FunctionCall,
                            FunctionResult = row.FunctionResult,
                            Reasoning = row.Reasoning
                        }).ToList());

                foreach (var thought in thoughts)
                {
                    if (grouped.TryGetValue(thought.Id, out var parts))
                    {
                        if (HasMeaningfulPartContent(parts))
                        {
                            thought.Parts = parts;
                        }
                    }
                }
            }

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

            var response = await _streamingService.CompletePromptAsync(
                _miChanFoundationProvider.GetChatAdapter(),
                conversationHistory,
                _miChanFoundationProvider.CreateExecutionOptions(0.7),
                cancellationToken);

            var resultText = response?.Trim() ?? "";

            _logger.LogDebug("Agent response for memory storage:\n{Response}", resultText);

            var memoriesStored = await ParseAndStoreMemoriesAsync(resultText, accountId, sequenceId, cancellationToken, maxRetries: 2);

            var summary = memoriesStored > 0
                ? $"Stored {memoriesStored} memory(ies). {resultText}"
                : resultText;

            _logger.LogInformation(
                "Memory summarization completed for sequence {SequenceId}. Stored {Count} memories. Response: {Response}",
                sequenceId, memoriesStored, resultText);

            return (true, summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error memorizing sequence {SequenceId}", sequenceId);
            return (false, $"Error: {ex.Message}");
        }
    }

    private static bool HasMeaningfulPartContent(List<SnThinkingMessagePart> parts)
    {
        if (parts.Count == 0)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (!string.IsNullOrWhiteSpace(part.Text) ||
                !string.IsNullOrWhiteSpace(part.Reasoning) ||
                part.FunctionCall != null ||
                part.FunctionResult != null ||
                (part.Metadata != null && part.Metadata.Count > 0) ||
                (part.Files != null && part.Files.Count > 0))
            {
                return true;
            }
        }

        return false;
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
                    var retryPrompt = $"JSON解析失败: {lastError}\n\n请修正以下JSON并返回有效的JSON数组格式：\n{jsonResponse}";

                    jsonResponse = await _streamingService.CompletePromptAsync(
                        _miChanFoundationProvider.GetChatAdapter(),
                        retryPrompt,
                        _miChanFoundationProvider.CreateExecutionOptions(0.7),
                        cancellationToken) ?? "";
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
