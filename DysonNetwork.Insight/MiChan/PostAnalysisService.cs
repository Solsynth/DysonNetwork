#pragma warning disable SKEXP0050
using System.Diagnostics.CodeAnalysis;
using System.Text;
using DysonNetwork.Insight.MiChan.Plugins;
using DysonNetwork.Shared.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace DysonNetwork.Insight.MiChan;

/// <summary>
/// Service for analyzing posts with vision and context support
/// </summary>
public class PostAnalysisService
{
    private readonly MiChanConfig _config;
    private readonly ILogger<PostAnalysisService> _logger;
    private readonly MiChanKernelProvider _kernelProvider;
    private readonly PostPlugin _postPlugin;

    public PostAnalysisService(
        MiChanConfig config,
        ILogger<PostAnalysisService> logger,
        MiChanKernelProvider kernelProvider,
        PostPlugin postPlugin)
    {
        _config = config;
        _logger = logger;
        _kernelProvider = kernelProvider;
        _postPlugin = postPlugin;
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("AtField", config.AccessToken);
    }

    /// <summary>
    /// Check if a post has any attachments (images)
    /// </summary>
    public bool HasAttachments(SnPost post)
    {
        return post.Attachments != null && post.Attachments.Count > 0;
    }

    /// <summary>
    /// Get supported image attachments from a single post
    /// </summary>
    public List<SnCloudFileReferenceObject> GetSupportedImageAttachments(SnPost post)
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

    /// <summary>
    /// Get all supported image attachments from the post and its context chain (replied/forwarded posts)
    /// </summary>
    public async Task<List<SnCloudFileReferenceObject>> GetAllImageAttachmentsFromContextAsync(SnPost post, int maxDepth = 3)
    {
        var allAttachments = new List<SnCloudFileReferenceObject>();
        var processedPostIds = new HashSet<Guid>();

        await CollectAttachmentsRecursiveAsync(post, allAttachments, processedPostIds, 0, maxDepth);

        return allAttachments;
    }

    /// <summary>
    /// Get all image attachments from multiple posts
    /// </summary>
    public async Task<List<SnCloudFileReferenceObject>> GetAllImageAttachmentsFromPostsAsync(List<SnPost> posts, int maxDepth = 3)
    {
        var allAttachments = new List<SnCloudFileReferenceObject>();
        var processedPostIds = new HashSet<Guid>();

        foreach (var post in posts)
        {
            await CollectAttachmentsRecursiveAsync(post, allAttachments, processedPostIds, 0, maxDepth);
        }

        return allAttachments;
    }

    private async Task CollectAttachmentsRecursiveAsync(SnPost post, List<SnCloudFileReferenceObject> attachments, HashSet<Guid> processedIds, int currentDepth, int maxDepth)
    {
        if (currentDepth >= maxDepth || post == null || processedIds.Contains(post.Id))
            return;

        processedIds.Add(post.Id);

        // Add attachments from current post
        var postAttachments = GetSupportedImageAttachments(post);
        attachments.AddRange(postAttachments);

        // Recursively collect from replied post
        if (post.RepliedPostId.HasValue && post.RepliedPost != null)
        {
            await CollectAttachmentsRecursiveAsync(post.RepliedPost, attachments, processedIds, currentDepth + 1, maxDepth);
        }
        else if (post.RepliedPostId.HasValue)
        {
            // Fetch replied post if not loaded
            var repliedPost = await _postPlugin.GetPost(post.RepliedPostId.Value.ToString());
            if (repliedPost != null)
            {
                await CollectAttachmentsRecursiveAsync(repliedPost, attachments, processedIds, currentDepth + 1, maxDepth);
            }
        }

        // Recursively collect from forwarded post
        if (post.ForwardedPostId.HasValue && post.ForwardedPost != null)
        {
            await CollectAttachmentsRecursiveAsync(post.ForwardedPost, attachments, processedIds, currentDepth + 1, maxDepth);
        }
        else if (post.ForwardedPostId.HasValue)
        {
            // Fetch forwarded post if not loaded
            var forwardedPost = await _postPlugin.GetPost(post.ForwardedPostId.Value.ToString());
            if (forwardedPost != null)
            {
                await CollectAttachmentsRecursiveAsync(forwardedPost, attachments, processedIds, currentDepth + 1, maxDepth);
            }
        }
    }

    /// <summary>
    /// Build text context chain for a post (replied and forwarded posts)
    /// </summary>
    public async Task<string> GetPostContextChainAsync(SnPost post, int maxDepth = 3)
    {
        var contextParts = new List<string>();

        await AddRepliedContextAsync(post, contextParts, 0, maxDepth);
        await AddForwardedContextAsync(post, contextParts, 0, maxDepth);

        return string.Join("\n\n", contextParts);
    }

    private async Task AddRepliedContextAsync(SnPost post, List<string> contextParts, int currentDepth, int maxDepth)
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

