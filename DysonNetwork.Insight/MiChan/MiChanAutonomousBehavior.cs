#pragma warning disable SKEXP0050
using System.Diagnostics.CodeAnalysis;
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
            
            // Select a random action from configured actions
            var availableActions = _config.AutonomousBehavior.Actions;
            if (availableActions.Count == 0)
                return false;

            var action = availableActions[_random.Next(availableActions.Count)];
            
            switch (action)
            {
                case "browse":
                    await BrowseAndAnalyzePostsAsync();
                    break;
                case "like":
                    await BrowseAndLikePostsAsync();
                    break;
                case "create_post":
                    await CreateAutonomousPostAsync();
                    break;
                case "reply_trending":
                    await ReplyToTrendingPostAsync();
                    break;
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
    private async Task BrowseAndAnalyzePostsAsync()
    {
        _logger.LogInformation("Autonomous: Browsing timeline...");
        
        // Get timeline posts
        var posts = await _apiClient.GetAsync<List<SnPost>>("sphere", "/timeline/home?take=20");
        if (posts == null || posts.Count == 0)
            return;

        // Select a random post to analyze
        var post = posts[_random.Next(posts.Count)];
        
        // Store in memory
        await _memoryService.StoreInteractionAsync(
            "autonomous",
            $"browse_{DateTime.UtcNow:yyyyMMdd_HHmmss}",
            new Dictionary<string, object>
            {
                ["action"] = "browse",
                ["post_id"] = post.Id,
                ["post_content"] = post.Content,
                ["author"] = post.Publisher?.Name ?? "unknown"
            },
            new Dictionary<string, object>
            {
                ["interest_level"] = _random.Next(1, 10),
                ["mood"] = _config.AutonomousBehavior.PersonalityMood
            }
        );

        _logger.LogInformation("Autonomous: Analyzed post from {Author}", post.Publisher?.Name ?? "unknown");
    }

    [Experimental("SKEXP0050")]
    private async Task BrowseAndLikePostsAsync()
    {
        _logger.LogInformation("Autonomous: Browsing for interesting posts to like...");
        
        var posts = await _apiClient.GetAsync<List<SnPost>>("sphere", "/timeline/home?take=20");
        if (posts == null || posts.Count == 0)
            return;

        // Try to find a post that matches MiChan's "interests"
        var postPlugin = _kernel!.Plugins["post"];
        var getPostFunction = postPlugin["get_post"];

        // Randomly like 1-3 posts
        var postsToLike = posts.OrderBy(_ => _random.Next()).Take(_random.Next(1, 4));
        
        foreach (var post in postsToLike)
        {
            try
            {
                await _apiClient.PostAsync("sphere", $"/posts/{post.Id}/like", new { });
                _logger.LogInformation("Autonomous: Liked post {PostId}", post.Id);
                
                await Task.Delay(TimeSpan.FromSeconds(2)); // Small delay between likes
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to like post {PostId}", post.Id);
            }
        }
    }

    [Experimental("SKEXP0050")]
    private async Task CreateAutonomousPostAsync()
    {
        _logger.LogInformation("Autonomous: Creating a post...");
        
        // Get recent memories to inform post content
        var recentInteractions = await _memoryService.GetInteractionsByTypeAsync("autonomous", 10);
        var interests = recentInteractions
            .SelectMany(i => i.Memory.GetValueOrDefault("topics", new List<string>()) as List<string> ?? new List<string>())
            .Distinct()
            .Take(5)
            .ToList();

        // Generate post content using AI
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

    [Experimental("SKEXP0050")]
    private async Task ReplyToTrendingPostAsync()
    {
        _logger.LogInformation("Autonomous: Looking for trending posts to reply to...");
        
        // Get trending posts
        var posts = await _apiClient.GetAsync<List<SnPost>>("sphere", "/timeline/global?take=20");
        if (posts == null || posts.Count == 0)
            return;

        // Select a post with engagement
        var trendingPost = posts
            .Where(p => !string.IsNullOrEmpty(p.Content))
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefault();

        if (trendingPost == null)
            return;

        // Generate reply
        var personality = PersonalityLoader.LoadPersonality(_config.PersonalityFile, _config.Personality, _logger);
        
        var prompt = $"""
            {personality}
            
            Someone posted: "{trendingPost.Content}"
            
            Write a brief, friendly reply (1-2 sentences) that adds to the conversation.
            Be genuine and conversational. Do not use emojis.
            """;

        var executionSettings = _kernelProvider.CreatePromptExecutionSettings();
        var result = await _kernel!.InvokePromptAsync(prompt, new KernelArguments(executionSettings));
        var replyContent = result.GetValue<string>()?.Trim();

        if (!string.IsNullOrEmpty(replyContent))
        {
            var request = new
            {
                content = replyContent,
                reply_to = trendingPost.Id.ToString()
            };
            await _apiClient.PostAsync<object>("sphere", "/posts", request);

            _logger.LogInformation("Autonomous: Replied to post {PostId}", trendingPost.Id);
        }
    }

    private TimeSpan CalculateNextInterval()
    {
        var min = _config.AutonomousBehavior.MinIntervalMinutes;
        var max = _config.AutonomousBehavior.MaxIntervalMinutes;
        var minutes = _random.Next(min, max + 1);
        return TimeSpan.FromMinutes(minutes);
    }
}

#pragma warning restore SKEXP0050
