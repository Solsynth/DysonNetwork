#pragma warning disable SKEXP0050
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using DysonNetwork.Insight.MiChan.Plugins;
using DysonNetwork.Insight.Thought.Memory;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using NodaTime;
using PostPinMode = DysonNetwork.Shared.Models.PostPinMode;

namespace DysonNetwork.Insight.MiChan;

public class MiChanAutonomousBehavior
{
    private readonly IConfiguration _configGlobal;
    private readonly MiChanConfig _config;
    private readonly ILogger<MiChanAutonomousBehavior> _logger;
    private readonly SolarNetworkApiClient _apiClient;
    private readonly MiChanKernelProvider _kernelProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly AccountService.AccountServiceClient _accountClient;
    private readonly PostAnalysisService _postAnalysisService;
    private readonly MemoryService _memoryService;
    private Kernel? _kernel;

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

    public MiChanAutonomousBehavior(
        MiChanConfig config,
        ILogger<MiChanAutonomousBehavior> logger,
        SolarNetworkApiClient apiClient,
        MiChanKernelProvider kernelProvider,
        IServiceProvider serviceProvider,
        AccountService.AccountServiceClient accountClient,
        PostAnalysisService postAnalysisService,
        IConfiguration configGlobal,
        MemoryService memoryService
    )
    {
        _config = config;
        _logger = logger;
        _apiClient = apiClient;
        _kernelProvider = kernelProvider;
        _serviceProvider = serviceProvider;
        _accountClient = accountClient;
        _postAnalysisService = postAnalysisService;
        _configGlobal = configGlobal;
        _memoryService = memoryService;
        _nextInterval = CalculateNextInterval();
        _mentionRegex = new Regex("@michan\\b", RegexOptions.IgnoreCase);
    }

    [Experimental("SKEXP0050")]
    public Task InitializeAsync()
    {
        _kernel = _kernelProvider.GetKernel();

        // Register plugins (only if not already registered)
        var postPlugin = _serviceProvider.GetRequiredService<PostPlugin>();
        var accountPlugin = _serviceProvider.GetRequiredService<AccountPlugin>();
        var memoryPlugin = _serviceProvider.GetRequiredService<MemoryPlugin>();

        if (!_kernel.Plugins.Contains("post"))
            _kernel.Plugins.AddFromObject(postPlugin, "post");
        if (!_kernel.Plugins.Contains("account"))
            _kernel.Plugins.AddFromObject(accountPlugin, "account");
        if (!_kernel.Plugins.Contains("memory"))
            _kernel.Plugins.AddFromObject(memoryPlugin, "memory");

        _logger.LogInformation("MiChan autonomous behavior initialized");

        return Task.CompletedTask;
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

    /// <summary>
    /// Checks if a post was created by MiChan herself
    /// </summary>
    private bool IsOwnPost(SnPost post)
    {
        // Check by PublisherId
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

            var request = new ListRelationshipSimpleRequest
            {
                RelatedId = _config.BotAccountId
            };

            var response = await _accountClient.ListBlockedAsync(request);
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

    [Experimental("SKEXP0050")]
    private async Task CheckAndInteractWithPostsAsync()
    {
        _logger.LogInformation("Autonomous: Checking posts...");

        // Get blocked users list (cached)
        var blockedUsers = await GetBlockedByUsersAsync();

        var personality = PersonalityLoader.LoadPersonality(_config.PersonalityFile, _config.Personality, _logger);
        var mood = _config.AutonomousBehavior.PersonalityMood;

        // Reset pagination for each autonomous cycle
        _currentPageIndex = 0;

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
                    continue;
                }

                // Skip posts from users who blocked MiChan
                var authorAccountId = post.Publisher?.AccountId?.ToString();
                if (!string.IsNullOrEmpty(authorAccountId) && blockedUsers.Contains(authorAccountId))
                {
                    _logger.LogInformation("Skipping post {PostId} from user {UserId} - user has blocked MiChan",
                        post.Id, authorAccountId);
                    continue;
                }

                // Skip posts MiChan already replied to
                var alreadyReplied = await HasMiChanRepliedAsync(post);
                if (alreadyReplied)
                {
                    _logger.LogDebug("Skipping post {PostId} - already replied by MiChan", post.Id);
                    continue;
                }

                // Skip posts MiChan already reacted to
                if (post.ReactionsMade != null && post.ReactionsMade.Any(r => r.Value))
                {
                    _logger.LogDebug("Skipping post {PostId} - already reacted by MiChan", post.Id);
                    continue;
                }

                // Check if mentioned
                var isMentioned = ContainsMention(post);

                // MiChan decides what to do with this post
                var decision = await DecidePostActionAsync(post, isMentioned, personality, mood);

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

                processedCount++;

                // If mentioned, prioritize and stop processing this page
                if (isMentioned)
                {
                    _logger.LogInformation("Autonomous: Detected mention in post {PostId}", post.Id);
                    mentionFound = true;
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
        string? context)
    {
        builder.AppendLine(personality);
        builder.AppendLine();
        builder.AppendLine($"当前心情: {mood}");
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
    /// Get vision model response with error handling
    /// </summary>
    private async Task<string> GetVisionResponseAsync(
        ChatHistory chatHistory,
        Guid postId)
    {
        try
        {
            var visionKernel = _kernelProvider.GetVisionKernel();
            var visionSettings = _kernelProvider.CreateVisionPromptExecutionSettings();
            var chatCompletionService = visionKernel.GetRequiredService<IChatCompletionService>();
            var reply = await chatCompletionService.GetChatMessageContentAsync(chatHistory, visionSettings);
            return reply.Content?.Trim() ?? "IGNORE";
        }
        catch (HttpOperationException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogError(ex,
                "Vision model service '{VisionService}' not found. Check that the service is configured in Thinking:Services configuration. Post {PostId}",
                _config.Vision.VisionThinkingService, postId);
            throw new InvalidOperationException(
                $"Vision model service '{_config.Vision.VisionThinkingService}' not found. Ensure it is configured in Thinking:Services with correct endpoint, model name, and API key.",
                ex);
        }
    }

    /// <summary>
    /// Represents a STORE action to be processed
    /// </summary>
    private record StoreAction(string Type, string Content);

    /// <summary>
    /// Parse decision text into PostActionDecision and extract store actions
    /// </summary>
    private (PostActionDecision Decision, List<StoreAction> StoreActions) ParseDecisionText(string decision, Guid postId)
    {
        _logger.LogInformation("AI decision for post {PostId}: {Decision}", postId, decision);

        var actionDecision = new PostActionDecision();
        var storeActions = new List<StoreAction>();
        var lines = decision.Split('\n').Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)).ToList();

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
                // Only process the first REACT, skip additional ones
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
            else if (line.StartsWith("STORE:", StringComparison.OrdinalIgnoreCase))
            {
                // Parse STORE:type:content format
                var storeContent = line["STORE:".Length..].Trim();
                var colonIndex = storeContent.IndexOf(':');
                if (colonIndex > 0)
                {
                    var type = storeContent[..colonIndex].Trim().ToLower();
                    var content = storeContent[(colonIndex + 1)..].Trim();

                    if (!string.IsNullOrEmpty(type) && !string.IsNullOrEmpty(content))
                    {
                        storeActions.Add(new StoreAction(type, content));
                    }
                }
            }
            else if (line.Equals("IGNORE", StringComparison.OrdinalIgnoreCase))
            {
                // IGNORE explicitly means no action
            }
        }

        return (actionDecision, storeActions);
    }