        await AddRepliedContextAsync(parentPost, contextParts, currentDepth + 1, maxDepth);
    }

    private async Task AddForwardedContextAsync(SnPost post, List<string> contextParts, int currentDepth, int maxDepth)
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

        await AddForwardedContextAsync(parentPost, contextParts, currentDepth + 1, maxDepth);
    }

    /// <summary>
    /// Check if vision model is available
    /// </summary>
    public bool IsVisionModelAvailable()
    {
        return _config.Vision.EnableVisionAnalysis && _kernelProvider.IsVisionModelAvailable();
    }

    /// <summary>
    /// Build a ChatHistory with images for vision analysis of posts
    /// </summary>
    [Experimental("SKEXP0050")]
    public async Task<ChatHistory> BuildVisionChatHistoryForPostsAsync(
        List<SnPost> posts,
        string userQuery,
        string? systemPrompt = null)
    {
        var chatHistory = new ChatHistory(systemPrompt ?? "You are an AI assistant analyzing images in social media posts. Describe what you see in the images and relate it to the user's question.");

        // Build the text part of the message
        var textBuilder = new StringBuilder();
        textBuilder.AppendLine("The user has shared posts with images and asked a question.");
        textBuilder.AppendLine();
        textBuilder.AppendLine("Posts:");

        foreach (var post in posts)
        {
            textBuilder.AppendLine($"- Post by @{post.Publisher?.Name}: {post.Content}");
        }

        textBuilder.AppendLine();
        textBuilder.AppendLine($"User's question: {userQuery}");
        textBuilder.AppendLine();
        textBuilder.AppendLine("Please analyze the images and provide relevant context to help answer the user's question.");

        // Create a collection to hold all content items (text + images)
        var contentItems = new ChatMessageContentItemCollection();
        contentItems.Add(new TextContent(textBuilder.ToString()));

        // Get all image attachments from posts and their context
        var allAttachments = await GetAllImageAttachmentsFromPostsAsync(posts, maxDepth: 3);

        // Download and add images
        foreach (var attachment in allAttachments)
        {
            try
            {
                var imageContent = BuildImageContent(attachment);
                if (imageContent != null)
                {
                    contentItems.Add(imageContent);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download image {FileId} for vision analysis", attachment.Id);
            }
        }

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
    /// Build a ChatHistory with images for MiChan's autonomous behavior decision making
    /// </summary>
    [Experimental("SKEXP0050")]
    public async Task<ChatHistory> BuildVisionChatHistoryForDecisionAsync(
        string personality,
        string mood,
        string author,
        string content,
        List<SnCloudFileReferenceObject> imageAttachments,
        int totalAttachmentCount,
        string context,
        bool isMentioned)
    {
        var chatHistory = new ChatHistory(personality);
        chatHistory.AddSystemMessage($"当前心情: {mood}");

        // Build the text part of the message
        var textBuilder = new StringBuilder();

        if (!string.IsNullOrEmpty(context))
        {
            textBuilder.AppendLine("上下文（回复从旧到新，转发从旧到新）：");
            textBuilder.AppendLine(context);
            textBuilder.AppendLine();
        }

        if (isMentioned)
            textBuilder.AppendLine($"{author} 在帖子中提到了你，包含 {totalAttachmentCount} 个附件：");
        else
            textBuilder.AppendLine($"你看到 {author} 的帖子，包含 {totalAttachmentCount} 个附件：");

        textBuilder.AppendLine($"内容：\"{content}\"");
        textBuilder.AppendLine();

        // Create a collection to hold all content items (text + images)
        var contentItems = new ChatMessageContentItemCollection { new TextContent(textBuilder.ToString()) };

        // Add images
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
        }
        else
        {
            instructionText.AppendLine("选择你的行动。每个行动单独一行。可以同时回复和反应！");
            instructionText.AppendLine();
            instructionText.AppendLine("**REPLY** - 回复表达你的想法。鼓励互动交流！");
            instructionText.AppendLine();
            instructionText.AppendLine("**REACT** - 添加表情反应表示赞赏或态度（只一个表情）。");
            instructionText.AppendLine();
            instructionText.AppendLine("**PIN** - 收藏帖子（仅限真正重要内容）");
            instructionText.AppendLine();
            instructionText.AppendLine("**IGNORE** - 忽略此帖子");
            instructionText.AppendLine();
            instructionText.AppendLine(
                "可用表情：thumb_up, heart, clap, laugh, party, pray, cry, confuse, angry, just_okay, thumb_down");
            instructionText.AppendLine();
            instructionText.AppendLine("格式：每行动单独一行：");
            instructionText.AppendLine("- REPLY: 你的回复内容");
            instructionText.AppendLine("- REACT:symbol:attitude （例如 REACT:heart:Positive）");
            instructionText.AppendLine("- PIN:PublisherPage");
            instructionText.AppendLine("- IGNORE");
            instructionText.AppendLine();
            instructionText.AppendLine("示例：");
            instructionText.AppendLine("REPLY: 这个很有意思！我也在想这个。");
            instructionText.AppendLine("REPLY: 我完全同意你的观点。");
            instructionText.AppendLine("REACT:heart:Positive");
            instructionText.AppendLine("REPLY: 这个很有意思！我也在想这个。");
            instructionText.AppendLine("REACT:heart:Positive");
            instructionText.AppendLine("REPLY: 我完全同意你的观点。");
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
    /// Create ImageContent from a cloud file attachment
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
                url = $"{_config.GatewayUrl}/drive/files/{attachment.Id}";
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
}
#pragma warning restore SKEXP0050
