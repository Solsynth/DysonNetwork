using System.ComponentModel;
using DysonNetwork.Shared.Models;
using Microsoft.SemanticKernel;

namespace DysonNetwork.Insight.MiChan.Plugins;

public class PostPlugin
{
    private readonly SolarNetworkApiClient _apiClient;
    private readonly ILogger<PostPlugin> _logger;

    public PostPlugin(SolarNetworkApiClient apiClient, ILogger<PostPlugin> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    [KernelFunction("get_post")]
    [Description("Get a specific post by its ID.")]
    public async Task<SnPost?> GetPost(
        [Description("The ID of the post")] string postId
    )
    {
        try
        {
            var post = await _apiClient.GetAsync<SnPost>("sphere", $"/posts/{postId}");
            return post;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get post {PostId}", postId);
            return null;
        }
    }

    [KernelFunction("create_post")]
    [Description("Create and publish a new post.")]
    public async Task<object> CreatePost(
        [Description("The content of the post")] string content,
        [Description("Optional visibility: public, followers, or private")] string visibility = "public"
    )
    {
        try
        {
            var request = new
            {
                content = content,
                visibility = visibility
            };

            var result = await _apiClient.PostAsync<object>("sphere", "/posts", request);
            
            _logger.LogInformation("Created new post");
            return new { success = true, message = "Post created successfully", data = result };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create post");
            return new { success = false, error = ex.Message };
        }
    }

    [KernelFunction("react_to_post")]
    [Description("React to a post with an emoji symbol.")]
    public async Task<object> ReactToPost(
        [Description("The ID of the post to react to")] string postId,
        [Description("The reaction symbol (thumb_up, heart, clap, laugh, party, pray, cry, confuse, angry, just_okay, thumb_down)")] string symbol = "thumb_up",
        [Description("The attitude: Positive, Negative, or Neutral")] string attitude = "Positive"
    )
    {
        try
        {
            // Validate symbol
            var validSymbols = new[] { "thumb_up", "thumb_down", "just_okay", "cry", "confuse", "clap", "laugh", "angry", "party", "pray", "heart" };
            if (!validSymbols.Contains(symbol.ToLower()))
            {
                return new { success = false, error = $"Invalid symbol. Valid symbols: {string.Join(", ", validSymbols)}" };
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

            await _apiClient.PostAsync("sphere", $"/posts/{postId}/reactions", request);
            
            _logger.LogInformation("Reacted to post {PostId} with {Symbol} ({Attitude})", postId, symbol, attitude);
            return new { success = true, message = $"Reacted with {symbol} successfully" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to react to post {PostId}", postId);
            return new { success = false, error = ex.Message };
        }
    }

    [KernelFunction("pin_post")]
    [Description("Pin a post to the profile or realm page.")]
    public async Task<object> PinPost(
        [Description("The ID of the post to pin")] string postId,
        [Description("Pin mode: ProfilePage or RealmPage")] string mode = "ProfilePage"
    )
    {
        try
        {
            var request = new
            {
                mode = mode
            };

            await _apiClient.PostAsync("sphere", $"/posts/{postId}/pin", request);
            
            _logger.LogInformation("Pinned post {PostId} to {Mode}", postId, mode);
            return new { success = true, message = $"Post pinned to {mode} successfully" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pin post {PostId}", postId);
            return new { success = false, error = ex.Message };
        }
    }

    [KernelFunction("unpin_post")]
    [Description("Unpin a post from the profile or realm page.")]
    public async Task<object> UnpinPost(
        [Description("The ID of the post to unpin")] string postId
    )
    {
        try
        {
            await _apiClient.DeleteAsync("sphere", $"/posts/{postId}/pin");
            
            _logger.LogInformation("Unpinned post {PostId}", postId);
            return new { success = true, message = "Post unpinned successfully" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unpin post {PostId}", postId);
            return new { success = false, error = ex.Message };
        }
    }

    [KernelFunction("reply_to_post")]
    [Description("Reply to a post.")]
    public async Task<object> ReplyToPost(
        [Description("The ID of the post to reply to")] string postId,
        [Description("The content of the reply")] string content
    )
    {
        try
        {
            var request = new
            {
                content = content,
                replied_post_id = postId
            };

            var result = await _apiClient.PostAsync<object>("sphere", "/posts", request);
            
            _logger.LogInformation("Replied to post {PostId}", postId);
            return new { success = true, message = "Reply created successfully", data = result };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reply to post {PostId}", postId);
            return new { success = false, error = ex.Message };
        }
    }

    [KernelFunction("repost_post")]
    [Description("Repost (share) a post.")]
    public async Task<object> RepostPost(
        [Description("The ID of the post to repost")] string postId,
        [Description("Optional comment to add with the repost")] string? comment = null
    )
    {
        try
        {
            var request = new
            {
                forwarded_post_id = postId,
                content = comment
            };

            var result = await _apiClient.PostAsync<object>("sphere", "/posts", request);
            
            _logger.LogInformation("Reposted post {PostId}", postId);
            return new { success = true, message = "Post reposted successfully", data = result };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to repost post {PostId}", postId);
            return new { success = false, error = ex.Message };
        }
    }

    [KernelFunction("search_posts")]
    [Description("Search for posts containing specific content.")]
    public async Task<List<SnPost>?> SearchPosts(
        [Description("Search query")] string query,
        [Description("Maximum number of results")] int limit = 20
    )
    {
        try
        {
            var posts = await _apiClient.GetAsync<List<SnPost>>(
                "sphere", 
                $"/posts/search?q={Uri.EscapeDataString(query)}&take={limit}"
            );
            
            return posts ?? new List<SnPost>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search posts with query: {Query}", query);
            return null;
        }
    }

    [KernelFunction("list_timeline")]
    [Description("Get the timeline (feed) posts.")]
    public async Task<List<SnPost>?> ListTimeline(
        [Description("Type of timeline: home, local, or global")] string timelineType = "home",
        [Description("Maximum number of posts")] int limit = 20
    )
    {
        try
        {
            var posts = await _apiClient.GetAsync<List<SnPost>>(
                "sphere", 
                $"/timeline/{timelineType}?take={limit}"
            );
            
            return posts ?? new List<SnPost>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get {TimelineType} timeline", timelineType);
            return null;
        }
    }
}