    /// <summary>
    /// Process store actions by saving them to memory
    /// </summary>
    private async Task ProcessStoreActionsAsync(List<StoreAction> storeActions, Guid? accountId)
    {
        foreach (var action in storeActions)
        {
            try
            {
                await _memoryService.StoreMemoryAsync(
                    type: action.Type,
                    content: action.Content,
                    confidence: 0.7f,
                    accountId: accountId,
                    hot: false);
                _logger.LogDebug("Stored memory from decision: type={Type}, content={Content}",
                    action.Type, action.Content[..Math.Min(action.Content.Length, 100)]);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to store memory from decision: type={Type}", action.Type);
            }
        }
    }

    private async Task<PostActionDecision> DecidePostActionAsync(SnPost post, bool isMentioned, string personality,
        string mood)
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
                promptBuilder.AppendLine("如果发现重要信息或用户偏好，请使用 store_memory 工具保存到记忆中。");

                var executionSettings = _kernelProvider.CreatePromptExecutionSettings();
                var kernelArgs = new KernelArguments(executionSettings);
                var result = await _kernel!.InvokePromptAsync(promptBuilder.ToString(), kernelArgs);
                var textReplyContent = result.GetValue<string>()?.Trim();

                return new PostActionDecision { ShouldReply = true, Content = textReplyContent };
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
                decisionPrompt.AppendLine("选择你的行动。每个行动单独一行。可以同时回复和反应！");
                decisionPrompt.AppendLine("**REPLY** - 回复表达你的想法。鼓励互动交流！");
                decisionPrompt.AppendLine("**REACT** - 添加表情反应表示赞赏或态度（只一个表情）。");
                decisionPrompt.AppendLine("**PIN** - 收藏帖子（仅限真正重要内容）");
                decisionPrompt.AppendLine("**IGNORE** - 忽略此帖子");
                decisionPrompt.AppendLine("**STORE** - 使用 store_memory 工具保存重要信息到记忆中");
                decisionPrompt.AppendLine();
                decisionPrompt.AppendLine(
                    "可用表情：thumb_up, heart, clap, laugh, party, pray, cry, confuse, angry, just_okay, thumb_down");
                decisionPrompt.AppendLine();
                decisionPrompt.AppendLine("格式：每行动单独一行：");
                decisionPrompt.AppendLine("- REPLY: 你的回复内容");
                decisionPrompt.AppendLine("- REACT:symbol:attitude （例如 REACT:heart:Positive）");
                decisionPrompt.AppendLine("- PIN:PublisherPage");
                decisionPrompt.AppendLine("- IGNORE");
                decisionPrompt.AppendLine("- STORE: 你想要保存的记忆内容");
                decisionPrompt.AppendLine();
                decisionPrompt.AppendLine("示例：");
                decisionPrompt.AppendLine("REPLY: 这个很有意思！我也在想这个。");
                decisionPrompt.AppendLine("REACT:heart:Positive");
                decisionPrompt.AppendLine("REPLY: 我完全同意你的观点。");
                decisionPrompt.AppendLine("REACT:clap:Positive");
                decisionPrompt.AppendLine("IGNORE");
                decisionPrompt.AppendLine("STORE: 用户喜欢分享关于AI技术的帖子");
                decisionPrompt.AppendLine();
                decisionPrompt.AppendLine("重要：如果发现重要信息、用户偏好、关键事实，请使用 STORE 行动保存到记忆中。");

