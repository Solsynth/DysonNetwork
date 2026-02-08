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

    [KernelFunction("like_post")]
    [Description("Like a post.")]
    public async Task<object> LikePost(
        [Description("The ID of the post to like")] string postId
    )
    {
        try
        {
            await _apiClient.PostAsync("sphere", $"/posts/{postId}/like", new { });
            
            _logger.LogInformation("Liked post {PostId}", postId);
            return new { success = true, message = "Post liked successfully" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to like post {PostId}", postId);
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
                reply_to = postId
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
                repost_of = postId,
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
