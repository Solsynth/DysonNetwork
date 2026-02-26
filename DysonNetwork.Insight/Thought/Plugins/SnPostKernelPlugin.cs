using System.ComponentModel;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.SemanticKernel;
using NodaTime;
using NodaTime.Serialization.Protobuf;
using NodaTime.Text;

namespace DysonNetwork.Insight.Thought.Plugins;

public class SnPostKernelPlugin(
    DyPostService.DyPostServiceClient postClient,
    DyPublisherService.DyPublisherServiceClient publisherClient
)
{
    [KernelFunction("get_post")]
    public async Task<SnPost?> GetPost(string postId)
    {
        var request = new DyGetPostRequest { Id = postId };
        var response = await postClient.GetPostAsync(request);
        return response is null ? null : SnPost.FromProtoValue(response);
    }

    [KernelFunction("search_posts")]
    [Description("Perform a full-text search in all Solar Network posts.")]
    public async Task<List<SnPost>> SearchPostsContent(string contentQuery, int pageSize = 10, int page = 1)
    {
        var request = new DySearchPostsRequest
        {
            Query = contentQuery,
            PageSize = pageSize,
            PageToken = ((page - 1) * pageSize).ToString()
        };
        var response = await postClient.SearchPostsAsync(request);
        return response.Posts.Select(SnPost.FromProtoValue).ToList();
    }

    public class KernelPostListResult
    {
        public List<SnPost> Posts { get; set; } = [];
        public int TotalCount { get; set; }
    }

    [KernelFunction("list_posts")]
    [Description("List all posts on the Solar Network without filters, orderBy can be date or popularity")]
    public async Task<KernelPostListResult> ListPosts(
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
        return new KernelPostListResult
        {
            Posts = response.Posts.Select(SnPost.FromProtoValue).ToList(),
            TotalCount = response.TotalSize,
        };
    }

    [KernelFunction("list_posts_within_time")]
    [Description(
        "List posts in a period of time, the time requires ISO-8601 format, one of the start and end must be provided.")]
    public async Task<KernelPostListResult> ListPostsWithinTime(
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
        return new KernelPostListResult
        {
            Posts = response.Posts.Select(SnPost.FromProtoValue).ToList(),
            TotalCount = response.TotalSize,
        };
    }

    [KernelFunction("list_publisher_posts")]
    [Description("Get the specific publisher's posts.")]
    public async Task<KernelPostListResult> ListPublisherPosts(
        [Description("The id of publisher")] string pubId,
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
        return new KernelPostListResult
        {
            Posts = response.Posts.Select(SnPost.FromProtoValue).ToList(),
            TotalCount = response.TotalSize,
        };
    }

    [KernelFunction("get_publisher")]
    [Description("Get the publisher information.")]
    public async Task<SnPublisher?> GetPublisher(
        [Description("The name of publisher")] string name
    )
    {
        var request = new DyGetPublisherRequest { Name = name };
        var result = await publisherClient.GetPublisherAsync(request);
        return result is not null ? SnPublisher.FromProtoValue(result.Publisher) : null;
    }

    [KernelFunction("get_publisher_by_id")]
    [Description("Get the publisher information.")]
    public async Task<SnPublisher?> GetPublisherById(
        [Description("The id of publisher, must be well formatted GUID")]
        string id
    )
    {
        var request = new DyGetPublisherRequest { Id = id };
        var result = await publisherClient.GetPublisherAsync(request);
        return result is not null ? SnPublisher.FromProtoValue(result.Publisher) : null;
    }
}