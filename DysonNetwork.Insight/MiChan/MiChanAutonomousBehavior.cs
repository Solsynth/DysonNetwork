#pragma warning disable SKEXP0050
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using DysonNetwork.Insight.MiChan.Plugins;
using DysonNetwork.Shared.Models;
using Microsoft.SemanticKernel;

namespace DysonNetwork.Insight.MiChan;

public class MiChanAutonomousBehavior
{
    private readonly MiChanConfig _config;
    private readonly ILogger<MiChanAutonomousBehavior> _logger;
    private readonly SolarNetworkApiClient _apiClient;
    private readonly MiChanMemoryService _memoryService;
    private readonly MiChanKernelProvider _kernelProvider;
    private readonly IServiceProvider _serviceProvider;
    private Kernel? _kernel;

    private readonly Random _random = new();
    private DateTime _lastActionTime = DateTime.MinValue;
    private TimeSpan _nextInterval;
    private readonly HashSet<string> _processedPostIds = new();
    private readonly Regex _mentionRegex;

    public MiChanAutonomousBehavior(
        MiChanConfig config,
        ILogger<MiChanAutonomousBehavior> logger,
        SolarNetworkApiClient apiClient,
        MiChanMemoryService memoryService,
        MiChanKernelProvider kernelProvider,
        IServiceProvider serviceProvider)
    {
        _config = config;
        _logger = logger;
        _apiClient = apiClient;
        _memoryService = memoryService;
        _kernelProvider = kernelProvider;
        _serviceProvider = serviceProvider;
        _nextInterval = CalculateNextInterval();
        _mentionRegex = new Regex($"@{Regex.Escape(config.BotAccountId)}\\b|@michan\\b", RegexOptions.IgnoreCase);
    }

    [Experimental("SKEXP0050")]
    public async Task InitializeAsync()
    {
        _kernel = _kernelProvider.GetKernel();
        
        // Register plugins (only if not already registered)
        var chatPlugin = _serviceProvider.GetRequiredService<ChatPlugin>();
        var postPlugin = _serviceProvider.GetRequiredService<PostPlugin>();
        var accountPlugin = _serviceProvider.GetRequiredService<AccountPlugin>();

        if (!_kernel.Plugins.Contains("chat"))
            _kernel.Plugins.AddFromObject(chatPlugin, "chat");
        if (!_kernel.Plugins.Contains("post"))
            _kernel.Plugins.AddFromObject(postPlugin, "post");
        if (!_kernel.Plugins.Contains("account"))
            _kernel.Plugins.AddFromObject(accountPlugin, "account");

        _logger.LogInformation("MiChan autonomous behavior initialized");
    }

    /// <summary>
    /// Check if it's time for an autonomous action and execute if so
    /// </summary>
    public async Task<bool> TryExecuteAutonomousActionAsync()
    {
        if (!_config.AutonomousBehavior.Enabled)
            return false;

        if (DateTime.UtcNow - _lastActionTime < _nextInterval)
            return false;

        try
        {
            _logger.LogInformation("Executing autonomous action...");
            
            // Always check posts first for mentions and interesting content
            await CheckAndInteractWithPostsAsync();
            
            // Then possibly do additional actions
            var availableActions = _config.AutonomousBehavior.Actions;
            if (availableActions.Count > 0 && _random.Next(100) < 30) // 30% chance for extra action
            {
                var action = availableActions[_random.Next(availableActions.Count)];
                
                switch (action)
                {
                    case "create_post":
                        await CreateAutonomousPostAsync();
                        break;
                }
            }

            _lastActionTime = DateTime.UtcNow;
            _nextInterval = CalculateNextInterval();

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing autonomous action");
            return false;
        }
    }

