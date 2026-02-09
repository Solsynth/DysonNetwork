#pragma warning disable SKEXP0050
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
using DysonNetwork.Insight.MiChan.Plugins;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
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
    private readonly AccountService.AccountServiceClient _accountClient;
    private readonly HttpClient _httpClient;
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
    private int _currentPageIndex = 0;
    private const int MaxPageIndex = 10;
    private const int PageSize = 30;

    private readonly PostPlugin _postPlugin;

    public MiChanAutonomousBehavior(
        MiChanConfig config,
        ILogger<MiChanAutonomousBehavior> logger,
        SolarNetworkApiClient apiClient,
        MiChanMemoryService memoryService,
        MiChanKernelProvider kernelProvider,
        IServiceProvider serviceProvider,
        AccountService.AccountServiceClient accountClient,
        PostPlugin postPlugin)
    {
        _config = config;
        _logger = logger;
        _apiClient = apiClient;
        _memoryService = memoryService;
        _kernelProvider = kernelProvider;
        _serviceProvider = serviceProvider;
        _accountClient = accountClient;
        _postPlugin = postPlugin;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("AtField", config.AccessToken);
        _nextInterval = CalculateNextInterval();
        _mentionRegex = new Regex($"@michan\\b", RegexOptions.IgnoreCase);
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
        if (post == null)
            return false;

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

            _logger.LogInformation("Fetched blocked users list: {Count} users have blocked MiChan", _cachedBlockedUsers.Count);
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
            _logger.LogInformation("Autonomous: Fetching posts page {PageIndex}/{MaxPages}", _currentPageIndex, MaxPageIndex);

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

                // Execute reply if decided
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
                if (decision.ShouldPin && decision.PinMode.HasValue)
                {
                    await PinPostAsync(post, decision.PinMode.Value);
                }

                if (!decision.ShouldReply && !decision.ShouldReact && !decision.ShouldPin)
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

    private async Task<PostActionDecision> DecidePostActionAsync(SnPost post, bool isMentioned, string personality,
        string mood)
    {
        try
        {
            var author = post.Publisher?.Name ?? "someone";
            var content = post.Content ?? "";

            // Check if post has attachments
            var hasAttachments = HasAttachments(post);

            // If post has attachments but vision analysis is not available, skip it entirely
            if (hasAttachments && (!_config.Vision.EnableVisionAnalysis || !_kernelProvider.IsVisionModelAvailable()))
            {
                _logger.LogDebug("Skipping post {PostId} - has attachments but vision analysis is not configured", post.Id);
                return new PostActionDecision { };
            }

            // Check if we should use vision model
            var useVisionModel = hasAttachments && _config.Vision.EnableVisionAnalysis && _kernelProvider.IsVisionModelAvailable();
            var imageAttachments = useVisionModel ? GetSupportedImageAttachments(post) : new List<SnCloudFileReferenceObject>();

            if (useVisionModel && imageAttachments.Count > 0)
            {
                _logger.LogInformation("Using vision model for post {PostId} with {Count} image attachment(s)", post.Id, imageAttachments.Count);
            }

            var context = await GetPostContextChainAsync(post, maxDepth: 3);

            // If mentioned, always reply
            if (isMentioned)
            {
                if (useVisionModel && imageAttachments.Count > 0)
                {
                    // Build vision-enabled chat history with images for mentions
                    var chatHistory = await BuildVisionChatHistoryAsync(
                        personality, mood, author, content, imageAttachments, post.Attachments?.Count ?? 0, context, isMentioned: true);
                    var visionKernel = _kernelProvider.GetVisionKernel();
                    var visionSettings = _kernelProvider.CreateVisionPromptExecutionSettings();
                    var chatCompletionService = visionKernel.GetRequiredService<IChatCompletionService>();
                    var reply = await chatCompletionService.GetChatMessageContentAsync(chatHistory, visionSettings);
                    var replyContent = reply.Content?.Trim();
                    return new PostActionDecision { ShouldReply = true, Content = replyContent };
                }
                else
                {
                    var promptBuilder = new StringBuilder();
                    promptBuilder.AppendLine(personality);
                    promptBuilder.AppendLine();
                    promptBuilder.AppendLine($"Current mood: {mood}");
                    promptBuilder.AppendLine();

                    if (!string.IsNullOrEmpty(context))
                    {
                        promptBuilder.AppendLine("Context (replies show oldest first, forwards show oldest first):");
                        promptBuilder.AppendLine(context);
                        promptBuilder.AppendLine();
                    }

                    promptBuilder.AppendLine($"@{author} mentioned you in their post:");
                    promptBuilder.AppendLine($"\"{content}\"");
                    promptBuilder.AppendLine();
                    promptBuilder.AppendLine("When mentioned, you should ALWAYS reply (1-2 sentences). You can also add a reaction if you really appreciate it.");
                    promptBuilder.AppendLine("Reply is your primary action - be conversational and genuine!");
                    promptBuilder.AppendLine("Do not use emojis.");

                    var executionSettings = _kernelProvider.CreatePromptExecutionSettings();
                    var kernelArgs = new KernelArguments(executionSettings);
                    var result = await _kernel!.InvokePromptAsync(promptBuilder.ToString(), kernelArgs);
                    var replyContent = result.GetValue<string>()?.Trim();

                    return new PostActionDecision { ShouldReply = true, Content = replyContent };
                }
            }

            // Otherwise, decide whether to interact
            string decisionText;

            if (useVisionModel && imageAttachments.Count > 0)
            {
                // Build vision-enabled chat history with images for decision making
                var chatHistory = await BuildVisionChatHistoryAsync(
                    personality, mood, author, content, imageAttachments, post.Attachments?.Count ?? 0, context, isMentioned: false);
                var visionKernel = _kernelProvider.GetVisionKernel();
                var visionSettings = _kernelProvider.CreateVisionPromptExecutionSettings();
                var chatCompletionService = visionKernel.GetRequiredService<IChatCompletionService>();
                var reply = await chatCompletionService.GetChatMessageContentAsync(chatHistory, visionSettings);
                decisionText = reply.Content?.Trim().ToUpper() ?? "IGNORE";
            }
            else
            {
                // Use regular text-only prompt
                var decisionPrompt = new StringBuilder();
                decisionPrompt.AppendLine(personality);
                decisionPrompt.AppendLine();
                decisionPrompt.AppendLine($"Current mood: {mood}");
                decisionPrompt.AppendLine();

                if (!string.IsNullOrEmpty(context))
                {
                    decisionPrompt.AppendLine("Context (replies show oldest first, forwards show oldest first):");
                    decisionPrompt.AppendLine(context);
                    decisionPrompt.AppendLine();
                }

                decisionPrompt.AppendLine($"You see a post from @{author}:");
                decisionPrompt.AppendLine($"\"{content}\"");
                decisionPrompt.AppendLine();
                decisionPrompt.AppendLine("Choose your action(s). You can combine actions!");
                decisionPrompt.AppendLine();
                decisionPrompt.AppendLine("**REPLY** - Respond with your thoughts. This is encouraged - be social and conversational!");
                decisionPrompt.AppendLine();
                decisionPrompt.AppendLine("**REACT** - Add a quick emoji reaction to show appreciation or sentiment.");
                decisionPrompt.AppendLine();
                decisionPrompt.AppendLine("**REPLY+REACT** - Both reply AND react to the post. Best for when you have thoughts AND want to show extra appreciation!");
                decisionPrompt.AppendLine();
                decisionPrompt.AppendLine("**PIN** - Save this post to your profile (only for truly important content)");
                decisionPrompt.AppendLine();
                decisionPrompt.AppendLine("**IGNORE** - Skip this post if completely irrelevant");
                decisionPrompt.AppendLine();
                decisionPrompt.AppendLine("Available reactions: thumb_up, heart, clap, laugh, party, pray, cry, confuse, angry, just_okay, thumb_down");
                decisionPrompt.AppendLine();
                decisionPrompt.AppendLine("Respond with ONLY:");
                decisionPrompt.AppendLine("- REPLY: your response text here");
                decisionPrompt.AppendLine("- REACT:symbol:attitude (e.g., REACT:heart:Positive)");
                decisionPrompt.AppendLine("- REPLY+REACT:your response here:symbol:attitude (e.g., REPLY+REACT:Great post!:heart:Positive)");
                decisionPrompt.AppendLine("- PIN:PublisherPage");
                decisionPrompt.AppendLine("- IGNORE");
                decisionPrompt.AppendLine();
                decisionPrompt.AppendLine("Examples:");
                decisionPrompt.AppendLine("REPLY: That's really interesting! I've been thinking about this too.");
                decisionPrompt.AppendLine("REACT:heart:Positive");
                decisionPrompt.AppendLine("REPLY+REACT:Great point!:clap:Positive");
                decisionPrompt.AppendLine("REPLY+REACT:I agree completely:heart:Positive");
                decisionPrompt.AppendLine("REPLY+REACT:Nice observation:thumb_up:Positive");
                decisionPrompt.AppendLine("IGNORE");

                var decisionSettings = _kernelProvider.CreatePromptExecutionSettings();
                var decisionResult = await _kernel!.InvokePromptAsync(decisionPrompt.ToString(), new KernelArguments(decisionSettings));
                decisionText = decisionResult.GetValue<string>()?.Trim().ToUpper() ?? "IGNORE";
            }
            
            var decision = decisionText;

            _logger.LogInformation("AI decision for post {PostId}: {Decision}", post.Id, decision);

            if (decision.StartsWith("REPLY+REACT:"))
            {
                var rest = decision.Substring(12).Trim();
                var colonIndex = rest.LastIndexOf(':');
                if (colonIndex > 0)
                {
                    var replyText = rest.Substring(0, colonIndex).Trim();
                    var reactPart = rest.Substring(colonIndex + 1).Trim();
                    var reactParts = reactPart.Split(':');
                    var symbol = reactParts.Length > 0 ? reactParts[0].Trim().ToLower() : "heart";
                    var attitude = reactParts.Length > 1 ? reactParts[1].Trim() : "Positive";
                    return new PostActionDecision
                    {
                        ShouldReply = true,
                        Content = replyText,
                        ShouldReact = true,
                        ReactionSymbol = symbol,
                        ReactionAttitude = attitude
                    };
                }
                return new PostActionDecision { ShouldReply = true, Content = rest };
            }

            if (decision.StartsWith("REPLY:"))
            {
                var replyText = decision.Substring(6).Trim();
                return new PostActionDecision { ShouldReply = true, Content = replyText };
            }

            if (decision.StartsWith("REACT:"))
            {
                var parts = decision.Substring(6).Split(':');
                var symbol = parts.Length > 0 ? parts[0].Trim().ToLower() : "heart";
                var attitude = parts.Length > 1 ? parts[1].Trim() : "Positive";
                return new PostActionDecision
                {
                    ShouldReact = true,
                    ReactionSymbol = symbol,
                    ReactionAttitude = attitude
                };
            }

            if (decision.StartsWith("PIN:"))
            {
                var mode = decision.Substring(4).Trim();
                var pinMode = mode.Equals("RealmPage", StringComparison.OrdinalIgnoreCase)
                    ? DysonNetwork.Shared.Models.PostPinMode.RealmPage
                    : DysonNetwork.Shared.Models.PostPinMode.PublisherPage;
                return new PostActionDecision { ShouldPin = true, PinMode = pinMode };
            }

            return new PostActionDecision { };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deciding action for post {PostId}", post.Id);
            return new PostActionDecision { };
        }
    }

    /// <summary>
    /// Build a ChatHistory with images for vision analysis
    /// </summary>
    private async Task<ChatHistory> BuildVisionChatHistoryAsync(string personality, string mood, string author, string content,
        List<SnCloudFileReferenceObject> imageAttachments, int totalAttachmentCount, string context, bool isMentioned)
    {
        var chatHistory = new ChatHistory(personality);
        chatHistory.AddSystemMessage($"Current mood: {mood}");

        // Build the text part of the message
        var textBuilder = new StringBuilder();

        if (!string.IsNullOrEmpty(context))
        {
            textBuilder.AppendLine("Context (replies show oldest first, forwards show oldest first):");
            textBuilder.AppendLine(context);
            textBuilder.AppendLine();
        }

        if (isMentioned)
        {
            textBuilder.AppendLine($"@{author} mentioned you in their post with {totalAttachmentCount} attachment(s):");
        }
        else
        {
            textBuilder.AppendLine($"You see a post from @{author} with {totalAttachmentCount} attachment(s):");
        }
        textBuilder.AppendLine($"Content: \"{content}\"");
        textBuilder.AppendLine();

        // Create a collection to hold all content items (text + images)
        var contentItems = new ChatMessageContentItemCollection();
        contentItems.Add(new TextContent(textBuilder.ToString()));

        // Download and add images
        foreach (var attachment in imageAttachments)
        {
            try
            {
                var imageBytes = await DownloadImageAsync(attachment);
                if (imageBytes != null && imageBytes.Length > 0)
                {
                    contentItems.Add(new ImageContent(imageBytes, attachment.MimeType ?? "image/jpeg"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download image {FileId} for vision analysis", attachment.Id);
            }
        }

        // Add instruction text after images
        var instructionText = new StringBuilder();
        if (imageAttachments.Count > 0)
        {
            instructionText.AppendLine();
            instructionText.AppendLine("Analyze the visual content along with the text to understand the full context.");
            instructionText.AppendLine();
        }

        if (isMentioned)
        {
            instructionText.AppendLine("When mentioned, you should ALWAYS reply (1-3 sentences). You can also add a reaction if you really appreciate it.");
            instructionText.AppendLine("Reply is your primary action - be conversational and genuine, considering both the text and any images.");
            instructionText.AppendLine("Do not use emojis.");
        }
        else
        {
            instructionText.AppendLine("Choose your action(s). You can combine actions!");
            instructionText.AppendLine();
            instructionText.AppendLine("**REPLY** - Respond with your thoughts. This is encouraged - be social and conversational!");
            instructionText.AppendLine();
            instructionText.AppendLine("**REACT** - Add a quick emoji reaction to show appreciation or sentiment.");
            instructionText.AppendLine();
            instructionText.AppendLine("**REPLY+REACT** - Both reply AND react to the post. Best for when you have thoughts AND want to show extra appreciation!");
            instructionText.AppendLine();
            instructionText.AppendLine("**PIN** - Save this post to your profile (only for truly important content)");
            instructionText.AppendLine();
            instructionText.AppendLine("**IGNORE** - Skip this post completely");
            instructionText.AppendLine();
            instructionText.AppendLine("Available reactions: thumb_up, heart, clap, laugh, party, pray, cry, confuse, angry, just_okay, thumb_down");
            instructionText.AppendLine();
            instructionText.AppendLine("Respond with ONLY:");
            instructionText.AppendLine("- REPLY: your response text here");
            instructionText.AppendLine("- REACT:symbol:attitude (e.g., REACT:heart:Positive)");
            instructionText.AppendLine("- REPLY+REACT:your response here:symbol:attitude (e.g., REPLY+REACT:Great post!:heart:Positive)");
            instructionText.AppendLine("- PIN:PublisherPage");
            instructionText.AppendLine("- IGNORE");
            instructionText.AppendLine();
            instructionText.AppendLine("Examples:");
            instructionText.AppendLine("REPLY: That's really interesting! I've been thinking about this too.");
            instructionText.AppendLine("REPLY: I completely agree with your point about this.");
            instructionText.AppendLine("REACT:heart:Positive");
            instructionText.AppendLine("REPLY+REACT:Great point!:clap:Positive");
            instructionText.AppendLine("REPLY+REACT:I agree completely:heart:Positive");
            instructionText.AppendLine("REPLY+REACT:Nice observation:thumb_up:Positive");
            instructionText.AppendLine("IGNORE");
        }

        contentItems.Add(new TextContent(instructionText.ToString()));

        // Create a ChatMessageContent with all items and add it to history
        var userMessage = new ChatMessageContent
        {
            Role = AuthorRole.User,
            Items = contentItems
        };
        chatHistory.Add(userMessage);

        return chatHistory;
    }

    /// <summary>
    /// Download image bytes from the drive service
    /// </summary>
    private async Task<byte[]?> DownloadImageAsync(SnCloudFileReferenceObject attachment)
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
                url = $"{_config.GatewayUrl}/drive/files/{attachment.Id}";
            }
            else
            {
                return null;
            }

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download image from {Url}", attachment.Url ?? $"/drive/files/{attachment.Id}");
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
                symbol = symbol,
                attitude = attitudeValue
            };

            await _apiClient.PostAsync("sphere", $"/posts/{post.Id}/reactions", request);

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

    private async Task PinPostAsync(SnPost post, DysonNetwork.Shared.Models.PostPinMode mode)
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

            await _apiClient.DeleteAsync("sphere", $"/posts/{post.Id}/pin");

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
        if (post.Mentions == null) return false;
        return post.Mentions.Any(mention =>
            mention.Username?.Equals("michan", StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <summary>
    /// Check if a post has attachments
    /// </summary>
    private bool HasAttachments(SnPost post)
    {
        return post.Attachments != null && post.Attachments.Count > 0;
    }

    /// <summary>
    /// Get supported image attachments for vision analysis
    /// </summary>
    private List<SnCloudFileReferenceObject> GetSupportedImageAttachments(SnPost post)
    {
        if (post.Attachments == null || post.Attachments.Count == 0)
            return new List<SnCloudFileReferenceObject>();

        var supportedImageTypes = new[] { "image/jpeg", "image/png", "image/gif", "image/webp", "image/jpg" };
        
        return post.Attachments
            .Where(a => !string.IsNullOrEmpty(a.MimeType) && 
                        supportedImageTypes.Contains(a.MimeType.ToLower()))
            .Where(a => !string.IsNullOrEmpty(a.Url) || !string.IsNullOrEmpty(a.Id))
            .ToList();
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

        var recentInteractions = await _memoryService.GetInteractionsByTypeAsync("autonomous", limit: 10);
        var interests = recentInteractions
            .SelectMany(i =>
                i.Memory.GetValueOrDefault("topics", new List<string>()) as List<string> ?? new List<string>())
            .Distinct()
            .Take(5)
            .ToList();

        var personality = PersonalityLoader.LoadPersonality(_config.PersonalityFile, _config.Personality, _logger);
        var mood = _config.AutonomousBehavior.PersonalityMood;

        var prompt = $"""
                      {personality}

                      Current mood: {mood}
                      Recent interests: {string.Join(", ", interests)}

                      Create a social media post. 
                      Share a thought, observation, question, or insight that reflects your personality.
                      Can be 1-4 sentences - take the space you need to express yourself.
                      Be natural, conversational, and authentic.
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
                var alreadyReposted = repostInteractions.Any(i =>
                    i.Memory.GetValueOrDefault("post_id")?.ToString() == post.Id.ToString());
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

Would this post be EXCEPTIONAL to repost? Only repost content that is truly outstanding.
Strict criteria - all must apply:
- The content is genuinely timeless, educational, or profound
- It offers unique insights not commonly found elsewhere
- Your followers would find it genuinely valuable
- This is NOT a casual opinion, announcement, or routine update

Be very selective. Most posts should NOT be reposted.

Respond with ONLY one word: YES or NO.";

            var executionSettings = _kernelProvider.CreatePromptExecutionSettings();
            var result = await _kernel!.InvokePromptAsync(prompt, new KernelArguments(executionSettings));
            var decision = result.GetValue<string>()?.Trim().ToUpper() ?? "NO";

            var isInteresting = decision.StartsWith("YES");
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

    private async Task<string> GetPostContextChainAsync(SnPost post, int maxDepth = 3)
    {
        var contextParts = new List<string>();

        await AddRepliedContextAsync(post, contextParts, 0, maxDepth, "replied");
        await AddForwardedContextAsync(post, contextParts, 0, maxDepth, "forwarded");

        return string.Join("\n\n", contextParts);
    }

    private async Task AddRepliedContextAsync(SnPost post, List<string> contextParts, int currentDepth, int maxDepth, string label)
    {
        if (currentDepth >= maxDepth || post.RepliedPostId == null)
            return;

        var parentPost = post.RepliedPost;
        if (parentPost == null && post.RepliedPostId.HasValue)
        {
            parentPost = await _postPlugin.GetPost(post.RepliedPostId.Value.ToString());
        }

        if (parentPost == null)
            return;

        var author = parentPost.Publisher?.Name ?? "unknown";
        var title = !string.IsNullOrEmpty(parentPost.Title) ? $" [{parentPost.Title}]" : "";
        var description = !string.IsNullOrEmpty(parentPost.Description) ? $" | {parentPost.Description}" : "";
        var content = parentPost.Content ?? "";
        var indent = new string(' ', currentDepth * 2);

        contextParts.Insert(0, $"{indent}↳ @{author}{title}{description}: {content}");

        await AddRepliedContextAsync(parentPost, contextParts, currentDepth + 1, maxDepth, label);
    }

    private async Task AddForwardedContextAsync(SnPost post, List<string> contextParts, int currentDepth, int maxDepth, string label)
    {
        if (currentDepth >= maxDepth || post.ForwardedPostId == null)
            return;

        var parentPost = post.ForwardedPost;
        if (parentPost == null && post.ForwardedPostId.HasValue)
        {
            parentPost = await _postPlugin.GetPost(post.ForwardedPostId.Value.ToString());
        }

        if (parentPost == null)
            return;

        var author = parentPost.Publisher?.Name ?? "unknown";
        var title = !string.IsNullOrEmpty(parentPost.Title) ? $" [{parentPost.Title}]" : "";
        var description = !string.IsNullOrEmpty(parentPost.Description) ? $" | {parentPost.Description}" : "";
        var content = parentPost.Content ?? "";
        var indent = new string(' ', currentDepth * 2);

        contextParts.Add($"{indent}⇢ @{author}{title}{description}: {content}");

        await AddForwardedContextAsync(parentPost, contextParts, currentDepth + 1, maxDepth, label);
    }

    private class PostActionDecision
    {
        public bool ShouldReply { get; set; }
        public bool ShouldReact { get; set; }
        public bool ShouldPin { get; set; }
        public string? Content { get; set; } // For replies
        public string? ReactionSymbol { get; set; } // For reactions: thumb_up, heart, etc.
        public string? ReactionAttitude { get; set; } // For reactions: Positive, Negative, Neutral
        public DysonNetwork.Shared.Models.PostPinMode? PinMode { get; set; } // For pins: ProfilePage, RealmPage
    }
}

#pragma warning restore SKEXP0050