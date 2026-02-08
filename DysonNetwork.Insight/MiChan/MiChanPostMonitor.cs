#pragma warning disable SKEXP0050
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;
using DysonNetwork.Insight.MiChan.Plugins;
using DysonNetwork.Shared.EventBus;
using DysonNetwork.Shared.Models;
using Microsoft.SemanticKernel;
using NATS.Client.Core;

namespace DysonNetwork.Insight.MiChan;

public class MiChanPostMonitor : IDisposable
{
    private readonly MiChanConfig _config;
    private readonly ILogger<MiChanPostMonitor> _logger;
    private readonly SolarNetworkApiClient _apiClient;
    private readonly MiChanMemoryService _memoryService;
    private readonly MiChanKernelProvider _kernelProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly INatsConnection _nats;
    private readonly IEventBus _eventBus;
    private Kernel? _kernel;

    private readonly Regex _mentionRegex;
    private readonly CancellationTokenSource _cts = new();
    private Task? _monitoringTask;

    public MiChanPostMonitor(
        MiChanConfig config,
        ILogger<MiChanPostMonitor> logger,
        SolarNetworkApiClient apiClient,
        MiChanMemoryService memoryService,
        MiChanKernelProvider kernelProvider,
        IServiceProvider serviceProvider,
        INatsConnection nats,
        IEventBus eventBus)
    {
        _config = config;
        _logger = logger;
        _apiClient = apiClient;
        _memoryService = memoryService;
        _kernelProvider = kernelProvider;
        _serviceProvider = serviceProvider;
        _nats = nats;
        _eventBus = eventBus;
        
        // Create regex pattern for @michan mentions (case insensitive)
        _mentionRegex = new Regex($"@{Regex.Escape(config.BotAccountId)}\\b|@michan\\b", RegexOptions.IgnoreCase);
    }

    [Experimental("SKEXP0050")]
    public async Task InitializeAsync()
    {
        _kernel = _kernelProvider.GetKernel();
        
        // Register plugins
        var postPlugin = _serviceProvider.GetRequiredService<PostPlugin>();
        var accountPlugin = _serviceProvider.GetRequiredService<AccountPlugin>();

        _kernel.Plugins.AddFromObject(postPlugin, "post");
        _kernel.Plugins.AddFromObject(accountPlugin, "account");

        _logger.LogInformation("MiChan post monitor initialized");
    }

    public void StartMonitoring()
    {
        if (!_config.PostMonitoring.Enabled)
        {
            _logger.LogInformation("Post monitoring is disabled");
            return;
        }

        _monitoringTask = Task.Run(async () =>
        {
            try
            {
                await MonitorPostsAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in post monitoring loop");
            }
        });

        _logger.LogInformation("Started monitoring posts for mentions");
    }

    public void StopMonitoring()
    {
        _cts.Cancel();
        _monitoringTask?.Wait(TimeSpan.FromSeconds(5));
    }

    private async Task MonitorPostsAsync(CancellationToken cancellationToken)
    {
        // Subscribe to NATS subject for new posts
        await foreach (var msg in _nats.SubscribeAsync<byte[]>(_config.PostMonitoring.NatsSubject, cancellationToken: cancellationToken))
        {
            try
            {
                var post = JsonSerializer.Deserialize<SnPost>(msg.Data);
                if (post == null) continue;

                // Check if post mentions MiChan
                if (ContainsMention(post))
                {
                    _logger.LogInformation("Detected mention in post {PostId} from {Author}", 
                        post.Id, post.Publisher?.Name ?? "unknown");
                    
                    // Respond with priority
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await RespondToMentionAsync(post);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error responding to mention in post {PostId}", post.Id);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing post in monitor");
            }
        }
    }

    private bool ContainsMention(SnPost post)
    {
        // Check content for @michan or @bot_account_id
        if (_mentionRegex.IsMatch(post.Content ?? ""))
            return true;

        // Check if mentions list contains MiChan
        if (post.Mentions != null)
        {
            foreach (var mention in post.Mentions)
            {
                if (mention.Username?.Equals("michan", StringComparison.OrdinalIgnoreCase) == true ||
                    mention.Username?.Equals($"@{_config.BotAccountId}", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return true;
                }
            }
        }

        return false;
    }

    [Experimental("SKEXP0050")]
    private async Task RespondToMentionAsync(SnPost post)
    {
        using var timeoutCts = new CancellationTokenSource(
            TimeSpan.FromSeconds(_config.PostMonitoring.MentionResponseTimeoutSeconds));
        
        try
        {
            // Get post author info
            var author = post.Publisher?.Name ?? "someone";
            var content = post.Content ?? "";

            // Get conversation context
            var contextId = $"post_{post.Id}";
            var recentInteractions = await _memoryService.GetRecentInteractionsAsync(contextId, 5);

            // Build personality-aware response
            var personality = PersonalityLoader.LoadPersonality(_config.PersonalityFile, _config.Personality, _logger);
            var mood = _config.AutonomousBehavior.PersonalityMood;

            var prompt = $"""
                {personality}
                
                Current mood: {mood}
                
                Someone mentioned you in their post:
                Author: @{author}
                Content: "{content}"
                
                Write a friendly, natural reply to this mention. 
                Keep it brief (1-3 sentences). Be conversational and genuine.
                Do not use emojis.
                """;

            // Add context from recent interactions if available
            if (recentInteractions.Any())
            {
                prompt += "\n\nPrevious interactions with this post/thread:\n";
                foreach (var interaction in recentInteractions)
                {
                    if (interaction.Context.TryGetValue("response", out var response))
                    {
                        prompt += $"- {response}\n";
                    }
                }
            }

            var executionSettings = _kernelProvider.CreatePromptExecutionSettings();
            var result = await _kernel!.InvokePromptAsync(prompt, new KernelArguments(executionSettings));
            var replyContent = result.GetValue<string>()?.Trim();

            if (!string.IsNullOrEmpty(replyContent))
            {
                // Reply to the post
                var request = new
                {
                    content = replyContent,
                    reply_to = post.Id.ToString()
                };
                await _apiClient.PostAsync<object>("sphere", "/posts", request);

                // Store in memory
                await _memoryService.StoreInteractionAsync(
                    "mention_response",
                    contextId,
                    new Dictionary<string, object>
                    {
                        ["post_id"] = post.Id.ToString(),
                        ["post_content"] = content,
                        ["author"] = author,
                        ["response"] = replyContent
                    },
                    new Dictionary<string, object>
                    {
                        ["mood"] = mood,
                        ["timestamp"] = DateTime.UtcNow
                    }
                );

                _logger.LogInformation("Responded to mention in post {PostId}", post.Id);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Timeout responding to mention in post {PostId}", post.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating response to mention");
        }
    }

    public void Dispose()
    {
        StopMonitoring();
        _cts.Dispose();
    }
}

#pragma warning restore SKEXP0050
