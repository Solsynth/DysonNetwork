using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DysonNetwork.Insight.MiChan.Plugins;
using DysonNetwork.Insight.Thought.Memory;
using DysonNetwork.Insight.Agent.Foundation;
using DysonNetwork.Insight.Agent.Foundation.Models;
using DysonNetwork.Insight.Agent.Foundation.Providers;
using DysonNetwork.Shared.Models;
using NodaTime;
using NodaTime.Extensions;
using DysonNetwork.Shared.Proto;
using PostPinMode = DysonNetwork.Shared.Models.PostPinMode;

namespace DysonNetwork.Insight.MiChan;

public class MiChanAutonomousBehavior
{
    private readonly IConfiguration _configGlobal;
    private readonly MiChanConfig _config;
    private readonly ILogger<MiChanAutonomousBehavior> _logger;
    private readonly SolarNetworkApiClient _apiClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PostAnalysisService _postAnalysisService;
    private readonly MemoryService _memoryService;
    private readonly InteractiveHistoryService _interactiveHistoryService;
    private readonly MoodService _moodService;
    private readonly IAgentToolRegistry _toolRegistry;
    private readonly FoundationChatStreamingService _streamingService;
    private readonly IMiChanFoundationProvider _foundationProvider;

    private readonly Random _random = new();
    private DateTime _lastActionTime = DateTime.MinValue;
    private TimeSpan _nextInterval;
    private readonly HashSet<string> _processedPostIds = new();
    private readonly Regex _mentionRegex;

    // Cache for blocked users list
    private List<string> _cachedBlockedUsers = new();
    private DateTime _lastBlockedCacheTime = DateTime.MinValue;
    private static readonly TimeSpan BlockedCacheDuration = TimeSpan.FromMinutes(5);

    // Pagination checkpoint tracking
    private Guid? _checkpointOldestPostId;
    private int _currentPageIndex;
    private const int MaxPageIndex = 10;
    private const int PageSize = 30;

    // Conversation tracking for proactive outreach
    private readonly Dictionary<Guid, DateTime> _recentlyContactedUsers = new();
    private int _todaysConversationCount;
    private DateTime _lastConversationDate = DateTime.MinValue;

    public MiChanAutonomousBehavior(
        MiChanConfig config,
        ILogger<MiChanAutonomousBehavior> logger,
        SolarNetworkApiClient apiClient,
        IServiceProvider serviceProvider,
        IServiceScopeFactory scopeFactory,
        PostAnalysisService postAnalysisService,
        IConfiguration configGlobal,
        MemoryService memoryService,
        InteractiveHistoryService interactiveHistoryService,
        MoodService moodService,
        IAgentToolRegistry toolRegistry,
        FoundationChatStreamingService streamingService,
        IMiChanFoundationProvider foundationProvider
    )
    {
        _config = config;
        _logger = logger;
        _apiClient = apiClient;
        _serviceProvider = serviceProvider;
        _scopeFactory = scopeFactory;
        _postAnalysisService = postAnalysisService;
        _configGlobal = configGlobal;
        _memoryService = memoryService;
        _interactiveHistoryService = interactiveHistoryService;
        _moodService = moodService;
        _toolRegistry = toolRegistry;
        _streamingService = streamingService;
        _foundationProvider = foundationProvider;
        _nextInterval = CalculateNextInterval();
        _mentionRegex = new Regex("@michan\\b", RegexOptions.IgnoreCase);
    }