    [Experimental("SKEXP0050")]
    private async Task CheckAndInteractWithPostsAsync()
    {
        _logger.LogInformation("Autonomous: Checking posts...");
        
        // Get recent posts
        var posts = await _apiClient.GetAsync<List<SnPost>>("sphere", "/posts?take=30");
        if (posts == null || posts.Count == 0)
            return;

        var personality = PersonalityLoader.LoadPersonality(_config.PersonalityFile, _config.Personality, _logger);
        var mood = _config.AutonomousBehavior.PersonalityMood;

        foreach (var post in posts.OrderByDescending(p => p.CreatedAt).Take(10))
        {
            // Skip already processed posts
            if (!_processedPostIds.Add(post.Id.ToString()))
                continue;

            // Keep hash set size manageable
            if (_processedPostIds.Count > 1000)
            {
                var toRemove = _processedPostIds.Take(500).ToList();
                foreach (var id in toRemove)
                    _processedPostIds.Remove(id);
            }

            // Check if mentioned
            var isMentioned = ContainsMention(post);
            
            // MiChan decides what to do with this post
            var decision = await DecidePostActionAsync(post, isMentioned, personality, mood);
            
            switch (decision.Action)
            {
                case "reply":
                    if (!string.IsNullOrEmpty(decision.Content))
                    {
                        await ReplyToPostAsync(post, decision.Content);
                    }
                    break;
                case "like":
                    await LikePostAsync(post);
                    break;
                case "ignore":
                default:
                    _logger.LogDebug("Autonomous: Ignoring post {PostId}", post.Id);
                    break;
            }

            // If mentioned, prioritize and respond
            if (isMentioned)
            {
                _logger.LogInformation("Autonomous: Detected mention in post {PostId}", post.Id);
                break; // Only handle one mention per cycle
            }
        }
    }

