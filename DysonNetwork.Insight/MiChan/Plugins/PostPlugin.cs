using System.ComponentModel;
using System.Text.Json;
using DysonNetwork.Shared.Models;
using DysonNetwork.Insight.Agent.Foundation;

namespace DysonNetwork.Insight.MiChan.Plugins;

public class PostPlugin(SolarNetworkApiClient apiClient, ILogger<PostPlugin> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    [AgentTool("get_post", Description = "Get a specific post by its ID. Returns JSON with post details including content, author, reactions, etc.")]
    public async Task<string> GetPost(
        [AgentToolParameter("The ID of the post")] string postId
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

    [AgentTool("create_post", Description = "Create and publish a new post. Returns JSON with success status and created post data.")]
    public async Task<string> CreatePost(
        [AgentToolParameter("The content of the post")]
        string content,
        [AgentToolParameter("The title of the post, optional")]
        string? title,
        [AgentToolParameter("The description of the post, optional")]
        string? description,
        [AgentToolParameter("The tags of the post, splitted by comma, optional")]
        string? tags,
        [AgentToolParameter("List of attachment IDs to include with the post, optional")]
        List<string>? attachments
    )
    {
        try
        {
            var request = new Dictionary<string, object>()
            {
                ["content"] = content,
            };
            if (!string.IsNullOrWhiteSpace(title)) request["title"] = title;
            if (!string.IsNullOrWhiteSpace(description)) request["description"] = description;
            if (!string.IsNullOrWhiteSpace(tags)) request["tags"] = tags.Split(',').Select(x => x.Trim()).ToArray();
            if (attachments is { Count: > 0 }) request["attachment_ids"] = attachments;

            var result = await apiClient.PostAsync<object>("sphere", "/posts", request);

            logger.LogInformation("Created new post");
            return JsonSerializer.Serialize(new { success = true, message = "Post created successfully", data = result }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create post");
            return JsonSerializer.Serialize(new { success = false, error = ex.Message }, JsonOptions);
        }
    }

    [AgentTool("react_to_post", Description = "React to a post with an emoji symbol. Returns JSON with success status.")]
    public async Task<string> ReactToPost(
        [AgentToolParameter("The ID of the post to react to")]
        string postId,
        [AgentToolParameter(
            "The reaction symbol (thumb_up, heart, clap, laugh, party, pray, cry, confuse, angry, just_okay, thumb_down)")]
        string symbol = "thumb_up",
        [AgentToolParameter("The attitude: Positive, Negative, or Neutral")]
        string attitude = "Positive"
    )
    {
        try
        {
            // Validate symbol
            var validSymbols = new[]
            {
                "thumb_up", "thumb_down", "just_okay", "cry", "confuse", "clap", "laugh", "angry", "party", "pray",
                "heart"
            };
            if (!validSymbols.Contains(symbol.ToLower()))
            {
                return JsonSerializer.Serialize(new
                {
                    success = false, error = $"Invalid symbol. Valid symbols: {string.Join(", ", validSymbols)}"
                }, JsonOptions);
            }

            // Map attitude string to enum value (PostReactionAttitude: Positive=0, Neutral=1, Negative=2)
            var attitudeValue = attitude.ToLower() switch
            {
                "negative" => 2,
                "neutral" => 1,
                _ => 0 // Positive
            };

            var request = new
            {
                symbol = symbol.ToLower(),
                attitude = attitudeValue
            };

            await apiClient.PostAsync("sphere", $"/posts/{postId}/reactions", request);

            logger.LogInformation("Reacted to post {PostId} with {Symbol} ({Attitude})", postId, symbol, attitude);
            return JsonSerializer.Serialize(new { success = true, message = $"Reacted with {symbol} successfully" }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to react to post {PostId}", postId);
            return JsonSerializer.Serialize(new { success = false, error = ex.Message }, JsonOptions);
        }
    }

    [AgentTool("pin_post", Description = "Pin a post to the profile or realm page. Returns JSON with success status.")]
    public async Task<string> PinPost(
        [AgentToolParameter("The ID of the post to pin")]
        string postId,
        [AgentToolParameter("Pin mode: ProfilePage or RealmPage")]
        string mode = "ProfilePage"
    )
    {
        try
        {
            var request = new Dictionary<string, object>()
            {
                ["mode"] = mode
            };

            await apiClient.PostAsync("sphere", $"/posts/{postId}/pin", request);

            logger.LogInformation("Pinned post {PostId} to {Mode}", postId, mode);
            return JsonSerializer.Serialize(new { success = true, message = $"Post pinned to {mode} successfully" }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to pin post {PostId}", postId);
            return JsonSerializer.Serialize(new { success = false, error = ex.Message }, JsonOptions);
        }
    }

    [AgentTool("unpin_post", Description = "Unpin a post from the profile or realm page. Returns JSON with success status.")]
    public async Task<string> UnpinPost(
        [AgentToolParameter("The ID of the post to unpin")]
        string postId
    )
    {
        try
        {
            await apiClient.DeleteAsync("sphere", $"/posts/{postId}/pin");

            logger.LogInformation("Unpinned post {PostId}", postId);
            return JsonSerializer.Serialize(new { success = true, message = "Post unpinned successfully" }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to unpin post {PostId}", postId);
            return JsonSerializer.Serialize(new { success = false, error = ex.Message }, JsonOptions);
        }
    }

    [AgentTool("reply_to_post", Description = "Reply to a post. Returns JSON with success status and created reply data.")]
    public async Task<string> ReplyToPost(
        [AgentToolParameter("The ID of the post to reply to")]
        string postId,
        [AgentToolParameter("The content of the reply")]
        string content
    )
    {
        try
        {
            var request = new Dictionary<string, object>
            {
                ["content"] = content,
                ["replied_post_id"] = postId
            };

            var result = await apiClient.PostAsync<object>("sphere", "/posts", request);

            logger.LogInformation("Replied to post {PostId}", postId);
            return JsonSerializer.Serialize(new { success = true, message = "Reply created successfully", data = result }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to reply to post {PostId}", postId);
            return JsonSerializer.Serialize(new { success = false, error = ex.Message }, JsonOptions);
        }
    }

    [AgentTool("repost_post", Description = "Repost (share) a post. Returns JSON with success status and created repost data.")]
    public async Task<string> RepostPost(
        [AgentToolParameter("The ID of the post to repost")]
        string postId,
        [AgentToolParameter("Optional comment to add with the repost")]
        string? comment = null
    )
    {
        try
        {
            var request = new
            {
                forwarded_post_id = postId,
                content = comment
            };

            var result = await apiClient.PostAsync<object>("sphere", "/posts", request);

            logger.LogInformation("Reposted post {PostId}", postId);
            return JsonSerializer.Serialize(new { success = true, message = "Post reposted successfully", data = result }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to repost post {PostId}", postId);
            return JsonSerializer.Serialize(new { success = false, error = ex.Message }, JsonOptions);
        }
    }

    [AgentTool("search_posts", Description = "Search for posts containing specific content. Returns JSON array of matching posts.")]
    public async Task<string> SearchPosts(
        [AgentToolParameter("Search query")] string query,
        [AgentToolParameter("Maximum number of results")]
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

    [AgentTool("list_posts", Description = "Get the newest posts. Returns JSON array of posts.")]
    public async Task<string> ListPosts(
        [AgentToolParameter("Maximum number of posts")]
        int limit = 20,
        [AgentToolParameter("Skip how many posts already saw in recent queries")]
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
            logger.LogError(ex, "Failed to get recent posts");
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }
    }

    [AgentTool("shuffle_posts", Description = "Get random posts. Returns JSON array of random posts.")]
    public async Task<string> ShufflePosts(
        [AgentToolParameter("Maximum number of posts")]
        int limit = 20
    )
    {
        try
        {
            var posts = await apiClient.GetAsync<List<SnPost>>(
                "sphere",
                $"/posts?shuffle=true&take={limit}"
            );

            if (posts == null || posts.Count == 0)
            {
                return JsonSerializer.Serialize(new { count = 0, posts = new List<SnPost>() }, JsonOptions);
            }

            return JsonSerializer.Serialize(new { count = posts.Count, posts }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get shuffled posts");
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }
    }

    [AgentTool("list_publisher_posts", Description = "Get the specific publisher's posts. Returns JSON array of posts.")]
    public async Task<string> ListPublisherPosts(
        [AgentToolParameter("The name of publisher")] string name,
        [AgentToolParameter("Maximum number of posts")]
        int limit = 20,
        [AgentToolParameter("Skip how many posts already saw in recent queries")]
        int offset = 0
    )
    {
        try
        {
            var posts = await apiClient.GetAsync<List<SnPost>>(
                "sphere",
                $"/posts?offset={offset}&take={limit}&pub={name}"
            );

            if (posts == null || posts.Count == 0)
            {
                return JsonSerializer.Serialize(new { count = 0, posts = new List<SnPost>() }, JsonOptions);
            }

            return JsonSerializer.Serialize(new { count = posts.Count, posts }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to get posts of {name}");
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }
    }

    [AgentTool("get_publisher", Description = "Get the publisher information. Returns JSON with publisher details.")]
    public async Task<string> GetPublisher(
        [AgentToolParameter("The name of publisher")] string name
    )
    {
        try
        {
            var publisher = await apiClient.GetAsync<SnPublisher>(
                "sphere",
                $"/publishers/{name}"
            );

            if (publisher == null)
            {
                return JsonSerializer.Serialize(new { error = $"Publisher @{name} not found" }, JsonOptions);
            }

            return JsonSerializer.Serialize(publisher, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to get publisher info for @{name}");
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }
    }
}