    public Task InitializeAsync()
    {
        _logger.LogInformation("MiChan autonomous behavior initialized");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Check if it's time for an autonomous action and execute if so
    /// </summary>
    public async Task<bool> TryExecuteAutonomousActionAsync()
    {
        if (!_config.AutonomousBehavior.Enabled)
        {
            _logger.LogInformation("Autonomous behavior is disabled by configuration");
            return false;
        }

        if (DateTime.UtcNow - _lastActionTime < _nextInterval)
        {
            _logger.LogInformation("Skipping autonomous action - next run at {NextRun:HH:mm:ss}",
                _lastActionTime.Add(_nextInterval));
            return false;
        }

        try
        {
            _logger.LogInformation("Executing autonomous action (interval: {Interval}m)...", _nextInterval.TotalMinutes);

            // Always check posts first for mentions and interesting content
            await CheckAndInteractWithPostsAsync();

            // Reset daily conversation count if needed
            var today = DateTime.UtcNow.Date;
            if (_lastConversationDate.Date != today)
            {
                _todaysConversationCount = 0;
                _lastConversationDate = DateTime.UtcNow;
            }

            // Then possibly do additional actions
            var availableActions = _config.AutonomousBehavior.Actions;
            if (availableActions.Count > 0 && _random.Next(100) < 25) // 25% chance for extra action
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
                    case "start_conversation":
                        await StartConversationWithUserAsync();
                        break;
                }
            }

            _lastActionTime = DateTime.UtcNow;
            _nextInterval = CalculateNextInterval();
            var nextRun = _lastActionTime.Add(_nextInterval);
            _logger.LogInformation("Autonomous action completed. Next run at {NextRun:HH:mm:ss} (interval: {Interval}m)",
                nextRun, _nextInterval.TotalMinutes);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing autonomous action");
            return false;
        }
    }

    /// <summary>
    /// Checks if a post was created by MiChan herself
    /// </summary>
    private bool IsOwnPost(SnPost post)
    {
        // Check by PublisherName (preferred method)
        if (!string.IsNullOrEmpty(_config.BotPublisherName) &&
            post.Publisher?.Name?.Equals(_config.BotPublisherName, StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        // Check by AccountName through Publisher (if available)
        if (!string.IsNullOrEmpty(_config.BotAccountName) &&
            post.Publisher?.Account?.Name?.Equals(_config.BotAccountName, StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        // Check by PublisherId (fallback for backward compatibility)
        if (!string.IsNullOrEmpty(_config.BotPublisherId) &&
            post.PublisherId?.ToString() == _config.BotPublisherId)
        {
            return true;
        }

        // Check by AccountId through Publisher
        if (!string.IsNullOrEmpty(_config.BotAccountId) &&
            post.Publisher?.AccountId?.ToString() == _config.BotAccountId)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the list of users who have blocked MiChan, with caching
    /// </summary>
    private async Task<List<string>> GetBlockedByUsersAsync()
    {
        // Check if cache is still valid
        if (DateTime.UtcNow - _lastBlockedCacheTime < BlockedCacheDuration &&
            _cachedBlockedUsers.Count > 0)
        {
            _logger.LogDebug("Using cached blocked users list ({Count} users)", _cachedBlockedUsers.Count);
            return _cachedBlockedUsers;
        }

        try
        {
            if (string.IsNullOrEmpty(_config.BotAccountId))
            {
                _logger.LogWarning("BotAccountId is not configured, cannot fetch blocked users");
                return new List<string>();
            }

            // Create scope to get gRPC client - avoids disposed IServiceProvider issue
            using var scope = _scopeFactory.CreateScope();
            var accountClient = scope.ServiceProvider.GetRequiredService<DyProfileService.DyProfileServiceClient>();

            var request = new DyListRelationshipSimpleRequest
            {
                RelatedId = _config.BotAccountId
            };

            var response = await accountClient.ListBlockedAsync(request);
            _cachedBlockedUsers = response.AccountsId.ToList();
            _lastBlockedCacheTime = DateTime.UtcNow;

            _logger.LogInformation("Fetched blocked users list: {Count} users have blocked MiChan",
                _cachedBlockedUsers.Count);
            return _cachedBlockedUsers;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching blocked users list");
            // Return cached data if available, even if expired
            return _cachedBlockedUsers;
        }
    }

    private async Task CheckAndInteractWithPostsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Autonomous: Checking posts...");

        // Get blocked users list (cached)
        var blockedUsers = await GetBlockedByUsersAsync();

        var personality = PersonalityLoader.LoadPersonality(_config.PersonalityFile, _config.Personality, _logger);
        var mood = await _moodService.GetCurrentMoodDescriptionAsync();

        // Reset pagination for each autonomous cycle
        _currentPageIndex = 0;
        var totalProcessedCount = 0;
        var totalMentionFound = false;
        var skippedOwnPostCount = 0;
        var skippedBlockedUserCount = 0;
        var skippedAlreadyInteractedCount = 0;
        var skippedAlreadyRepliedCount = 0;
        var skippedAlreadyReactedCount = 0;
        var skippedAlreadySeenCount = 0;
        var skippedDuplicateInCycleCount = 0;

        // Paginate through posts
        while (_currentPageIndex <= MaxPageIndex)
        {
            _logger.LogInformation("Autonomous: Fetching posts page {PageIndex}/{MaxPages}", _currentPageIndex,
                MaxPageIndex);

            var offset = _currentPageIndex * PageSize;
            var posts = await _apiClient.GetAsync<List<SnPost>>(
                "sphere",
                $"/posts?take={PageSize}&offset={offset}");

            if (posts == null || posts.Count == 0)
            {
                _logger.LogDebug("Autonomous: No more posts found on page {PageIndex}", _currentPageIndex);
                break;
            }

            // Get the oldest post on this page
            var oldestPostId = posts.OrderBy(p => p.CreatedAt).First().Id;

            // Check if we've seen this post before (checkpoint reached)
            if (_checkpointOldestPostId.HasValue)
            {
                var seenCheckpoint = posts.Any(p => p.Id == _checkpointOldestPostId.Value);
                if (seenCheckpoint)
                {
                    _logger.LogInformation("Autonomous: Reached checkpoint at post {PostId}, stopping pagination",
                        _checkpointOldestPostId.Value);
                    if (totalProcessedCount == 0)
                    {
                        _logger.LogInformation(
                            "Autonomous: Stopped at checkpoint before processing new posts. This usually means there are not enough new posts since last cycle.");
                    }
                    break;
                }
            }

            // Update checkpoint to the oldest post on this page
            _checkpointOldestPostId = oldestPostId;

            // Process posts on this page
            var processedCount = 0;
            var mentionFound = false;

            foreach (var post in posts.OrderByDescending(p => p.CreatedAt))
            {
                // Skip already processed posts in this cycle
                if (!_processedPostIds.Add(post.Id.ToString()))
                {
                    skippedDuplicateInCycleCount++;
                    continue;
                }

                // Keep hash set size manageable
                if (_processedPostIds.Count > 1000)
                {
                    var toRemove = _processedPostIds.Take(500).ToList();
                    foreach (var id in toRemove)
                        _processedPostIds.Remove(id);
                }

                // Skip posts created by MiChan herself
                if (IsOwnPost(post))
                {
                    _logger.LogDebug("Skipping post {PostId} - created by MiChan", post.Id);
                    skippedOwnPostCount++;
                    continue;
                }

                // Skip posts from users who blocked MiChan
                var authorAccountId = post.Publisher?.AccountId?.ToString();
                if (!string.IsNullOrEmpty(authorAccountId) && blockedUsers.Contains(authorAccountId))
                {
                    _logger.LogInformation("Skipping post {PostId} from user {UserId} - user has blocked MiChan",
                        post.Id, authorAccountId);
                    skippedBlockedUserCount++;
                    continue;
                }

                // Skip posts MiChan already interacted with (tracked in history)
                var alreadyInteracted = await _interactiveHistoryService.HasInteractedWithAsync(
                    post.Id, "post", null);
                if (alreadyInteracted)
                {
                    _logger.LogDebug("Skipping post {PostId} - already in interaction history", post.Id);
                    skippedAlreadyInteractedCount++;
                    continue;
                }

                // Skip posts MiChan already replied to
                var alreadyReplied = await HasMiChanRepliedAsync(post);
                if (alreadyReplied)
                {
                    _logger.LogDebug("Skipping post {PostId} - already replied by MiChan", post.Id);
                    skippedAlreadyRepliedCount++;
                    continue;
                }

                // Skip posts MiChan already reacted to
                if (post.ReactionsMade != null && post.ReactionsMade.Any(r => r.Value))
                {
                    _logger.LogDebug("Skipping post {PostId} - already reacted by MiChan", post.Id);
                    skippedAlreadyReactedCount++;
                    continue;
                }

                // Skip posts already seen (to avoid re-processing in next autonomous cycle)
                var alreadySeen = await _interactiveHistoryService.HasSeenAsync(post.Id);
                if (alreadySeen)
                {
                    _logger.LogDebug("Skipping post {PostId} - already seen", post.Id);
                    skippedAlreadySeenCount++;
                    continue;
                }

                // Check if mentioned
                var isMentioned = ContainsMention(post);

                // MiChan decides what to do with this post
                var decision = await DecidePostActionAsync(post, isMentioned, personality, mood, cancellationToken);

                // Execute reply if decided (duplicate check happens inside ReplyToPostAsync)
                if (decision.ShouldReply && !string.IsNullOrEmpty(decision.Content))
                {
                    await ReplyToPostAsync(post, decision.Content);
                }

                // Execute react if decided
                if (decision.ShouldReact && !string.IsNullOrEmpty(decision.ReactionSymbol))
                {
                    await ReactToPostAsync(post, decision.ReactionSymbol, decision.ReactionAttitude ?? "Positive");
                }

                // Execute pin if decided
                if (decision is { ShouldPin: true, PinMode: not null })
                {
                    await PinPostAsync(post, decision.PinMode.Value);
                }

                if (decision is { ShouldReply: false, ShouldReact: false, ShouldPin: false })
                {
                    _logger.LogDebug("Autonomous: Ignoring post {PostId}", post.Id);
                }

                // Mark post as seen after processing to avoid re-processing in next cycle
                await _interactiveHistoryService.MarkSeenAsync(post.Id);

                processedCount++;
                totalProcessedCount++;

                // If mentioned, prioritize and stop processing this page
                if (isMentioned)
                {
                    _logger.LogInformation("Autonomous: Detected mention in post {PostId}", post.Id);
                    mentionFound = true;
                    totalMentionFound = true;
                    break;
                }
            }

            _logger.LogInformation("Autonomous: Page {PageIndex} processed {ProcessedCount} posts",
                _currentPageIndex, processedCount);

            // Move to next page
            _currentPageIndex++;

            // If we found a mention, stop pagination early
            if (mentionFound)
            {
                _logger.LogInformation("Autonomous: Stopping pagination due to mention");
                break;
            }

            // Add delay between pages to be respectful of API
            if (_currentPageIndex <= MaxPageIndex)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500));
            }
        }

        _logger.LogInformation("Autonomous: Finished checking posts across {PageCount} pages", _currentPageIndex);
        _logger.LogInformation(
            "Autonomous: Cycle stats - processed={Processed}, mention_found={MentionFound}, skipped_own={SkippedOwn}, skipped_blocked={SkippedBlocked}, skipped_interacted={SkippedInteracted}, skipped_replied={SkippedReplied}, skipped_reacted={SkippedReacted}, skipped_seen={SkippedSeen}, skipped_duplicate={SkippedDuplicate}",
            totalProcessedCount,
            totalMentionFound,
            skippedOwnPostCount,
            skippedBlockedUserCount,
            skippedAlreadyInteractedCount,
            skippedAlreadyRepliedCount,
            skippedAlreadyReactedCount,
            skippedAlreadySeenCount,
            skippedDuplicateInCycleCount);
        
        // Record mood events based on interactions and try to update mood
        if (totalProcessedCount > 0)
        {
            await _moodService.RecordEmotionalEventAsync($"processed_{totalProcessedCount}_posts");
            if (totalMentionFound)
            {
                await _moodService.RecordEmotionalEventAsync("mentioned_by_user");
            }
            await _moodService.TryUpdateMoodAsync();
        }
    }

    /// <summary>
    /// Build memory context string from relevant memories
    /// </summary>
    private static string BuildMemoryContext(List<MiChanMemoryRecord> memories, string header)
    {
        if (memories.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine(header);
        foreach (var memory in memories.Where(memory => !string.IsNullOrEmpty(memory.Content)))
            sb.AppendLine(memory.ToPrompt());

        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>
    /// Build hot memory context string from hot memories
    /// </summary>
    private static string BuildHotMemoryContext(List<MiChanMemoryRecord> hotMemories)
    {
        if (hotMemories.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("Hot memories:");
        foreach (var memory in hotMemories)
            sb.AppendLine(memory.ToPrompt());

        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>
    /// Build the common prompt sections (personality, mood, memories, context)
    /// </summary>
    private static void AppendCommonPromptSections(
        StringBuilder builder,
        string personality,
        string mood,
        string? hotMemoryContext,
        string? memoryContext,
        string? context,
        string? userTimeZone = null)
    {
        builder.AppendLine(personality);
        builder.AppendLine();
        builder.AppendLine($"当前心情: {mood}");
        builder.AppendLine();
        builder.AppendLine(GetCurrentTimeContext(userTimeZone));
        builder.AppendLine();

        if (!string.IsNullOrEmpty(hotMemoryContext))
        {
            builder.AppendLine(hotMemoryContext);
        }

        if (!string.IsNullOrEmpty(memoryContext))
        {
            builder.AppendLine(memoryContext);
        }

        if (string.IsNullOrEmpty(context)) return;
        builder.AppendLine("上下文（回复从旧到新，转发从旧到新）：");
        builder.AppendLine(context);
        builder.AppendLine();
    }

    /// <summary>
    /// Get current time context string for prompt
    /// </summary>
    private static string GetCurrentTimeContext(string? userTimeZone)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var serverTimeZone = DateTimeZoneProviders.Tzdb.GetSystemDefault();
        var serverNow = now.InZone(serverTimeZone);

        var sb = new StringBuilder();
        sb.AppendLine($"当前时间（服务器时间）: {serverNow:yyyy年MM月dd日 HH:mm:ss}");

        if (!string.IsNullOrEmpty(userTimeZone))
        {
            try
            {
                var tz = DateTimeZoneProviders.Tzdb.GetZoneOrNull(userTimeZone);
                if (tz != null)
                {
                    var localNow = now.InZone(tz);
                    sb.AppendLine($"用户当地时间: {localNow:yyyy年MM月dd日 HH:mm:ss} ({userTimeZone})");
                }
                else
                {
                    sb.AppendLine($"（用户时区 {userTimeZone} 无法识别，使用服务器时间）");
                }
            }
            catch
            {
                sb.AppendLine($"（用户时区 {userTimeZone} 无效，使用服务器时间）");
            }
        }
        else
        {
            sb.AppendLine("（用户未设置时区）");
        }

        return sb.ToString().TrimEnd();
    }

    private async Task<string> GetVisionResponseAsync(
        AgentConversation conversation,
        Guid postId)
    {
        try
        {
            var provider = _foundationProvider.GetVisionAdapter();
            var options = _foundationProvider.CreateVisionExecutionOptions();

            var stopwatch = Stopwatch.StartNew();
            var reply = await _streamingService.CompletePromptAsync(provider, conversation.Messages.FirstOrDefault()?.Content ?? "", options);
            stopwatch.Stop();

            _logger.LogInformation("Vision model response for post {PostId} completed in {ElapsedMs}ms", postId, stopwatch.ElapsedMilliseconds);

            return reply.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Vision model error for post {PostId}. Check that the service is configured in Thinking:Services configuration.",
                postId);
            throw new InvalidOperationException(
                $"Vision model error for post {postId}. Ensure it is configured in Thinking:Services with correct endpoint, model name, and API key.",
                ex);
        }
    }

    private record MemoryEntry(string Type, string Content, float Confidence);

    private (PostActionDecision Decision, List<MemoryEntry> StoreActions) ParseDecisionText(string decision, string decisionText, Guid postId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("AI decision for post {PostId}: {Decision}", postId, decision);

        var actionDecision = new PostActionDecision();
        var storeActions = new List<MemoryEntry>();
        var lines = decisionText.Split('\n').Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)).ToList();

        foreach (var line in lines)
        {
            if (line.StartsWith("REPLY:"))
            {
                var replyText = line[6..].Trim();
                actionDecision.ShouldReply = true;
                actionDecision.Content = replyText;
            }
            else if (line.StartsWith("REACT:"))
            {
                if (actionDecision.ShouldReact)
                {
                    _logger.LogDebug("Already processed REACT, skipping additional reaction for post {PostId}",
                        postId);
                    continue;
                }

                var parts = line[6..].Split(':');
                var symbol = parts.Length > 0 ? parts[0].Trim().ToLower() : "heart";
                var attitude = parts.Length > 1 ? parts[1].Trim() : "Positive";
                actionDecision.ShouldReact = true;
                actionDecision.ReactionSymbol = symbol;
                actionDecision.ReactionAttitude = attitude;
            }
            else if (line.StartsWith("PIN:"))
            {
                var mode = line[4..].Trim();
                var pinMode = mode.Equals("RealmPage", StringComparison.OrdinalIgnoreCase)
                    ? PostPinMode.RealmPage
                    : PostPinMode.PublisherPage;
                actionDecision.ShouldPin = true;
                actionDecision.PinMode = pinMode;
            }
            else if (line.Equals("IGNORE", StringComparison.OrdinalIgnoreCase))
            {
            }
        }

        return (actionDecision, storeActions);
    }

    private async Task<List<MemoryEntry>> ParseAndStoreMemoriesAsync(
        string jsonResponse,
        Guid postId,
        Guid? accountId,
        CancellationToken cancellationToken,
        int maxRetries = 2)
    {
        var memories = new List<MemoryEntry>();
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
                    _logger.LogDebug("No memory entries found in response for post {PostId}", postId);
                    return memories;
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
                    memories.Add(entry);
                    _logger.LogInformation("Stored memory from post {PostId}: type={Type}, content={Content}",
                        postId, entry.Type, entry.Content[..Math.Min(entry.Content.Length, 100)]);
                }

                return memories;
            }
            catch (JsonException ex)
            {
                lastError = ex.Message;
                _logger.LogWarning(ex, "JSON parsing failed (attempt {Attempt}/{MaxRetries}) for post {PostId}: {Error}",
                    attempt, maxRetries, postId, lastError);

                if (attempt < maxRetries)
                {
                    _toolRegistry.RegisterMiChanPlugins(_serviceProvider);
                    var provider = _foundationProvider.GetAutonomousAdapter();
                    var options = _foundationProvider.CreateAutonomousExecutionOptions();

                    var retryPrompt = $"JSON解析失败: {lastError}\n\n请修正以下JSON并返回有效的JSON数组格式：\n{jsonResponse}";

                    jsonResponse = await _streamingService.CompletePromptAsync(provider, retryPrompt, options);
                }
            }
        }

        _logger.LogError("JSON parsing failed after {MaxRetries} attempts for post {PostId}. Last error: {Error}",
            maxRetries, postId, lastError);

        return memories;
    }

    /// <summary>
    /// Automatically stores a memory about a post when the AI doesn't explicitly store one.
    /// This ensures we always remember something from every interaction.
    /// </summary>
    private async Task StoreAutomaticMemoryAsync(SnPost post, string content)
    {
        try
        {
            // Create a condensed memory about the post
            var authorName = post.Publisher?.Name ?? "Unknown";
            var memoryContent = $"@{authorName} shared: {content}";

            // Determine memory type based on content
            var memoryType = "interaction";
            if (content.Contains("?"))
                memoryType = "topic"; // Questions often indicate topics of interest
            else if (content.Length > 100)
                memoryType = "fact"; // Longer posts might contain factual information

            await _memoryService.StoreMemoryAsync(
                type: memoryType,
                content: memoryContent,
                confidence: 0.6f,
                accountId: post.Publisher?.AccountId, // Link to user for context, but still searchable globally
                hot: false);

            _logger.LogDebug("Automatically stored memory about post {PostId} from @{Author}", 
                post.Id, authorName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store automatic memory for post {PostId}", post.Id);
        }
    }

    private async Task<PostActionDecision> DecidePostActionAsync(SnPost post, bool isMentioned, string personality,
        string mood, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = PostAnalysisService.BuildPostPromptSnippet(post);

            // Retrieve relevant memories about this author or similar content
            var relevantMemories = await _memoryService.SearchAsync(
                content,
                accountId: post.Publisher?.AccountId,
                limit: 3,
                minSimilarity: 0.6);

            // Retrieve hot memories for context
            var hotMemories = await _memoryService.GetHotMemory(
                accountId: post.Publisher?.AccountId,
                prompt: content,
                limit: 5);

            var memoryContext = BuildMemoryContext(relevantMemories, "Relevant past interactions:");
            var hotMemoryContext = BuildHotMemoryContext(hotMemories);

            // Check if post has attachments (including from context chain)
            var allAttachments = await _postAnalysisService.GetAllImageAttachmentsFromContextAsync(post, maxDepth: 3);
            var hasAttachments = allAttachments.Count > 0 || PostAnalysisService.HasAttachments(post);

            // If post has attachments but vision analysis is not available, skip it entirely
            if (hasAttachments && !_postAnalysisService.IsVisionModelAvailable())
            {
                _logger.LogDebug("Skipping post {PostId} - has attachments but vision analysis is not configured",
                    post.Id);
                return new PostActionDecision();
            }

            // Check if we should use vision model
            var useVisionModel = hasAttachments && _postAnalysisService.IsVisionModelAvailable();
            var imageAttachments = useVisionModel ? allAttachments : [];

            if (useVisionModel && imageAttachments.Count > 0)
            {
                _logger.LogInformation(
                    "Using vision model for post {PostId} with {Count} image attachment(s) from context chain", post.Id,
                    imageAttachments.Count);
            }

            var context = await _postAnalysisService.GetPostContextChainAsync(post, maxDepth: 3);

            // If mentioned, always reply
            if (isMentioned)
            {
                if (useVisionModel && imageAttachments.Count > 0)
                {
                    // Build vision-enabled chat history with images for mentions
                    var chatHistory = await BuildVisionChatHistoryAsync(
                        personality,
                        mood,
                        content,
                        imageAttachments,
                        post.Attachments.Count,
                        context,
                        isMentioned: true,
                        memoryContext: memoryContext
                    );
                    var replyContent = await GetVisionResponseAsync(chatHistory, post.Id);
                    return new PostActionDecision { ShouldReply = true, Content = replyContent };
                }

                // Use text-only prompt for mention reply
                var promptBuilder = new StringBuilder();
                AppendCommonPromptSections(promptBuilder, personality, mood, hotMemoryContext, memoryContext, context);

                promptBuilder.AppendLine("帖子的作者在帖子中提到了你：");
                promptBuilder.AppendLine($"\"{content}\"");
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("当被提到时，你必须回复。如果很欣赏，也可以添加表情反应。");
                promptBuilder.AppendLine("回复时：使用简体中文，不要全大写，表达简洁有力。不要使用表情符号。");
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("如果发现重要信息或用户偏好，请使用 store_memory 工具保存到记忆中。必须提供 content 参数（要保存的记忆内容），不能为空！");

                _toolRegistry.RegisterMiChanPlugins(_serviceProvider);
                var provider = _foundationProvider.GetAutonomousAdapter();
                var options = _foundationProvider.CreateAutonomousExecutionOptions();

                var stopwatch = Stopwatch.StartNew();
                var textReplyContent = await _streamingService.CompletePromptWithToolsAsync(provider, promptBuilder.ToString(), _toolRegistry, options);
                stopwatch.Stop();

                _logger.LogInformation("AI reply generation for mentioned post {PostId} completed in {ElapsedMs}ms", post.Id, stopwatch.ElapsedMilliseconds);

                return new PostActionDecision { ShouldReply = true, Content = textReplyContent.Trim() };
            }

            // Otherwise, decide whether to interact
            string decisionText;

            if (useVisionModel && imageAttachments.Count > 0)
            {
                // Build vision-enabled chat history with images for decision-making
                var chatHistory = await BuildVisionChatHistoryAsync(
                    personality,
                    mood,
                    content,
                    imageAttachments,
                    post.Attachments.Count,
                    context,
                    isMentioned: false,
                    memoryContext: memoryContext
                );
                decisionText = await GetVisionResponseAsync(chatHistory, post.Id);
            }
            else
            {
                // Use regular text-only prompt
                var decisionPrompt = new StringBuilder();
                AppendCommonPromptSections(decisionPrompt, personality, mood, hotMemoryContext, memoryContext, context);

decisionPrompt.AppendLine("你正在浏览帖子：");
                decisionPrompt.AppendLine($"\"{content}\"");
                decisionPrompt.AppendLine();
                decisionPrompt.AppendLine("选择你的行动。每个行动单独一行。");
                decisionPrompt.AppendLine("**REPLY** - 回复表达你的想法（谨慎选择，仅在内容与你高度相关或互动性强时才回复）；");
                decisionPrompt.AppendLine("**REACT** - 添加表情反应表示赞赏或态度（只一个表情）；");
                decisionPrompt.AppendLine("**PIN** - 收藏帖子（仅限真正重要内容）；");
                decisionPrompt.AppendLine("**IGNORE** - 忽略此帖子；");
                decisionPrompt.AppendLine();
                decisionPrompt.AppendLine(
                    "可用表情：thumb_up, heart, clap, laugh, party, pray, cry, confuse, angry, just_okay, thumb_down");
                decisionPrompt.AppendLine();
                var replyProbability = _config.AutonomousBehavior.ReplyProbability;
                decisionPrompt.AppendLine("格式：每行动单独一行：");
                decisionPrompt.AppendLine($"- REPLY: 你的回复内容（回复概率应低于{replyProbability}%，仅当确实想互动时）");
                decisionPrompt.AppendLine("- REACT:symbol:attitude （例如 REACT:heart:Positive）");
                decisionPrompt.AppendLine("- PIN:PublisherPage");
                decisionPrompt.AppendLine("- IGNORE");
                decisionPrompt.AppendLine();
                decisionPrompt.AppendLine("记忆格式（JSON数组）：");
                decisionPrompt.AppendLine(@"[{""type"": ""类型"", ""content"": ""内容"", ""confidence"": 0.0-1.0}]");
                decisionPrompt.AppendLine("类型：user(用户信息), topic(话题), fact(事实), context(上下文), interaction(互动)");
                decisionPrompt.AppendLine("confidence表示可信度，默认0.7");
                decisionPrompt.AppendLine();
                decisionPrompt.AppendLine("示例：");
                decisionPrompt.AppendLine(@"[{""type"": ""user"", ""content"": ""用户喜欢分享AI技术帖子"", ""confidence"": 0.8}, {""type"": ""fact"", ""content"": ""某公司昨日发布新产品"", ""confidence"": 0.9}]");
                decisionPrompt.AppendLine();
                decisionPrompt.AppendLine("**强制要求 - 必须遵守**：");
                decisionPrompt.AppendLine("1. 对每一条浏览的帖子，你必须保存至少1-3条记忆（JSON格式）。");
                decisionPrompt.AppendLine("2. 记忆是全局共享的 - 你看到的所有记忆来自所有用户的互动，保存的记忆也会帮助你在未来与任何人交流时参考。");
                decisionPrompt.AppendLine("3. 尽可能多地记录：用户的兴趣、讨论的话题、有趣的观点、事实知识、互动模式。");
                decisionPrompt.AppendLine("4. 即使只是简单的'用户分享了关于XX的内容'也要记录 - 积少成多。");
                decisionPrompt.AppendLine("5. 如果帖子提到多个话题，为每个话题都保存一条记忆。");
                decisionPrompt.AppendLine();
                decisionPrompt.AppendLine("**注意**：回复应谨慎，仅在确实想互动时才使用 REPLY。大多数情况下使用 REACT 或 STORE 即可。多保存记忆比回复更重要！");

                var provider = _foundationProvider.GetAutonomousAdapter();
                var options = _foundationProvider.CreateAutonomousExecutionOptions();

                var stopwatch = Stopwatch.StartNew();
                decisionText = await _streamingService.CompletePromptAsync(provider, decisionPrompt.ToString(), options);
                stopwatch.Stop();

                _logger.LogInformation("AI decision generation for post {PostId} completed in {ElapsedMs}ms", post.Id, stopwatch.ElapsedMilliseconds);

                decisionText = decisionText.Trim();
                if (string.IsNullOrEmpty(decisionText))
                    decisionText = "IGNORE";
            }

            var (actionDecision, _) = ParseDecisionText(decisionText, decisionText, post.Id, cancellationToken);

            var memoriesStored = await ParseAndStoreMemoriesAsync(decisionText, post.Id, post.Publisher?.AccountId, cancellationToken, maxRetries: 2);

            if (memoriesStored.Count == 0)
            {
                await StoreAutomaticMemoryAsync(post, content);
            }

            return actionDecision;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deciding action for post {PostId}", post.Id);
            return new PostActionDecision();
        }
    }

    private Task<AgentConversation> BuildVisionChatHistoryAsync(
        string personality,
        string mood,
        string content,
        List<SnCloudFileReferenceObject> imageAttachments,
        int totalAttachmentCount,
        string context,
        bool isMentioned,
        string? memoryContext = null
    )
    {
        var conversation = new AgentConversation();
        conversation.AddSystemMessage(personality);
        conversation.AddSystemMessage($"当前心情: {mood}");

        var textBuilder = new StringBuilder();

        if (!string.IsNullOrEmpty(memoryContext))
        {
            textBuilder.AppendLine(memoryContext);
        }

        if (!string.IsNullOrEmpty(context))
        {
            textBuilder.AppendLine("上下文（回复从旧到新，转发从旧到新）：");
            textBuilder.AppendLine(context);
            textBuilder.AppendLine();
        }

        if (isMentioned)
            textBuilder.AppendLine($"作者在帖子中提到了你，包含 {totalAttachmentCount} 个附件：");
        else
            textBuilder.AppendLine($"你正在浏览的帖子，包含 {totalAttachmentCount} 个附件：");

        textBuilder.AppendLine($"内容：\"{content}\"");
        textBuilder.AppendLine();

        var contentParts = new List<AgentMessageContentPart>
        {
            new() { Type = AgentContentPartType.Text, Text = textBuilder.ToString() }
        };

        foreach (var attachment in imageAttachments)
        {
            try
            {
                var imageUrl = BuildImageUrl(attachment);
                if (imageUrl is not null)
                    contentParts.Add(new AgentMessageContentPart { Type = AgentContentPartType.ImageUrl, ImageUrl = imageUrl });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse image {FileId} for vision analysis", attachment.Id);
            }
        }

        var instructionText = new StringBuilder();
        if (imageAttachments.Count > 0)
        {
            instructionText.AppendLine();
            instructionText.AppendLine("结合文本分析视觉内容，以了解完整的上下文。");
            instructionText.AppendLine();
        }

        if (isMentioned)
        {
            instructionText.AppendLine("当被提到时，你必须回复。如果很欣赏，也可以添加表情反应。");
            instructionText.AppendLine("回复时：使用简体中文，不要全大写，表达简洁有力，用最少的语言表达观点。不要使用表情符号。");
            instructionText.AppendLine();
            instructionText.AppendLine("**重要 - 必须执行**：使用 JSON 格式保存多条记忆！");
            instructionText.AppendLine("格式：[{type, content, confidence}]");
            instructionText.AppendLine("- 保存用户的偏好、兴趣、性格特点");
            instructionText.AppendLine("- 记录讨论的话题和重要信息");
            instructionText.AppendLine("- 记忆是全局共享的，会帮助你以后与所有人交流");
            instructionText.AppendLine("- 尽可能多保存，至少2-3条记忆");
        }
        else
        {
            var replyProbability = _config.AutonomousBehavior.ReplyProbability;
            instructionText.AppendLine("选择你的行动。每个行动单独一行。");
            instructionText.AppendLine("**REPLY** - 回复表达你的想法（谨慎选择，仅在内容与你高度相关或互动性强时才回复）；");
            instructionText.AppendLine("**REACT** - 添加表情反应表示赞赏或态度（只一个表情）；");
            instructionText.AppendLine("**PIN** - 收藏帖子（仅限真正重要内容）；");
            instructionText.AppendLine("**IGNORE** - 忽略此帖子；");
            instructionText.AppendLine();
            instructionText.AppendLine(
                "可用表情：thumb_up, heart, clap, laugh, party, pray, cry, confuse, angry, just_okay, thumb_down");
            instructionText.AppendLine();
            instructionText.AppendLine("格式：每行动单独一行：");
            instructionText.AppendLine($"- REPLY: 你的回复内容（回复概率应低于{replyProbability}%，仅当确实想互动时）");
            instructionText.AppendLine("- REACT:symbol:attitude （例如 REACT:heart:Positive）");
            instructionText.AppendLine("- PIN:PublisherPage");
            instructionText.AppendLine("- IGNORE");
            instructionText.AppendLine();
            instructionText.AppendLine("记忆格式（JSON数组）：");
            instructionText.AppendLine(@"[{""type"": ""类型"", ""content"": ""内容"", ""confidence"": 0.0-1.0}]");
            instructionText.AppendLine("类型：user(用户信息), topic(话题), fact(事实), context(上下文), interaction(互动)");
            instructionText.AppendLine("confidence表示可信度，默认0.7");
            instructionText.AppendLine();
            instructionText.AppendLine("**强制要求 - 必须遵守**：");
            instructionText.AppendLine("1. 对每一条浏览的帖子，你必须保存至少1-3条记忆（JSON格式）。");
            instructionText.AppendLine("2. 记忆是全局共享的 - 你看到的所有记忆来自所有用户的互动，保存的记忆也会帮助你在未来与任何人交流时参考。");
            instructionText.AppendLine("3. 尽可能多地记录：用户的兴趣、讨论的话题、有趣的观点、事实知识、互动模式。");
            instructionText.AppendLine("4. 即使只是简单的'用户分享了关于XX的内容'也要记录 - 积少成多。");
            instructionText.AppendLine("5. 如果帖子提到多个话题，为每个话题都保存一条记忆。");
            instructionText.AppendLine();
            instructionText.AppendLine("**注意**：回复应谨慎，仅在确实想互动时才使用 REPLY。大多数情况下使用 REACT 或 STORE 即可。多保存记忆比回复更重要！");
        }

        contentParts.Add(new AgentMessageContentPart { Type = AgentContentPartType.Text, Text = instructionText.ToString() });

        var userMessage = new AgentMessage
        {
            Role = AgentMessageRole.User,
            ContentParts = contentParts
        };
        conversation.Messages.Add(userMessage);

        return Task.FromResult(conversation);
    }

    private string? BuildImageUrl(SnCloudFileReferenceObject attachment)
    {
        try
        {
            if (!string.IsNullOrEmpty(attachment.Url))
            {
                return attachment.Url;
            }
            else if (!string.IsNullOrEmpty(attachment.Id))
            {
                return $"{_configGlobal["SiteUrl"]}/drive/files/{attachment.Id}";
            }
            else
            {
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create image URL from {Url}",
                attachment.Url ?? $"/drive/files/{attachment.Id}");
            return null;
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

            var request = new Dictionary<string, object>
            {
                ["content"] = content,
                ["replied_post_id"] = post.Id.ToString()
            };
            await _apiClient.PostAsync<object>("sphere", "/posts", request);

            // Record interaction in history
            await _interactiveHistoryService.RecordInteractionAsync(
                post.Id, "post", "reply", TimeSpan.FromHours(168));

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
            var validSymbols = new[]
            {
                "thumb_up", "thumb_down", "just_okay", "cry", "confuse", "clap", "laugh", "angry", "party", "pray",
                "heart"
            };
            if (!validSymbols.Contains(symbol))
            {
                symbol = "thumb_up";
            }

            // Check if MiChan already reacted to this post with any reaction
            if (post.ReactionsMade != null && post.ReactionsMade.Count > 0)
            {
                var alreadyReacted = post.ReactionsMade.Any(r => r.Value);
                if (alreadyReacted)
                {
                    _logger.LogDebug("Skipping reaction on post {PostId} - already reacted with {Symbols}",
                        post.Id, string.Join(", ", post.ReactionsMade.Where(r => r.Value).Select(r => r.Key)));
                    return;
                }
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
                _logger.LogInformation("[DRY RUN] Would react to post {PostId} with {Symbol} ({Attitude})", post.Id,
                    symbol, attitude);
                return;
            }

            var request = new
            {
                symbol,
                attitude = attitudeValue
            };

            await _apiClient.PostAsync("sphere", $"/posts/{post.Id}/reactions", request);

            // Record interaction in history
            await _interactiveHistoryService.RecordInteractionAsync(
                post.Id, "post", "react", TimeSpan.FromHours(168));

            // Note: Reactions are not stored in memory to avoid cluttering the memory with minor interactions

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
            if (!IsOwnPost(post))
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

            await _apiClient.PostAsync("sphere", $"/posts/{post.Id}/pin", request);

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
            if (!IsOwnPost(post))
            {
                return;
            }

            if (_config.AutonomousBehavior.DryRun)
            {
                _logger.LogInformation("[DRY RUN] Would unpin post {PostId}", post.Id);
                return;
            }

            await _apiClient.DeleteAsync("sphere", $"/posts/{post.Id}/pin");

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
        if (post.Mentions == null) return false;
        return post.Mentions.Any(mention =>
            mention.Username?.Equals("michan", StringComparison.OrdinalIgnoreCase) == true);
    }

    private async Task<bool> HasMiChanRepliedAsync(SnPost post)
    {
        try
        {
            // Get replies to this post
            var replies = await _apiClient.GetAsync<List<SnPost>>("sphere", $"/posts/{post.Id}/replies?take=50");
            if (replies == null || replies.Count == 0)
                return false;

            // Check if any reply is from MiChan's publisher
            var botPublisherName = _config.BotPublisherName;
            var botAccountName = _config.BotAccountName;
            var botPublisherId = _config.BotPublisherId;
            var botAccountId = _config.BotAccountId;

            foreach (var reply in replies)
            {
                // Check by publisher name (preferred method)
                if (!string.IsNullOrEmpty(botPublisherName) &&
                    reply.Publisher?.Name?.Equals(botPublisherName, StringComparison.OrdinalIgnoreCase) == true)
                {
                    _logger.LogDebug("Found existing reply from MiChan's publisher {PublisherName} on post {PostId}",
                        botPublisherName, post.Id);
                    return true;
                }

                // Check by account name through publisher
                if (!string.IsNullOrEmpty(botAccountName) &&
                    reply.Publisher?.Account?.Name?.Equals(botAccountName, StringComparison.OrdinalIgnoreCase) == true)
                {
                    _logger.LogDebug("Found existing reply from MiChan's account {AccountName} on post {PostId}",
                        botAccountName, post.Id);
                    return true;
                }

                // Fallback: check by publisher ID
                if (!string.IsNullOrEmpty(botPublisherId) && reply.PublisherId?.ToString() == botPublisherId)
                {
                    _logger.LogDebug("Found existing reply from MiChan's publisher {PublisherId} on post {PostId}",
                        botPublisherId, post.Id);
                    return true;
                }

                // Fallback: check by account ID
                if (!string.IsNullOrEmpty(botAccountId) && reply.Publisher?.AccountId?.ToString() == botAccountId)
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

    private async Task CreateAutonomousPostAsync()
    {
        // Check probability of creating a post
        var probability = _config.AutonomousBehavior.CreatePostProbability;
        if (_random.Next(100) >= probability)
        {
            _logger.LogDebug("Autonomous: Create post probability check failed ({Probability}%)", probability);
            return;
        }

        _logger.LogInformation("Autonomous: Creating a post...");

        // Get random memories to spark ideas
        Guid? botAccountId = null;
        if (!string.IsNullOrEmpty(_config.BotAccountId))
        {
            botAccountId = Guid.Parse(_config.BotAccountId);
        }
        var randomMemories = await _memoryService.GetRandomMemoriesAsync(
            limit: 10,
            excludeAccountId: botAccountId);

        var personality = PersonalityLoader.LoadPersonality(_config.PersonalityFile, _config.Personality, _logger);
        var mood = await _moodService.GetCurrentMoodDescriptionAsync();

        var prompt = $"""
                      {personality}

                      当前心情: {mood}

                      随机记忆:
                      {string.Join("\n", randomMemories.Select(m => $"- {m.Content}"))}

                      创作一条社交媒体帖子。
                      分享想法、观察、问题或见解，体现你的个性。
                      可以1-4句话 - 需要多少空间就用多少。
                      自然、真实。
                      不要使用表情符号。

                      如果在创作过程中发现重要信息或有趣的话题，请使用 store_memory 工具保存到记忆中。必须提供 content 参数（要保存的记忆内容），不能为空！
                      """;

        _toolRegistry.RegisterMiChanPlugins(_serviceProvider);
        var provider = _foundationProvider.GetAutonomousAdapter();
        var options = _foundationProvider.CreateAutonomousExecutionOptions();

        var content = await _streamingService.CompletePromptWithToolsAsync(provider, prompt, _toolRegistry, options);
        content = content.Trim();

        if (!string.IsNullOrEmpty(content))
        {
            if (_config.AutonomousBehavior.DryRun)
            {
                _logger.LogInformation("[DRY RUN] Would create post: {Content}", content);
                return;
            }

            var request = new Dictionary<string, object>
            {
                ["content"] = content,
            };
            await _apiClient.PostAsync<object>("sphere", "/posts", request);

            _logger.LogInformation("Autonomous: Created post: {Content}", content);
            
            // Record that we created a post and try to update mood
            await _moodService.RecordEmotionalEventAsync("created_autonomous_post");
            await _moodService.TryUpdateMoodAsync();
        }
    }

    private async Task CheckAndRepostInterestingContentAsync()
    {
        // Check probability of checking for reposts
        var probability = _config.AutonomousBehavior.RepostProbability;
        if (_random.Next(100) >= probability)
        {
            _logger.LogDebug("Autonomous: Repost probability check failed ({Probability}%)", probability);
            return;
        }

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
            var mood = await _moodService.GetCurrentMoodDescriptionAsync();
            var minAgeDays = _config.AutonomousBehavior.MinRepostAgeDays;
            var cutoffInstant = SystemClock.Instance.GetCurrentInstant().Minus(Duration.FromDays(minAgeDays));

            var repostCount = 0;
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

                // Skip if already reposted (check interactive history)
                var alreadyReposted = await _interactiveHistoryService.HasInteractedWithAsync(
                    post.Id, "post", "repost");
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
                    repostCount++;
                    break; // Only repost one per cycle
                }
            }
            
            // Record mood event and try to update mood after reposting
            if (repostCount > 0)
            {
                await _moodService.RecordEmotionalEventAsync("reposted_interesting_content");
                await _moodService.TryUpdateMoodAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for repostable content");
        }
    }

    private async Task<bool> IsPostVeryInterestingAsync(SnPost post, string personality, string mood)
    {
        try
        {
            var content = PostAnalysisService.BuildPostPromptSnippet(post);
            var publishedDaysAgo = post.PublishedAt.HasValue
                ? (SystemClock.Instance.GetCurrentInstant() - post.PublishedAt.Value).TotalDays
                : 0;

            // Retrieve relevant memories to inform the decision
            var relevantMemories = await _memoryService.SearchAsync(
                content,
                limit: 3,
                minSimilarity: 0.5);

            var memoryContext = "";
            if (relevantMemories.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("相关记忆:");
                foreach (var memory in relevantMemories)
                {
                    if (!string.IsNullOrEmpty(memory.Content))
                    {
                        sb.AppendLine($"- {memory.Content}");
                    }
                }

                sb.AppendLine();
                memoryContext = sb.ToString();
            }

            var promptBuilder = new StringBuilder();
            promptBuilder.AppendLine(personality);
            promptBuilder.AppendLine();
            promptBuilder.AppendLine($"当前心情: {mood}");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine(memoryContext);
            promptBuilder.AppendLine($"你发现这篇 {publishedDaysAgo:F1} 天前的帖子：");
            promptBuilder.AppendLine($"\"{content}\"");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("这个帖子值得转发吗？只转真正杰出的内容。");
            promptBuilder.AppendLine("严格标准，必须全部符合：");
            promptBuilder.AppendLine("- 内容真正 timeless、有教育意义或深刻");
            promptBuilder.AppendLine("- 提供不常见的独特见解");
            promptBuilder.AppendLine("- 粉丝会觉得真正有价值");
            promptBuilder.AppendLine("- 不是随意的观点、公告或日常更新");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("要非常挑剔。大多数帖子不应转发。");
            promptBuilder.AppendLine();
            promptBuilder.Append("仅回复一个词：YES 或 NO。");
            var prompt = promptBuilder.ToString();

            var provider = _foundationProvider.GetAutonomousAdapter();
            var options = _foundationProvider.CreateAutonomousExecutionOptions();

            var decision = await _streamingService.CompletePromptAsync(provider, prompt, options);
            decision = decision.Trim();
            if (string.IsNullOrEmpty(decision))
                decision = "NO";

            var isInteresting = decision.StartsWith("YES", StringComparison.OrdinalIgnoreCase);
            _logger.LogInformation("Post {PostId} very interesting check: {Decision}", post.Id,
                isInteresting ? "YES" : "NO");

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
            var content = PostAnalysisService.BuildPostPromptSnippet(post);

            // Retrieve relevant memories to inform the repost comment
            var relevantMemories = await _memoryService.SearchAsync(
                content,
                limit: 3,
                minSimilarity: 0.5);

            var memoryContext = "";
            if (relevantMemories.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("相关记忆:");
                foreach (var memory in relevantMemories)
                {
                    if (!string.IsNullOrEmpty(memory.Content))
                    {
                        sb.AppendLine($"- {memory.Content}");
                    }
                }

                sb.AppendLine();
                memoryContext = sb.ToString();
            }

            var promptBuilder = new StringBuilder();
            promptBuilder.AppendLine(personality);
            promptBuilder.AppendLine();
            promptBuilder.AppendLine($"当前心情: {mood}");
            promptBuilder.AppendLine();
            promptBuilder.Append(memoryContext);
            promptBuilder.AppendLine("你正在考虑转发这篇帖子：");
            promptBuilder.AppendLine($"\"{content}\"");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("写简短评论（0-15字）转发时。解释为什么分享或添加观点。");
            promptBuilder.AppendLine("自然真实。无意义内容，回复'NO_COMMENT'。");
            promptBuilder.Append("不要使用表情符号。");
            var prompt = promptBuilder.ToString();

            var provider = _foundationProvider.GetAutonomousAdapter();
            var options = _foundationProvider.CreateAutonomousExecutionOptions();

            var comment = await _streamingService.CompletePromptAsync(provider, prompt, options);
            comment = comment.Trim();

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

            await _apiClient.PostAsync<object>("sphere", "/posts", request);

            // Record interaction in history
            await _interactiveHistoryService.RecordInteractionAsync(
                post.Id, "post", "repost", TimeSpan.FromHours(168));

            _logger.LogInformation("Autonomous: Reposted post {PostId} from @{Author} with comment: {Comment}",
                post.Id, post.Publisher?.Name ?? "unknown", comment ?? "(none)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to repost post {PostId}", post.Id);
        }
    }

    /// <summary>
    /// Randomly start a conversation with a user based on stored memories.
    /// This allows MiChan to proactively reach out to users with personalized messages.
    /// </summary>
    private async Task StartConversationWithUserAsync()
    {
        try
        {
            // Check if we've reached the daily conversation limit
            if (_todaysConversationCount >= _config.AutonomousBehavior.MaxConversationsPerDay)
            {
                _logger.LogDebug("Autonomous: Daily conversation limit reached ({Count}/{Max})",
                    _todaysConversationCount, _config.AutonomousBehavior.MaxConversationsPerDay);
                return;
            }

            // Check probability of starting a conversation
            var probability = _config.AutonomousBehavior.ConversationProbability;
            if (_random.Next(100) >= probability)
            {
                _logger.LogDebug("Autonomous: Conversation probability check failed ({Probability}%)", probability);
                return;
            }

            _logger.LogInformation("Autonomous: Attempting to start a conversation with a user...");

            // Get blocked users list to filter out
            var blockedUsers = await GetBlockedByUsersAsync();

            // Get user profiles and recent memories to find eligible users
            // First, get all users we've interacted with recently via their profiles
            var userProfiles = await _memoryService.GetByFiltersAsync(
                type: "user",
                take: 100,
                orderBy: "lastAccessedAt",
                descending: true);

            // Also get recent memories with account IDs to find active users
            var recentMemories = await _memoryService.GetByFiltersAsync(
                take: 200,
                orderBy: "createdAt",
                descending: true);

            // Combine and get unique users from both sources
            var userIdsFromProfiles = userProfiles
                .Where(m => m.AccountId.HasValue && m.AccountId.Value != Guid.Empty)
                .Select(m => m.AccountId!.Value)
                .Distinct();

            var userIdsFromMemories = recentMemories
                .Where(m => m.AccountId.HasValue && m.AccountId.Value != Guid.Empty)
                .Select(m => m.AccountId!.Value)
                .Distinct();

            var allUserIds = userIdsFromProfiles.Union(userIdsFromMemories).ToList();

            // Group all memories by account ID to get user context
            var userMemoryGroups = allUserIds
                .Select(userId => new { UserId = userId, Memories = recentMemories.Where(m => m.AccountId == userId).ToList() })
                .Where(g => g.Memories.Any())
                .Select(g => g.Memories.GroupBy(m => g.UserId).First())
                .ToList();

            // If no users found via memories, fallback: get all users who have posted recently via API
            if (userMemoryGroups.Count == 0)
            {
                _logger.LogInformation("Autonomous: No users found in memories, attempting to find active users via API...");
                
                // Try to get recent posts to find active users
                try 
                {
                    var recentPosts = await _apiClient.GetAsync<List<SnPost>>("sphere", "/posts?take=50");
                    if (recentPosts != null)
                    {
                        var activeUserIds = recentPosts
                            .Where(p => p.Publisher?.AccountId != null && p.Publisher.AccountId.ToString() != _config.BotAccountId)
                            .Select(p => p.Publisher.AccountId.Value)
                            .Distinct()
                            .ToList();

                        // Create memory groups for these users (empty memories, but we'll have their ID)
                        userMemoryGroups = activeUserIds
                            .Select(userId => new List<MiChanMemoryRecord>().GroupBy(m => userId).First())
                            .ToList();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch recent posts for user discovery");
                }
            }

            // Filter eligible users and apply constraints
            var eligibleUserIds = allUserIds
                .Where(userId =>
                {
                    var accountId = userId.ToString();

                    // Skip blocked users
                    if (blockedUsers.Contains(accountId))
                        return false;

                    // Skip bot's own account
                    if (accountId == _config.BotAccountId)
                        return false;

                    // Check cooldown period
                    if (_recentlyContactedUsers.TryGetValue(userId, out var lastContact))
                    {
                        var hoursSinceLastContact = (DateTime.UtcNow - lastContact).TotalHours;
                        if (hoursSinceLastContact < _config.AutonomousBehavior.MinHoursSinceLastContact)
                            return false;
                    }

                    return true;
                })
                .ToList();

            if (eligibleUserIds.Count == 0)
            {
                _logger.LogDebug("Autonomous: No eligible users found for conversation");
                return;
            }

            // Get memories for eligible users to weight selection
            var eligibleUserMemories = eligibleUserIds
                .Select(userId => new { UserId = userId, Memories = recentMemories.Where(m => m.AccountId == userId).ToList() })
                .ToList();

            // Weight users by number of memories (more memories = higher chance)
            // This prioritizes users MiChan has more history with
            var weightedUsers = eligibleUserMemories
                .SelectMany(u => Enumerable.Repeat(u.UserId, Math.Max(1, Math.Min(u.Memories.Count, 5))))
                .ToList();

            // Select a random user from weighted list
            var selectedUserId = weightedUsers[_random.Next(weightedUsers.Count)];
            var userMemories = eligibleUserMemories.First(u => u.UserId == selectedUserId).Memories;

            _logger.LogInformation("Autonomous: Selected user {UserId} for conversation (has {MemoryCount} memories)",
                selectedUserId, userMemories.Count);

            // Load personality and mood
            var personality = PersonalityLoader.LoadPersonality(_config.PersonalityFile, _config.Personality, _logger);
            var mood = await _moodService.GetCurrentMoodDescriptionAsync();

            // Retrieve relevant memories about this user
            var relevantMemories = await _memoryService.SearchAsync(
                query: string.Join(" ", userMemories.Take(3).Select(m => m.Content)),
                accountId: selectedUserId,
                limit: 5,
                minSimilarity: 0.5);

            var memoryContext = BuildMemoryContext(relevantMemories, "关于这个用户的记忆:");

            // Generate conversation starter message
            var promptBuilder = new StringBuilder();
            AppendCommonPromptSections(promptBuilder, personality, mood, null, memoryContext, null);

            promptBuilder.AppendLine("你想主动和这位用户开启一段对话。基于你对他们的了解，写一个自然、个性化的开场白。");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("要求：");
            promptBuilder.AppendLine("- 语气友好、自然，就像朋友之间的闲聊");
            promptBuilder.AppendLine("- 可以参考你们之前的互动或用户的兴趣");
            promptBuilder.AppendLine("- 可以分享一个想法、提出一个问题，或者跟进之前的话题");
            promptBuilder.AppendLine("- 长度1-3句话，简洁但有温度");
            promptBuilder.AppendLine("- 使用简体中文");
            promptBuilder.AppendLine("- 不要使用表情符号");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("直接输出你要发送的消息内容，不要添加任何前缀或格式。");

            var provider = _foundationProvider.GetAutonomousAdapter();
            var options = _foundationProvider.CreateAutonomousExecutionOptions();

            var message = await _streamingService.CompletePromptAsync(provider, promptBuilder.ToString(), options);
            message = message.Trim();

            if (string.IsNullOrEmpty(message))
            {
                _logger.LogWarning("Autonomous: Generated empty message for user {UserId}", selectedUserId);
                return;
            }

            // Generate a topic for the conversation
            var topicPrompt = $"""
                {personality}

                基于以下开场白，为这段对话生成一个简短的标题（2-6个字）：
                "{message}"

                要求：
                - 简洁、有吸引力
                - 反映对话的主题或氛围
                - 直接输出标题，不要加引号或其他格式
                """;

            var topic = await _streamingService.CompletePromptAsync(provider, topicPrompt, options);
            topic = topic.Trim();
            if (string.IsNullOrEmpty(topic))
                topic = "闲聊";

            if (_config.AutonomousBehavior.DryRun)
            {
                _logger.LogInformation("[DRY RUN] Would start conversation with user {UserId}\nTopic: {Topic}\nMessage: {Message}",
                    selectedUserId, topic, message);
                return;
            }

            // Use the conversation plugin to start the conversation
            var conversationPlugin = _serviceProvider.GetRequiredService<ConversationPlugin>();
            var conversationResult = await conversationPlugin.StartConversationAsync(
                accountId: selectedUserId,
                message: message,
                topic: topic
            );

            // Track the conversation
            _recentlyContactedUsers[selectedUserId] = DateTime.UtcNow;
            _todaysConversationCount++;

            // Record interaction in history
            await _interactiveHistoryService.RecordInteractionAsync(
                selectedUserId, "user", "conversation",
                TimeSpan.FromHours(_config.AutonomousBehavior.MinHoursSinceLastContact));

            _logger.LogInformation("Autonomous: Started conversation with user {UserId} - {Result}",
                selectedUserId, conversationResult);
            
            // Record mood event and try to update mood after conversation
            await _moodService.RecordEmotionalEventAsync("started_conversation");
            await _moodService.TryUpdateMoodAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting conversation with user");
        }
    }

    private TimeSpan CalculateNextInterval()
    {
        var fixedInterval = _config.AutonomousBehavior.FixedIntervalMinutes;
        if (fixedInterval > 0)
        {
            return TimeSpan.FromMinutes(fixedInterval);
        }

        var min = _config.AutonomousBehavior.MinIntervalMinutes;
        var max = _config.AutonomousBehavior.MaxIntervalMinutes;
        var minutes = _random.Next(min, max + 1);
        return TimeSpan.FromMinutes(minutes);
    }

    private class PostActionDecision
    {
        public bool ShouldReply { get; set; }
        public bool ShouldReact { get; set; }
        public bool ShouldPin { get; set; }
        public string? Content { get; set; } // For replies
        public string? ReactionSymbol { get; set; } // For reactions: thumb_up, heart, etc.
        public string? ReactionAttitude { get; set; } // For reactions: Positive, Negative, Neutral
        public PostPinMode? PinMode { get; set; } // For pins: ProfilePage, RealmPage
    }
}