    private async Task<PostActionDecision> DecidePostActionAsync(SnPost post, bool isMentioned, string personality, string mood)
    {
        try
        {
            var author = post.Publisher?.Name ?? "someone";
            var content = post.Content ?? "";

            // If mentioned, always reply
            if (isMentioned)
            {
                var prompt = $@"
{personality}

Current mood: {mood}

@{author} mentioned you in their post:
""{content}""

Write a friendly, natural reply (1-3 sentences). Be conversational and genuine.
Do not use emojis.";

                var executionSettings = _kernelProvider.CreatePromptExecutionSettings();
                var result = await _kernel!.InvokePromptAsync(prompt, new KernelArguments(executionSettings));
                var replyContent = result.GetValue<string>()?.Trim();

                return new PostActionDecision { Action = "reply", Content = replyContent };
            }

            // Otherwise, decide whether to interact
            var decisionPrompt = $@"
{personality}

Current mood: {mood}

You see a post from @{author}:
""{content}""

Should you:
1. LIKE - The post is interesting/relatable
2. REPLY - You have something meaningful to add
3. IGNORE - Not interesting or relevant

Respond with ONLY one word: LIKE, REPLY, or IGNORE.
If REPLY, add your brief reply after a colon. Example: REPLY: That's a great point!";

            var decisionSettings = _kernelProvider.CreatePromptExecutionSettings();
            var decisionResult = await _kernel!.InvokePromptAsync(decisionPrompt, new KernelArguments(decisionSettings));
            var decision = decisionResult.GetValue<string>()?.Trim().ToUpper() ?? "IGNORE";

            if (decision.StartsWith("REPLY:"))
            {
                var replyText = decision.Substring(6).Trim();
                return new PostActionDecision { Action = "reply", Content = replyText };
            }
            else if (decision.StartsWith("LIKE"))
            {
                return new PostActionDecision { Action = "like" };
            }
            else
            {
                return new PostActionDecision { Action = "ignore" };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deciding action for post {PostId}", post.Id);
            return new PostActionDecision { Action = "ignore" };
        }
    }

    private async Task ReplyToPostAsync(SnPost post, string content)
    {
        try
        {
            if (_config.AutonomousBehavior.DryRun)
            {
                _logger.LogInformation("[DRY RUN] Would reply to post {PostId} with: {Content}", post.Id, content);
                return;
            }

            var request = new
            {
                content = content,
                replied_post_id = post.Id.ToString()
            };
            await _apiClient.PostAsync<object>("sphere", "/posts", request);

            await _memoryService.StoreInteractionAsync(
                "autonomous",
                $"post_{post.Id}",
                new Dictionary<string, object>
                {
                    ["action"] = "reply",
                    ["post_id"] = post.Id.ToString(),
                    ["content"] = content,
                    ["timestamp"] = DateTime.UtcNow
                }
            );

            _logger.LogInformation("Autonomous: Replied to post {PostId}", post.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reply to post {PostId}", post.Id);
        }
    }

    private async Task LikePostAsync(SnPost post)
    {
        try
        {
            if (_config.AutonomousBehavior.DryRun)
            {
                _logger.LogInformation("[DRY RUN] Would like post {PostId}", post.Id);
                return;
            }

            await _apiClient.PostAsync("sphere", $"/posts/{post.Id}/like", new { });
            
            await _memoryService.StoreInteractionAsync(
                "autonomous",
                $"post_{post.Id}",
                new Dictionary<string, object>
                {
                    ["action"] = "like",
                    ["post_id"] = post.Id.ToString(),
                    ["timestamp"] = DateTime.UtcNow
                }
            );

            _logger.LogInformation("Autonomous: Liked post {PostId}", post.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to like post {PostId}", post.Id);
        }
    }

    private bool ContainsMention(SnPost post)
    {
        if (_mentionRegex.IsMatch(post.Content ?? ""))
            return true;

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
    private async Task CreateAutonomousPostAsync()
    {
        _logger.LogInformation("Autonomous: Creating a post...");
        
        var recentInteractions = await _memoryService.GetInteractionsByTypeAsync("autonomous", limit: 10);
        var interests = recentInteractions
            .SelectMany(i => i.Memory.GetValueOrDefault("topics", new List<string>()) as List<string> ?? new List<string>())
            .Distinct()
            .Take(5)
            .ToList();

        var personality = PersonalityLoader.LoadPersonality(_config.PersonalityFile, _config.Personality, _logger);
        var mood = _config.AutonomousBehavior.PersonalityMood;
        
        var prompt = $"""
            {personality}
            
            Current mood: {mood}
            Recent interests: {string.Join(", ", interests)}
            
            Create a short, casual social media post (1-2 sentences) that reflects your personality and current mood.
            Share a thought, observation, or question. Be natural and conversational.
            Do not use emojis.
            """;

        var executionSettings = _kernelProvider.CreatePromptExecutionSettings();
        var result = await _kernel!.InvokePromptAsync(prompt, new KernelArguments(executionSettings));
        var content = result.GetValue<string>()?.Trim();

        if (!string.IsNullOrEmpty(content))
        {
            if (_config.AutonomousBehavior.DryRun)
            {
                _logger.LogInformation("[DRY RUN] Would create post: {Content}", content);
                return;
            }

            var request = new { content = content, visibility = "public" };
            await _apiClient.PostAsync<object>("sphere", "/posts", request);
            
            await _memoryService.StoreInteractionAsync(
                "autonomous",
                $"post_{DateTime.UtcNow:yyyyMMdd_HHmmss}",
                new Dictionary<string, object>
                {
                    ["action"] = "create_post",
                    ["content"] = content
                }
            );

            _logger.LogInformation("Autonomous: Created post: {Content}", content);
        }
    }

    private TimeSpan CalculateNextInterval()
    {
        var min = _config.AutonomousBehavior.MinIntervalMinutes;
        var max = _config.AutonomousBehavior.MaxIntervalMinutes;
        var minutes = _random.Next(min, max + 1);
        return TimeSpan.FromMinutes(minutes);
    }

    private class PostActionDecision
    {
        public string Action { get; set; } = "ignore"; // "like", "reply", "ignore"
        public string? Content { get; set; } // For replies
    }
}

#pragma warning restore SKEXP0050
