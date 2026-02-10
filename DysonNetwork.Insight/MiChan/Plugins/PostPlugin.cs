using System.ComponentModel;
using DysonNetwork.Shared.Models;
using Microsoft.SemanticKernel;

namespace DysonNetwork.Insight.MiChan.Plugins;

public class PostPlugin(SolarNetworkApiClient apiClient, ILogger<PostPlugin> logger)
{
    [KernelFunction("get_post")]
    [Description("Get a specific post by its ID.")]
    public async Task<SnPost?> GetPost(
        [Description("The ID of the post")] string postId
    )
    {
        try
        {
            var post = await apiClient.GetAsync<SnPost>("sphere", $"/posts/{postId}");
            return post;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get post {PostId}", postId);
            return null;
        }
    }

    [KernelFunction("create_post")]
    [Description("Create and publish a new text-only post. MiChan is not capable of posting with attachments/ media.")]
    public async Task<object> CreatePost(
        [Description("The content of the post")]
        string content,
        [Description("The title of the post, optional")]
        string? title,
        [Description("The description of the post, optional")]
        string? description,
        [Description("The tags of the post, splitted by comma, optional")]
        string? tags
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

            var result = await apiClient.PostAsync<object>("sphere", "/posts", request);

            logger.LogInformation("Created new post");
            return new { success = true, message = "Post created successfully", data = result };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create post");
            return new { success = false, error = ex.Message };
        }
    }

    [KernelFunction("react_to_post")]
    [Description("React to a post with an emoji symbol.")]
    public async Task<object> ReactToPost(
        [Description("The ID of the post to react to")]
        string postId,
        [Description(
            "The reaction symbol (thumb_up, heart, clap, laugh, party, pray, cry, confuse, angry, just_okay, thumb_down)")]
        string symbol = "thumb_up",
        [Description("The attitude: Positive, Negative, or Neutral")]
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
                return new
                {
                    success = false, error = $"Invalid symbol. Valid symbols: {string.Join(", ", validSymbols)}"
                };
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
            return new { success = true, message = $"Reacted with {symbol} successfully" };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to react to post {PostId}", postId);
            return new { success = false, error = ex.Message };
        }
    }

    [KernelFunction("pin_post")]
    [Description("Pin a post to the profile or realm page.")]
    public async Task<object> PinPost(
        [Description("The ID of the post to pin")]
        string postId,
        [Description("Pin mode: ProfilePage or RealmPage")]
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
            return new { success = true, message = $"Post pinned to {mode} successfully" };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to pin post {PostId}", postId);
            return new { success = false, error = ex.Message };
        }
    }

    [KernelFunction("unpin_post")]
    [Description("Unpin a post from the profile or realm page.")]
    public async Task<object> UnpinPost(
        [Description("The ID of the post to unpin")]
        string postId
    )
    {
        try
        {
            await apiClient.DeleteAsync("sphere", $"/posts/{postId}/pin");

            logger.LogInformation("Unpinned post {PostId}", postId);
            return new { success = true, message = "Post unpinned successfully" };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to unpin post {PostId}", postId);
            return new { success = false, error = ex.Message };
        }
    }

    [KernelFunction("reply_to_post")]
    [Description("Reply to a post.")]
    public async Task<object> ReplyToPost(
        [Description("The ID of the post to reply to")]
        string postId,
        [Description("The content of the reply")]
        string content
    )
    {
        try
        {
            var request = new
            {
                content = content,
                replied_post_id = postId
            };

            var result = await apiClient.PostAsync<object>("sphere", "/posts", request);

            logger.LogInformation("Replied to post {PostId}", postId);
            return new { success = true, message = "Reply created successfully", data = result };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to reply to post {PostId}", postId);
            return new { success = false, error = ex.Message };
        }
    }

    [KernelFunction("repost_post")]
    [Description("Repost (share) a post.")]
    public async Task<object> RepostPost(
        [Description("The ID of the post to repost")]
        string postId,
        [Description("Optional comment to add with the repost")]
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
            return new { success = true, message = "Post reposted successfully", data = result };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to repost post {PostId}", postId);
            return new { success = false, error = ex.Message };
        }
    }

    [KernelFunction("search_posts")]
    [Description("Search for posts containing specific content.")]
    public async Task<List<SnPost>?> SearchPosts(
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

            return posts ?? new List<SnPost>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to search posts with query: {Query}", query);
            return null;
        }
    }

    [KernelFunction("list_posts")]
    [Description("Get the newest posts.")]
    public async Task<List<SnPost>?> ListPosts(
        [Description("Maximum number of posts")] int limit = 20, 
        [Description("Skip how many posts already saw in recent queries")] int offset = 0
    )
    {
        try
        {
            var posts = await apiClient.GetAsync<List<SnPost>>(
                "sphere",
                $"/posts?offset={offset}&take={limit}"
            );

            return posts ?? new List<SnPost>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get recent posts");
            return null;
        }
    }
    
    [KernelFunction("shuffle_posts")]
    [Description("Get the random posts.")]
    public async Task<List<SnPost>?> ShufflePosts(
        [Description("Maximum number of posts")] int limit = 20
    )
    {
        try
        {
            var posts = await apiClient.GetAsync<List<SnPost>>(
                "sphere",
                $"/posts?shuffle=true&take={limit}"
            );

            return posts ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get shuffled posts");
            return null;
        }
    }
    
    [KernelFunction("list_publisher_posts")]
    [Description("Get the specific publisher's posts.")]
    public async Task<List<SnPost>?> ListPublisherPosts(
        [Description("The name of publisher")] string name,
        [Description("Maximum number of posts")] int limit = 20, 
        [Description("Skip how many posts already saw in recent queries")] int offset = 0
    )
    {
        try
        {
            var posts = await apiClient.GetAsync<List<SnPost>>(
                "sphere",
                $"/posts?offset={offset}&take={limit}&pub={name}"
            );

            return posts ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to get posts of {name}");
            return null;
        }
    }

    [KernelFunction("get_publisher")]
    [Description("Get the publisher information.")]
    public async Task<SnPublisher?> GetPublisher(
        [Description("The name of publisher")] string name
    )
    {
        try
        {
            return await apiClient.GetAsync<SnPublisher?>(
                "sphere",
                $"/publishers/{name}"
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to get publisher info for @{name}");
            return null;
        }
    }
}