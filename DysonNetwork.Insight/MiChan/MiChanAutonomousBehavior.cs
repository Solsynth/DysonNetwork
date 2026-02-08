#pragma warning disable SKEXP0050
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using DysonNetwork.Insight.MiChan.Plugins;
using DysonNetwork.Shared.Models;
using Microsoft.SemanticKernel;
using NodaTime;

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
                    case "repost":
                        await CheckAndRepostInterestingContentAsync();
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

            // Check if MiChan already replied to this post
            var alreadyReplied = await HasMiChanRepliedAsync(post);
            if (alreadyReplied)
            {
                _logger.LogDebug("Skipping post {PostId} - already replied", post.Id);
                continue;
            }

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
                case "react":
                    if (!string.IsNullOrEmpty(decision.ReactionSymbol))
                    {
                        await ReactToPostAsync(post, decision.ReactionSymbol, decision.ReactionAttitude ?? "Positive");
                    }
                    break;
                case "pin":
                    if (decision.PinMode.HasValue)
                    {
                        await PinPostAsync(post, decision.PinMode.Value);
                    }
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
1. REACT - The post is interesting/relatable (choose appropriate reaction)
2. REPLY - You have something meaningful to add
3. PIN - This is important and should be pinned to the profile
4. IGNORE - Not interesting or relevant

Available reaction symbols: thumb_up, heart, clap, laugh, party, pray, cry, confuse, angry, just_okay, thumb_down
Reaction attitudes: Positive, Negative, Neutral

