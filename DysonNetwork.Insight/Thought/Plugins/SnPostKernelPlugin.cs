using System.ComponentModel;
using System.Text.Json;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Insight.Agent.Foundation;
using NodaTime;
using NodaTime.Serialization.Protobuf;
using NodaTime.Text;

namespace DysonNetwork.Insight.Thought.Plugins;

public static class KernelPluginUtils
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string ToJson<T>(T obj) => JsonSerializer.Serialize(obj, JsonOptions);
}

public class SnPostKernelPlugin(
    DyPostService.DyPostServiceClient postClient,
    DyPublisherService.DyPublisherServiceClient publisherClient
)
{
    [AgentTool("get_post")]
    public async Task<string> GetPost(string postId)
    {
        var request = new DyGetPostRequest { Id = postId };
        var response = await postClient.GetPostAsync(request);
        return response is null ? KernelPluginUtils.ToJson(new { error = "Post not found" }) : KernelPluginUtils.ToJson(SnPost.FromProtoValue(response));
    }

    [AgentTool("search_posts", Description = "Perform a full-text search in all Solar Network posts.")]
    public async Task<string> SearchPostsContent(string contentQuery, int pageSize = 10, int page = 1)
    {
        var request = new DySearchPostsRequest
        {
            Query = contentQuery,
            PageSize = pageSize,
            PageToken = ((page - 1) * pageSize).ToString()
        };
        var response = await postClient.SearchPostsAsync(request);
        return KernelPluginUtils.ToJson(new { count = response.Posts.Count, posts = response.Posts.Select(SnPost.FromProtoValue).ToList() });
    }

    public class KernelPostListResult
    {
        public List<SnPost> Posts { get; set; } = [];
        public int TotalCount { get; set; }
    }

    [AgentTool("list_posts", Description = "List all posts on the Solar Network without filters, orderBy can be date or popularity")]
    public async Task<string> ListPosts(
        string orderBy = "date",
        bool orderDesc = true,
        int pageSize = 10,
        int page = 1
    )
    {
        var request = new DyListPostsRequest
        {
            OrderBy = orderBy,
            OrderDesc = orderDesc,
            PageSize = pageSize,
            PageToken = ((page - 1) * pageSize).ToString()
        };
        var response = await postClient.ListPostsAsync(request);
        return KernelPluginUtils.ToJson(new
        {
            totalCount = response.TotalSize,
            posts = response.Posts.Select(SnPost.FromProtoValue).ToList()
        });
    }

    [AgentTool("list_posts_within_time", Description =
        "List posts in a period of time, the time requires ISO-8601 format, one of the start and end must be provided.")]
    public async Task<string> ListPostsWithinTime(
        string? beforeTime,
        string? afterTime,
        int pageSize = 10,
        int page = 1
    )
    {
        var pattern = InstantPattern.General;
        Instant? before = !string.IsNullOrWhiteSpace(beforeTime)
            ? pattern.Parse(beforeTime).TryGetValue(default, out var beforeValue) ? beforeValue : null
            : null;
        Instant? after = !string.IsNullOrWhiteSpace(afterTime)
            ? pattern.Parse(afterTime).TryGetValue(default, out var afterValue) ? afterValue : null
            : null;
        var request = new DyListPostsRequest
        {
            After = after?.ToTimestamp(),
            Before = before?.ToTimestamp(),
            PageSize = pageSize,
            PageToken = ((page - 1) * pageSize).ToString()
        };
        var response = await postClient.ListPostsAsync(request);
        return KernelPluginUtils.ToJson(new
        {
            totalCount = response.TotalSize,
            posts = response.Posts.Select(SnPost.FromProtoValue).ToList()
        });
    }

    [AgentTool("list_publisher_posts", Description = "Get the specific publisher's posts.")]
    public async Task<string> ListPublisherPosts(
        [AgentToolParameter("The id of publisher")] string pubId,
        int pageSize = 10,
        int page = 1
    )
    {
        var request = new DyListPostsRequest
        {
            PublisherId = pubId,
            PageSize = pageSize,
            PageToken = ((page - 1) * pageSize).ToString()
        };
        var response = await postClient.ListPostsAsync(request);
        return KernelPluginUtils.ToJson(new
        {
            totalCount = response.TotalSize,
            posts = response.Posts.Select(SnPost.FromProtoValue).ToList()
        });
    }

    [AgentTool("get_publisher", Description = "Get the publisher information.")]
    public async Task<string> GetPublisher(
        [AgentToolParameter("The name of publisher")] string name
    )
    {
        var request = new DyGetPublisherRequest { Name = name };
        var result = await publisherClient.GetPublisherAsync(request);
        return result is not null ? KernelPluginUtils.ToJson(SnPublisher.FromProtoValue(result.Publisher)) : KernelPluginUtils.ToJson(new { error = $"Publisher {name} not found" });
    }

    [AgentTool("get_publisher_by_id", Description = "Get the publisher information.")]
    public async Task<string> GetPublisherById(
        [AgentToolParameter("The id of publisher, must be well formatted GUID")] string id
    )
    {
        var request = new DyGetPublisherRequest { Id = id };
        var result = await publisherClient.GetPublisherAsync(request);
        return result is not null ? KernelPluginUtils.ToJson(SnPublisher.FromProtoValue(result.Publisher)) : KernelPluginUtils.ToJson(new { error = $"Publisher {id} not found" });
    }
}
