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

    public MiChanAutonomousBehavior(
        MiChanConfig config,
        ILogger<MiChanAutonomousBehavior> logger,
        SolarNetworkApiClient apiClient,
        MiChanMemoryService memoryService,
        MiChanKernelProvider kernelProvider,
        IServiceProvider serviceProvider,
        AccountService.AccountServiceClient accountClient)
    {
        _config = config;
        _logger = logger;
        _apiClient = apiClient;
        _memoryService = memoryService;
        _kernelProvider = kernelProvider;
        _serviceProvider = serviceProvider;
        _accountClient = accountClient;
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

        // Get recent posts
        var posts = await _apiClient.GetAsync<List<SnPost>>("sphere", "/posts?take=30");
        if (posts == null || posts.Count == 0)
            return;

        // Get blocked users list (cached)
        var blockedUsers = await GetBlockedByUsersAsync();

        var personality = PersonalityLoader.LoadPersonality(_config.PersonalityFile, _config.Personality, _logger);
        var mood = _config.AutonomousBehavior.PersonalityMood;

        foreach (var post in posts.OrderByDescending(p => p.CreatedAt).Take(10))
        {
            // Skip already processed posts
            if (!_processedPostIds.Add(post.Id.ToString()))
            {
                _logger.LogDebug("Skipping post {PostId} - already processed", post.Id);
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
                _logger.LogInformation("Skipping post {PostId} from user {UserId} - user has blocked MiChan", post.Id, authorAccountId);
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
                return new PostActionDecision { Action = "ignore" };
            }

            // Check if we should use vision model
            var useVisionModel = hasAttachments && _config.Vision.EnableVisionAnalysis && _kernelProvider.IsVisionModelAvailable();
            var imageAttachments = useVisionModel ? GetSupportedImageAttachments(post) : new List<SnCloudFileReferenceObject>();

            if (useVisionModel && imageAttachments.Count > 0)
            {
                _logger.LogInformation("Using vision model for post {PostId} with {Count} image attachment(s)", post.Id, imageAttachments.Count);
            }

            // If mentioned, always reply
            if (isMentioned)
            {
                if (useVisionModel && imageAttachments.Count > 0)
                {
                    // Build vision-enabled chat history with images for mentions
                    var chatHistory = await BuildVisionChatHistoryAsync(
                        personality, mood, author, content, imageAttachments, post.Attachments?.Count ?? 0, isMentioned: true);
                    var visionKernel = _kernelProvider.GetVisionKernel();
                    var visionSettings = _kernelProvider.CreateVisionPromptExecutionSettings();
                    var chatCompletionService = visionKernel.GetRequiredService<IChatCompletionService>();
                    var reply = await chatCompletionService.GetChatMessageContentAsync(chatHistory, visionSettings);
                    var replyContent = reply.Content?.Trim();
                    return new PostActionDecision { Action = "reply", Content = replyContent };
                }
                else
                {
                    var prompt = $@"
{personality}

Current mood: {mood}

@{author} mentioned you in their post:
""{content}""

Write a friendly, natural reply (1-3 sentences). Be conversational and genuine.
Do not use emojis.";

                    var executionSettings = _kernelProvider.CreatePromptExecutionSettings();
                    var kernelArgs = new KernelArguments(executionSettings);
                    var result = await _kernel!.InvokePromptAsync(prompt, kernelArgs);
                    var replyContent = result.GetValue<string>()?.Trim();

                    return new PostActionDecision { Action = "reply", Content = replyContent };
                }
            }

            // Otherwise, decide whether to interact
            string decisionText;
            
            if (useVisionModel && imageAttachments.Count > 0)
            {
                // Build vision-enabled chat history with images for decision making
                var chatHistory = await BuildVisionChatHistoryAsync(
                    personality, mood, author, content, imageAttachments, post.Attachments?.Count ?? 0, isMentioned: false);
                var visionKernel = _kernelProvider.GetVisionKernel();
                var visionSettings = _kernelProvider.CreateVisionPromptExecutionSettings();
                var chatCompletionService = visionKernel.GetRequiredService<IChatCompletionService>();
                var reply = await chatCompletionService.GetChatMessageContentAsync(chatHistory, visionSettings);
                decisionText = reply.Content?.Trim().ToUpper() ?? "IGNORE";
            }
            else
            {
                // Use regular text-only prompt
                var decisionPrompt = $@"
{personality}

Current mood: {mood}

You see a post from @{author}:
""{content}""

Choose ONE action:

**REPLY** - Respond with your thoughts, agreement, question, or perspective. Use this when:
- You have ANYTHING to say about the topic
- You want to engage in conversation
- You agree or disagree with the point
- You have a related thought or question

**REACT** - Quick emoji reaction when you want to acknowledge but have nothing to add. Use this when:
- The post is good but you don't have words to contribute
- You just want to show appreciation without conversation

**PIN** - Save this post to your profile (only for truly important content)

**IGNORE** - Skip this post completely

REPLY is your default - be conversational and social! Don't be shy about engaging.

Available reactions if you choose REACT: thumb_up, heart, clap, laugh, party, pray, cry, confuse, angry, just_okay, thumb_down

Respond with ONLY:
- REPLY: your response text here
- REACT:symbol:attitude (e.g., REACT:thumb_up:Positive)
- PIN:PublisherPage
- IGNORE

Examples:
REPLY: That's really interesting! I've been thinking about this too.
REPLY: I completely agree with your point about this.
REACT:heart:Positive
REACT:clap:Positive
IGNORE";
                var decisionSettings = _kernelProvider.CreatePromptExecutionSettings();
                var decisionResult = await _kernel!.InvokePromptAsync(decisionPrompt, new KernelArguments(decisionSettings));
                decisionText = decisionResult.GetValue<string>()?.Trim().ToUpper() ?? "IGNORE";
            }
            
            var decision = decisionText;

            _logger.LogInformation("AI decision for post {PostId}: {Decision}", post.Id, decision);

            if (decision.StartsWith("REPLY:"))
            {
                var replyText = decision.Substring(6).Trim();
                return new PostActionDecision { Action = "reply", Content = replyText };
            }

            // 30% chance to convert REACT to REPLY to encourage more conversation
            if (decision.StartsWith("REACT:") && _random.Next(100) < 30)
            {
                _logger.LogInformation("Converting REACT to REPLY for post {PostId} (30% chance)", post.Id);
                var parts = decision.Substring(6).Split(':');
                var symbol = parts.Length > 0 ? parts[0].Trim().ToLower() : "thumb_up";

                // Generate a quick reply based on the reaction type (no emojis)
                var quickReply = symbol switch
                {
                    "heart" => "Love this!",
                    "clap" => "Well said!",
                    "laugh" => "Haha, this is great!",
                    "thumb_up" => "Totally agree with this!",
                    "party" => "This is exciting!",
                    "pray" => "Sending good vibes",
                    _ => "Interesting point!"
                };

                return new PostActionDecision { Action = "reply", Content = quickReply };
            }

            if (decision.StartsWith("REACT:"))
            {
                var parts = decision.Substring(6).Split(':');
                var symbol = parts.Length > 0 ? parts[0].Trim().ToLower() : "thumb_up";
                var attitude = parts.Length > 1 ? parts[1].Trim() : "Positive";
                return new PostActionDecision
                    { Action = "react", ReactionSymbol = symbol, ReactionAttitude = attitude };
            }

            if (decision.StartsWith("PIN:"))
            {
                var mode = decision.Substring(4).Trim();
                var pinMode = mode.Equals("RealmPage", StringComparison.OrdinalIgnoreCase)
                    ? DysonNetwork.Shared.Models.PostPinMode.RealmPage
                    : DysonNetwork.Shared.Models.PostPinMode.PublisherPage;
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

    /// <summary>
    /// Build a ChatHistory with images for vision analysis
    /// </summary>
    private async Task<ChatHistory> BuildVisionChatHistoryAsync(string personality, string mood, string author, string content,
        List<SnCloudFileReferenceObject> imageAttachments, int totalAttachmentCount, bool isMentioned)
    {
        var chatHistory = new ChatHistory(personality);
        chatHistory.AddSystemMessage($"Current mood: {mood}");

        // Build the text part of the message
        var textBuilder = new StringBuilder();
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
            instructionText.AppendLine("Write a friendly, natural reply (1-3 sentences). Be conversational and genuine, considering both the text and any images.");
            instructionText.AppendLine("Do not use emojis.");
        }
        else
        {
            instructionText.AppendLine("Choose ONE action:");
            instructionText.AppendLine();
            instructionText.AppendLine("**REPLY** - Respond with your thoughts, agreement, question, or perspective. Use this when:");
            instructionText.AppendLine("- You have ANYTHING to say about the topic");
            instructionText.AppendLine("- You want to engage in conversation");
            instructionText.AppendLine("- You agree or disagree with the point");
            instructionText.AppendLine("- You have a related thought or question");
            instructionText.AppendLine();
            instructionText.AppendLine("**REACT** - Quick emoji reaction when you want to acknowledge but have nothing to add. Use this when:");
            instructionText.AppendLine("- The post is good but you don't have words to contribute");
            instructionText.AppendLine("- You just want to show appreciation without conversation");
            instructionText.AppendLine();
            instructionText.AppendLine("**PIN** - Save this post to your profile (only for truly important content)");
            instructionText.AppendLine();
            instructionText.AppendLine("**IGNORE** - Skip this post completely");
            instructionText.AppendLine();
            instructionText.AppendLine("REPLY is your default - be conversational and social! Don't be shy about engaging.");
            instructionText.AppendLine();
            instructionText.AppendLine("Available reactions if you choose REACT: thumb_up, heart, clap, laugh, party, pray, cry, confuse, angry, just_okay, thumb_down");
            instructionText.AppendLine();
            instructionText.AppendLine("Respond with ONLY:");
            instructionText.AppendLine("- REPLY: your response text here");
            instructionText.AppendLine("- REACT:symbol:attitude (e.g., REACT:thumb_up:Positive)");
            instructionText.AppendLine("- PIN:PublisherPage");
            instructionText.AppendLine("- IGNORE");
            instructionText.AppendLine();
            instructionText.AppendLine("Examples:");
            instructionText.AppendLine("REPLY: That's really interesting! I've been thinking about this too.");
            instructionText.AppendLine("REPLY: I completely agree with your point about this.");
            instructionText.AppendLine("REACT:heart:Positive");
            instructionText.AppendLine("REACT:clap:Positive");
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

    private class PostActionDecision
    {
        public string Action { get; set; } = "ignore"; // "react", "reply", "pin", "ignore"
        public string? Content { get; set; } // For replies
        public string? ReactionSymbol { get; set; } // For reactions: thumb_up, heart, etc.
        public string? ReactionAttitude { get; set; } // For reactions: Positive, Negative, Neutral
        public DysonNetwork.Shared.Models.PostPinMode? PinMode { get; set; } // For pins: ProfilePage, RealmPage
    }
}

#pragma warning restore SKEXP0050