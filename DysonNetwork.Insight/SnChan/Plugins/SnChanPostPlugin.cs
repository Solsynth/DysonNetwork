using System.ComponentModel;
using System.Text.Json;
using DysonNetwork.Shared.Models;
using Microsoft.SemanticKernel;

namespace DysonNetwork.Insight.SnChan.Plugins;

/// <summary>
/// Plugin for SnChan to interact with posts
/// Uses separate bot account authentication
/// </summary>
public class SnChanPostPlugin(
    SnChanApiClient apiClient,
    SnChanMoodService moodService,
    SnChanPublisherService publisherService,
    ILogger<SnChanPostPlugin> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    [KernelFunction("create_post")]
    [Description("Create and publish a new post on Solar Network. Returns JSON with success status and created post data.")]
    public async Task<string> CreatePost(
        [Description("The content of the post")]
        string content,
        [Description("The title of the post, optional")]
        string? title = null,
        [Description("The description of the post, optional")]
        string? description = null,
        [Description("The tags of the post, splitted by comma, optional")]
        string? tags = null,
        [Description("List of attachment IDs to include with the post, optional")]
        List<string>? attachments = null,
        [Description("Whether to post as official publisher (solsynth). Default false (personal). Use true for official announcements, support responses, or formal statements.")]
        bool asOfficial = false
    )
    {
        try
        {
            // Ensure publisher service is initialized
            await publisherService.EnsureInitializedAsync();

            var request = new Dictionary<string, object>()
            {
                ["content"] = content,
            };
            if (!string.IsNullOrWhiteSpace(title)) request["title"] = title;
            if (!string.IsNullOrWhiteSpace(description)) request["description"] = description;
            if (!string.IsNullOrWhiteSpace(tags)) request["tags"] = tags.Split(',').Select(x => x.Trim()).ToArray();
            if (attachments is { Count: > 0 }) request["attachment_ids"] = attachments;

            // Determine publisher and add query parameter
            var publisherName = publisherService.GetPublisherNameForPost(asOfficial);
            var queryParams = new Dictionary<string, string>
            {
                ["pub"] = publisherName
            };

            logger.LogInformation("SnChan attempting to create post as publisher '{PublisherName}'", publisherName);

            var result = await apiClient.PostAsync<object>("sphere", "/posts", request, queryParams);

            logger.LogInformation("SnChan created new post as {Publisher}", publisherName);

            // Record emotional event and trigger mood update
            await moodService.RecordInteractionAsync("created_post");
            await moodService.TryUpdateMoodAsync();

            return JsonSerializer.Serialize(new { success = true, message = $"Post created successfully as {publisherName}", data = result }, JsonOptions);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            logger.LogError(ex, "Failed to create post - 403 Forbidden. This usually means the bot account is not an editor of the publisher.");
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "403 Forbidden - The bot account needs to be an editor of the publisher to post. Please check publisher membership settings.",
                details = ex.Message
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create post");
            return JsonSerializer.Serialize(new { success = false, error = ex.Message }, JsonOptions);
        }
    }

    [KernelFunction("reply_to_post")]
    [Description("Reply to a post. Returns JSON with success status and created reply data. AI should decide whether to use official or personal publisher based on context.")]
    public async Task<string> ReplyToPost(
        [Description("The ID of the post to reply to")]
        string postId,
        [Description("The content of the reply")]
        string content,
        [Description("Whether to reply as official publisher (solsynth). Default false (personal). Consider using true when: 1) The original post is from official publisher, 2) User is asking about Solar Network issues or support, 3) A formal/official response is appropriate.")]
        bool asOfficial = false
    )
    {
        try
        {
            // Ensure publisher service is initialized
            await publisherService.EnsureInitializedAsync();

            var request = new Dictionary<string, object>
            {
                ["content"] = content,
                ["replied_post_id"] = postId
            };

            // Determine publisher and add query parameter
            var publisherName = publisherService.GetPublisherNameForPost(asOfficial);
            var queryParams = new Dictionary<string, string>
            {
                ["pub"] = publisherName
            };

            logger.LogInformation("SnChan attempting to reply to post {PostId} as publisher '{PublisherName}'", postId, publisherName);

            var result = await apiClient.PostAsync<object>("sphere", "/posts", request, queryParams);

            logger.LogInformation("SnChan replied to post {PostId} as {Publisher}", postId, publisherName);

            // Record emotional event and trigger mood update
            await moodService.RecordInteractionAsync("replied_to_post");
            await moodService.TryUpdateMoodAsync();

            return JsonSerializer.Serialize(new { success = true, message = $"Reply created successfully as {publisherName}", data = result }, JsonOptions);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            logger.LogError(ex, "Failed to reply to post {PostId} - 403 Forbidden", postId);
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "403 Forbidden - The bot account needs to be an editor of the publisher to post. Please check publisher membership settings.",
                details = ex.Message
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to reply to post {PostId}", postId);
            return JsonSerializer.Serialize(new { success = false, error = ex.Message }, JsonOptions);
        }
    }

    [KernelFunction("get_post")]
    [Description("Get a specific post by its ID. Returns JSON with post details.")]
    public async Task<string> GetPost(
        [Description("The ID of the post")] string postId
    )
    {
        try
        {
            var post = await apiClient.GetAsync<SnPost>("sphere", $"/posts/{postId}");

            if (post == null)
            {
                return JsonSerializer.Serialize(new { error = "Post not found" }, JsonOptions);
            }

            return JsonSerializer.Serialize(post, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get post {PostId}", postId);
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }
    }

    [KernelFunction("get_replies")]
    [Description("Get replies to a specific post. Only returns replies from other users (filters out SnChan's own replies).")]
    public async Task<string> GetReplies(
        [Description("The ID of the post to get replies for")]
        string postId,
        [Description("Maximum number of replies (default 20)")]
        int limit = 20
    )
    {
        try
        {
            // Get the bot's publisher info to filter out its own replies
            var botPublisher = await apiClient.GetAsync<SnPublisher>("sphere", "/publishers/me");
            var botPublisherId = botPublisher?.Id;

            // Get all posts that are replies to this post
            var posts = await apiClient.GetAsync<List<SnPost>>(
                "sphere",
                $"/posts?reply_to={postId}&take={limit * 2}" // Get more to account for filtering
            );

            if (posts == null || posts.Count == 0)
            {
                return JsonSerializer.Serialize(new { count = 0, replies = new List<SnPost>() }, JsonOptions);
            }

            // Filter out SnChan's own replies
            var filteredReplies = posts
                .Where(p => p.RepliedPostId != null && p.RepliedPostId.Value.ToString() == postId)
                .Where(p => botPublisherId == null || p.Publisher == null || p.Publisher.Id != botPublisherId)
                .Take(limit)
                .ToList();

            logger.LogInformation(
                "SnChan got {TotalCount} replies for post {PostId}, filtered to {FilteredCount} (excluding own replies)",
                posts.Count, postId, filteredReplies.Count);

            return JsonSerializer.Serialize(new
            {
                count = filteredReplies.Count,
                replies = filteredReplies
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get replies for post {PostId}", postId);
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }
    }

    [KernelFunction("get_my_posts")]
    [Description("Get posts created by SnChan bot account.")]
    public async Task<string> GetMyPosts(
        [Description("Maximum number of posts (default 20)")]
        int limit = 20,
        [Description("Skip how many posts")]
        int offset = 0
    )
    {
        try
        {
            var posts = await apiClient.GetAsync<List<SnPost>>(
                "sphere",
                $"/posts?offset={offset}&take={limit}"
            );

            var result = posts ?? new List<SnPost>();
            return JsonSerializer.Serialize(new { count = result.Count, posts = result }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get SnChan posts");
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }
    }

    [KernelFunction("search_posts")]
    [Description("Search for posts containing specific content. Returns JSON array of matching posts.")]
    public async Task<string> SearchPosts(
        [Description("Search query")] string query,
        [Description("Maximum number of results")]
        int limit = 20
    )
    {
        try
        {
            var posts = await apiClient.GetAsync<List<SnPost>>(
                "sphere",
                $"/posts/search?q={Uri.EscapeDataString(query)}&take={limit}"
            );

            if (posts == null || posts.Count == 0)
            {
                return JsonSerializer.Serialize(new { count = 0, posts = new List<SnPost>() }, JsonOptions);
            }

            return JsonSerializer.Serialize(new { count = posts.Count, posts }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to search posts with query: {Query}", query);
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }
    }

    [KernelFunction("get_publisher_context")]
    [Description("Get context about SnChan's available publishers to help decide which one to use. Returns guidance on when to use personal vs official publisher.")]
    public string GetPublisherContext()
    {
        return publisherService.GetPublisherContext();
    }
}