Respond with ONLY one of these formats:
- REACT:symbol:attitude (e.g., REACT:thumb_up:Positive, REACT:heart:Positive, REACT:laugh:Positive)
- REPLY:your reply text
- PIN:mode (ProfilePage or RealmPage)
- IGNORE
If REPLY, add your brief reply after a colon. Example: REPLY: That's a great point!";

            var decisionSettings = _kernelProvider.CreatePromptExecutionSettings();
            var decisionResult = await _kernel!.InvokePromptAsync(decisionPrompt, new KernelArguments(decisionSettings));
            var decision = decisionResult.GetValue<string>()?.Trim().ToUpper() ?? "IGNORE";

            if (decision.StartsWith("REPLY:"))
            {
                var replyText = decision.Substring(6).Trim();
                return new PostActionDecision { Action = "reply", Content = replyText };
            }

            if (decision.StartsWith("REACT:"))
            {
                var parts = decision.Substring(6).Split(':');
                var symbol = parts.Length > 0 ? parts[0].Trim().ToLower() : "thumb_up";
                var attitude = parts.Length > 1 ? parts[1].Trim() : "Positive";
                return new PostActionDecision { Action = "react", ReactionSymbol = symbol, ReactionAttitude = attitude };
            }

            if (decision.StartsWith("PIN:"))
            {
                var mode = decision.Substring(4).Trim();
                var pinMode = mode.Equals("RealmPage", StringComparison.OrdinalIgnoreCase) 
                    ? PostPinMode.RealmPage 
                    : PostPinMode.PublisherPage;
                return new PostActionDecision { Action = "pin", PinMode = pinMode };
            }

            return new PostActionDecision { Action = "ignore" };
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

            var request = new Dictionary<string, object>()
            {
                ["content"] = content,
                ["replied_post_id"] = post.Id.ToString()
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

    private async Task ReactToPostAsync(SnPost post, string symbol, string attitude)
    {
        try
        {
            // Validate symbol
            var validSymbols = new[] { "thumb_up", "thumb_down", "just_okay", "cry", "confuse", "clap", "laugh", "angry", "party", "pray", "heart" };
            if (!validSymbols.Contains(symbol))
            {
                symbol = "thumb_up";
            }

            // Map attitude string to enum value (PostReactionAttitude: Positive=0, Neutral=1, Negative=2)
            var attitudeValue = attitude.ToLower() switch
            {
                "negative" => 2,
                "neutral" => 1,
                _ => 0 // Positive
            };

            if (_config.AutonomousBehavior.DryRun)
            {
                _logger.LogInformation("[DRY RUN] Would react to post {PostId} with {Symbol} ({Attitude})", post.Id, symbol, attitude);
                return;
            }

            var request = new
            {
                symbol = symbol,
                attitude = attitudeValue
            };

            await _apiClient.PostAsync("sphere", $"/api/posts/{post.Id}/reactions", request);
            
            await _memoryService.StoreInteractionAsync(
                "autonomous",
                $"post_{post.Id}",
                new Dictionary<string, object>
                {
                    ["action"] = "react",
                    ["post_id"] = post.Id.ToString(),
                    ["symbol"] = symbol,
                    ["attitude"] = attitude,
                    ["timestamp"] = DateTime.UtcNow
                }
            );

            _logger.LogInformation("Autonomous: Reacted to post {PostId} with {Symbol}", post.Id, symbol);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to react to post {PostId}", post.Id);
        }
    }

    private async Task PinPostAsync(SnPost post, PostPinMode mode)
    {
        try
        {
            // Only pin posts from our own publisher
            if (post.Publisher?.AccountId?.ToString() != _config.BotAccountId)
            {
                _logger.LogDebug("Cannot pin post {PostId} - not owned by bot", post.Id);
                return;
            }

            if (_config.AutonomousBehavior.DryRun)
            {
                _logger.LogInformation("[DRY RUN] Would pin post {PostId} with mode {Mode}", post.Id, mode);
                return;
            }

            var request = new
            {
                mode = mode.ToString()
            };

            await _apiClient.PostAsync("sphere", $"/api/posts/{post.Id}/pin", request);
            
            await _memoryService.StoreInteractionAsync(
                "autonomous",
                $"post_{post.Id}",
                new Dictionary<string, object>
                {
                    ["action"] = "pin",
                    ["post_id"] = post.Id.ToString(),
                    ["mode"] = mode.ToString(),
                    ["timestamp"] = DateTime.UtcNow
                }
            );

            _logger.LogInformation("Autonomous: Pinned post {PostId} with mode {Mode}", post.Id, mode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pin post {PostId}", post.Id);
        }
    }

    private async Task UnpinPostAsync(SnPost post)
    {
        try
        {
            // Only unpin posts from our own publisher
            if (post.Publisher?.AccountId?.ToString() != _config.BotAccountId)
            {
                return;
            }

            if (_config.AutonomousBehavior.DryRun)
            {
                _logger.LogInformation("[DRY RUN] Would unpin post {PostId}", post.Id);
                return;
            }

            await _apiClient.DeleteAsync("sphere", $"/api/posts/{post.Id}/pin");
            
            await _memoryService.StoreInteractionAsync(
                "autonomous",
                $"post_{post.Id}",
                new Dictionary<string, object>
                {
                    ["action"] = "unpin",
                    ["post_id"] = post.Id.ToString(),
                    ["timestamp"] = DateTime.UtcNow
                }
            );

            _logger.LogInformation("Autonomous: Unpinned post {PostId}", post.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unpin post {PostId}", post.Id);
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

    private async Task<bool> HasMiChanRepliedAsync(SnPost post)
    {
        try
        {
            // Get replies to this post
            var replies = await _apiClient.GetAsync<List<SnPost>>("sphere", $"/api/posts/{post.Id}/replies?take=50");
            if (replies == null || replies.Count == 0)
                return false;

            // Check if any reply is from MiChan's publisher
            var botPublisherId = _config.BotPublisherId;
            var botAccountId = _config.BotAccountId;

            foreach (var reply in replies)
            {
                // Check by publisher ID (most reliable)
                if (!string.IsNullOrEmpty(botPublisherId) &&
                    reply.PublisherId?.ToString() == botPublisherId)
                {
                    _logger.LogDebug("Found existing reply from MiChan's publisher {PublisherId} on post {PostId}",
                        botPublisherId, post.Id);
                    return true;
                }

                // Fallback: check by account ID
                if (!string.IsNullOrEmpty(botAccountId) &&
                    reply.Publisher?.AccountId?.ToString() == botAccountId)
                {
                    _logger.LogDebug("Found existing reply from MiChan's account {AccountId} on post {PostId}",
                        botAccountId, post.Id);
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if MiChan already replied to post {PostId}", post.Id);
            return false; // Assume not replied if error occurs
        }
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

    [Experimental("SKEXP0050")]
    private async Task CheckAndRepostInterestingContentAsync()
    {
        _logger.LogInformation("Autonomous: Checking for interesting content to repost...");

        try
        {
            // Get posts in random order with shuffle=true
            var posts = await _apiClient.GetAsync<List<SnPost>>("sphere", "/posts?take=50&shuffle=true");
            if (posts == null || posts.Count == 0)
            {
                _logger.LogDebug("No posts found for reposting");
                return;
            }

            var personality = PersonalityLoader.LoadPersonality(_config.PersonalityFile, _config.Personality, _logger);
            var mood = _config.AutonomousBehavior.PersonalityMood;
            var minAgeDays = _config.AutonomousBehavior.MinRepostAgeDays;
            var cutoffInstant = SystemClock.Instance.GetCurrentInstant().Minus(Duration.FromDays(minAgeDays));

            foreach (var post in posts)
            {
                // Skip if post is too recent
                if (post.PublishedAt == null || post.PublishedAt > cutoffInstant)
                {
                    _logger.LogDebug("Skipping post {PostId} - too recent (published {PublishedAt})",
                        post.Id, post.PublishedAt);
                    continue;
                }

                // Skip own posts
                if (post.Publisher?.AccountId?.ToString() == _config.BotAccountId)
                {
                    _logger.LogDebug("Skipping post {PostId} - own post", post.Id);
                    continue;
                }

                // Skip if MiChan already replied to this post
                var alreadyReplied = await HasMiChanRepliedAsync(post);
                if (alreadyReplied)
                {
                    _logger.LogDebug("Skipping post {PostId} - already replied by MiChan", post.Id);
                    continue;
                }

                // Skip if already reposted
                var repostInteractions = await _memoryService.GetInteractionsByTypeAsync("repost", limit: 100);
                var alreadyReposted = repostInteractions.Any(i => i.Memory.GetValueOrDefault("post_id")?.ToString() == post.Id.ToString());
                if (alreadyReposted)
                {
                    _logger.LogDebug("Skipping post {PostId} - already reposted", post.Id);
                    continue;
                }

                // Check if VERY interesting
                var isVeryInteresting = await IsPostVeryInterestingAsync(post, personality, mood);
                if (isVeryInteresting)
                {
                    await RepostPostAsync(post, personality, mood);
                    break; // Only repost one per cycle
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for repostable content");
        }
    }

    [Experimental("SKEXP0050")]
    private async Task<bool> IsPostVeryInterestingAsync(SnPost post, string personality, string mood)
    {
        try
        {
            var author = post.Publisher?.Name ?? "someone";
            var content = post.Content ?? "";
            var publishedDaysAgo = post.PublishedAt.HasValue 
                ? (SystemClock.Instance.GetCurrentInstant() - post.PublishedAt.Value).TotalDays 
                : 0;

            var prompt = $@"
{personality}

Current mood: {mood}

You discovered an old post from @{author} published {publishedDaysAgo:F1} days ago:
""{content}""

Is this post VERY interesting, valuable, or worth sharing with your followers?
Consider:
- Is the content timeless or still relevant?
- Does it provide unique insights or value?
- Would your followers appreciate seeing this?
- Is it NOT just a casual update but something meaningful?

Respond with ONLY one word: YES or NO.";

            var executionSettings = _kernelProvider.CreatePromptExecutionSettings();
            var result = await _kernel!.InvokePromptAsync(prompt, new KernelArguments(executionSettings));
            var decision = result.GetValue<string>()?.Trim().ToUpper() ?? "NO";

            var isInteresting = decision.StartsWith("YES");
            _logger.LogInformation("Post {PostId} very interesting check: {Decision}", post.Id, isInteresting ? "YES" : "NO");

            return isInteresting;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if post {PostId} is very interesting", post.Id);
            return false;
        }
    }

    private async Task RepostPostAsync(SnPost post, string personality, string mood)
    {
        try
        {
            // Generate a comment for the repost
            var author = post.Publisher?.Name ?? "someone";
            var content = post.Content ?? "";

            var prompt = $@"
{personality}

Current mood: {mood}

You are reposting a great post from @{author}:
""{content}""

Write a brief comment (0-15 words) to accompany this repost. Explain why you're sharing it or add your perspective.
Make it natural and conversational. If you have nothing meaningful to add, just respond with 'NO_COMMENT'.
Do not use emojis.";

            var executionSettings = _kernelProvider.CreatePromptExecutionSettings();
            var result = await _kernel!.InvokePromptAsync(prompt, new KernelArguments(executionSettings));
            var comment = result.GetValue<string>()?.Trim();

            if (comment?.Equals("NO_COMMENT", StringComparison.OrdinalIgnoreCase) == true)
            {
                comment = null;
            }

            if (_config.AutonomousBehavior.DryRun)
            {
                _logger.LogInformation("[DRY RUN] Would repost post {PostId} from @{Author} with comment: {Comment}",
                    post.Id, post.Publisher?.Name ?? "unknown", comment ?? "(none)");
                return;
            }

            var request = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(comment))
            {
                request["content"] = comment;
            }
            request["forwarded_post_id"] = post.Id.ToString();
            request["visibility"] = "public";

            await _apiClient.PostAsync<object>("sphere", "/posts", request);

            await _memoryService.StoreInteractionAsync(
                "repost",
                $"post_{post.Id}",
                new Dictionary<string, object>
                {
                    ["action"] = "repost",
                    ["post_id"] = post.Id.ToString(),
                    ["original_author"] = post.Publisher?.Name ?? "unknown",
                    ["comment"] = comment ?? "",
                    ["timestamp"] = DateTime.UtcNow
                }
            );

            _logger.LogInformation("Autonomous: Reposted post {PostId} from @{Author} with comment: {Comment}",
                post.Id, post.Publisher?.Name ?? "unknown", comment ?? "(none)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to repost post {PostId}", post.Id);
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
        public string Action { get; set; } = "ignore"; // "react", "reply", "pin", "ignore"
        public string? Content { get; set; } // For replies
        public string? ReactionSymbol { get; set; } // For reactions: thumb_up, heart, etc.
        public string? ReactionAttitude { get; set; } // For reactions: Positive, Negative, Neutral
        public PostPinMode? PinMode { get; set; } // For pins: ProfilePage, RealmPage
    }
}

#pragma warning restore SKEXP0050