                var decisionSettings = _kernelProvider.CreatePromptExecutionSettings();
                var decisionResult = await _kernel!.InvokePromptAsync(decisionPrompt.ToString(), new KernelArguments(decisionSettings));
                decisionText = decisionResult.GetValue<string>()?.Trim() ?? "IGNORE";
            }

            var (actionDecision, storeActions) = ParseDecisionText(decisionText, post.Id);

            // Process any STORE actions
            if (storeActions.Count > 0)
            {
                await ProcessStoreActionsAsync(storeActions, post.Publisher?.AccountId);
            }

            return actionDecision;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deciding action for post {PostId}", post.Id);
            return new PostActionDecision();
        }
    }

    /// <summary>
    /// Build a ChatHistory with images for vision analysis
    /// </summary>
    private Task<ChatHistory> BuildVisionChatHistoryAsync(
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
        var chatHistory = new ChatHistory(personality);
        chatHistory.AddSystemMessage($"当前心情: {mood}");

        // Build the text part of the message
        var textBuilder = new StringBuilder();

        // Add relevant memories
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

        // Create a collection to hold all content items (text + images)
        var contentItems = new ChatMessageContentItemCollection { new TextContent(textBuilder.ToString()) };

        // Download and add images
        foreach (var attachment in imageAttachments)
        {
            try
            {
                var imageContext = BuildImageContent(attachment);
                if (imageContext is not null)
                    contentItems.Add(imageContext);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse image {FileId} for vision analysis", attachment.Id);
            }
        }

        // Add instruction text after images
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
            instructionText.AppendLine("如果发现重要信息或用户偏好，请使用 store_memory 工具保存到记忆中。");
        }
        else
        {
            instructionText.AppendLine("选择你的行动。每个行动单独一行。可以同时回复和反应！");
            instructionText.AppendLine("**REPLY** - 回复表达你的想法。鼓励互动交流！");
            instructionText.AppendLine("**REACT** - 添加表情反应表示赞赏或态度（只一个表情）。");
            instructionText.AppendLine("**PIN** - 收藏帖子（仅限真正重要内容）");
            instructionText.AppendLine("**IGNORE** - 忽略此帖子");
            instructionText.AppendLine("**STORE** - 使用 store_memory 工具保存重要信息到记忆中");
            instructionText.AppendLine();
            instructionText.AppendLine(
                "可用表情：thumb_up, heart, clap, laugh, party, pray, cry, confuse, angry, just_okay, thumb_down");
            instructionText.AppendLine();
            instructionText.AppendLine("格式：每行动单独一行：");
            instructionText.AppendLine("- REPLY: 你的回复内容");
            instructionText.AppendLine("- REACT:symbol:attitude （例如 REACT:heart:Positive）");
            instructionText.AppendLine("- PIN:PublisherPage");
            instructionText.AppendLine("- IGNORE");
            instructionText.AppendLine("- STORE:类型:你想要保存的记忆内容（类型可以为 user, summary, context 等）");
            instructionText.AppendLine();
            instructionText.AppendLine("示例：");
            instructionText.AppendLine("REPLY: 这个很有意思！我也在想这个。");
            instructionText.AppendLine("REACT:heart:Positive");
            instructionText.AppendLine("IGNORE");
            instructionText.AppendLine("STORE:user:用户喜欢分享关于AI技术的帖子");
            instructionText.AppendLine();
            instructionText.AppendLine("重要：如果发现重要信息、用户偏好、关键事实，请使用 STORE 行动保存到记忆中。");
        }

        contentItems.Add(new TextContent(instructionText.ToString()));

        // Create a ChatMessageContent with all items and add it to history
        var userMessage = new ChatMessageContent
        {
            Role = AuthorRole.User,
            Items = contentItems
        };
        chatHistory.Add(userMessage);

        return Task.FromResult(chatHistory);
    }

    /// <summary>
    /// Download image bytes from the drive service
    /// </summary>
    private ImageContent? BuildImageContent(SnCloudFileReferenceObject attachment)
    {
        try
        {
            string url;
            if (!string.IsNullOrEmpty(attachment.Url))
            {
                url = attachment.Url;
            }
            else if (!string.IsNullOrEmpty(attachment.Id))
            {
                // Build URL from gateway + file ID
                url = $"{_configGlobal["SiteUrl"]}/drive/files/{attachment.Id}";
            }
            else
            {
                return null;
            }

            return new ImageContent(new Uri(url));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create image context from {Url}",
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
            if (post.PublisherId?.ToString() != _config.BotPublisherId)
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
            if (post.Publisher?.AccountId?.ToString() != _config.BotAccountId)
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
            var botPublisherId = _config.BotPublisherId;
            var botAccountId = _config.BotAccountId;

            foreach (var reply in replies)
            {
                // Check by publisher ID (most reliable)
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

    [Experimental("SKEXP0050")]
    private async Task CreateAutonomousPostAsync()
    {
        _logger.LogInformation("Autonomous: Creating a post...");

        var recentInteractions = await _memoryService.GetByFiltersAsync(
            type: "autonomous",
            take: 10,
            orderBy: "createdAt",
            descending: true);

        var interests = recentInteractions
            .SelectMany(m => m.Content.Split('\n'))
            .Where(line => line.Contains("topics"))
            .Select(line => line.Split(':').LastOrDefault()?.Trim() ?? "")
            .Where(topic => !string.IsNullOrEmpty(topic))
            .Distinct()
            .Take(5)
            .ToList();

        // Retrieve relevant memories to spark ideas
        var relevantMemories = await _memoryService.SearchAsync(
            string.Join(" ", interests),
            limit: 5,
            minSimilarity: 0.5);

        var personality = PersonalityLoader.LoadPersonality(_config.PersonalityFile, _config.Personality, _logger);
        var mood = _config.AutonomousBehavior.PersonalityMood;

        var prompt = $"""
                      {personality}

                      当前心情: {mood}
                      最近关注: {string.Join(", ", interests)}

                      相关记忆:
                      {string.Join("\n", relevantMemories.Take(3).Select(m => $"- {m.Content}"))}

                      创作一条社交媒体帖子。
                      分享想法、观察、问题或见解，体现你的个性。
                      可以1-4句话 - 需要多少空间就用多少。
                      自然、真实。
                      不要使用表情符号。

                      如果在创作过程中发现重要信息或有趣的话题，请使用 store_memory 工具保存到记忆中。
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

            var request = new Dictionary<string, object>
            {
                ["content"] = content,
            };
            await _apiClient.PostAsync<object>("sphere", "/posts", request);

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
                var repostInteractions = await _memoryService.GetByFiltersAsync(
                    type: "repost",
                    take: 100,
                    orderBy: "createdAt",
                    descending: true);
                var alreadyReposted = repostInteractions.Any(i =>
                    i.Content.Contains(post.Id.ToString()));
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

            var executionSettings = _kernelProvider.CreatePromptExecutionSettings();
            var result = await _kernel!.InvokePromptAsync(prompt, new KernelArguments(executionSettings));
            var decision = result.GetValue<string>()?.Trim() ?? "NO";

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

            await _apiClient.PostAsync<object>("sphere", "/posts", request);

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
        public bool ShouldReply { get; set; }
        public bool ShouldReact { get; set; }
        public bool ShouldPin { get; set; }
        public string? Content { get; set; } // For replies
        public string? ReactionSymbol { get; set; } // For reactions: thumb_up, heart, etc.
        public string? ReactionAttitude { get; set; } // For reactions: Positive, Negative, Neutral
        public PostPinMode? PinMode { get; set; } // For pins: ProfilePage, RealmPage
    }
}

#pragma warning restore SKEXP0050