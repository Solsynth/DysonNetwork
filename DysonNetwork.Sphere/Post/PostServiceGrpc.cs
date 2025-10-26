using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Models;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Sphere.Post;

public class PostServiceGrpc(AppDatabase db, PostService ps) : Shared.Proto.PostService.PostServiceBase
{
    public override async Task<Shared.Proto.Post> GetPost(GetPostRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid post id"));

        var post = await db.Posts
            .Include(p => p.Publisher)
            .Include(p => p.Tags)
            .Include(p => p.Categories)
            .Include(p => p.RepliedPost)
            .Include(p => p.ForwardedPost)
            .Include(p => p.FeaturedRecords)
            .FilterWithVisibility(null, [], [])
            .FirstOrDefaultAsync(p => p.Id == id);

        if (post == null) throw new RpcException(new Status(StatusCode.NotFound, "post not found"));

        post = await ps.LoadPostInfo(post);

        return post.ToProtoValue();
    }

    public override async Task<GetPostBatchResponse> GetPostBatch(GetPostBatchRequest request,
        ServerCallContext context)
    {
        var ids = request.Ids
            .Where(s => !string.IsNullOrWhiteSpace(s) && Guid.TryParse(s, out _))
            .Select(Guid.Parse)
            .ToList();

        if (ids.Count == 0) return new GetPostBatchResponse();

        var posts = await db.Posts
            .Include(p => p.Publisher)
            .Include(p => p.Tags)
            .Include(p => p.Categories)
            .Include(p => p.RepliedPost)
            .Include(p => p.ForwardedPost)
            .Include(p => p.FeaturedRecords)
            .Include(p => p.Awards)
            .Where(p => ids.Contains(p.Id))
            .FilterWithVisibility(null, [], [])
            .ToListAsync();

        posts = await ps.LoadPostInfo(posts, null);

        var resp = new GetPostBatchResponse();
        resp.Posts.AddRange(posts.Select(p => p.ToProtoValue()));
        return resp;
    }

    public override async Task<SearchPostsResponse> SearchPosts(SearchPostsRequest request, ServerCallContext context)
    {
        var query = db.Posts
            .Include(p => p.Publisher)
            .Include(p => p.Tags)
            .Include(p => p.Categories)
            .Include(p => p.RepliedPost)
            .Include(p => p.ForwardedPost)
            .Include(p => p.Awards)
            .Include(p => p.FeaturedRecords)
            .Where(p => p.DeletedAt == null) // Only active posts
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            // Simple search, assuming full-text search or title/content contains
            query = query.Where(p =>
                (p.Title != null && EF.Functions.ILike(p.Title, $"%{request.Query}%")) ||
                (p.Content != null && EF.Functions.ILike(p.Content, $"%{request.Query}%")) ||
                (p.Description != null && EF.Functions.ILike(p.Description, $"%{request.Query}%")));
        }

        if (!string.IsNullOrWhiteSpace(request.PublisherId) && Guid.TryParse(request.PublisherId, out var pid))
        {
            query = query.Where(p => p.PublisherId == pid);
        }

        if (!string.IsNullOrWhiteSpace(request.RealmId) && Guid.TryParse(request.RealmId, out var rid))
        {
            query = query.Where(p => p.RealmId == rid);
        }

        query = query.FilterWithVisibility(null, [], []);

        var totalSize = await query.CountAsync();

        // Apply pagination
        var pageSize = request.PageSize > 0 ? request.PageSize : 20;
        var pageToken = request.PageToken;
        var offset = string.IsNullOrEmpty(pageToken) ? 0 : int.Parse(pageToken);

        var posts = await query
            .OrderByDescending(p => p.PublishedAt ?? p.CreatedAt)
            .Skip(offset)
            .Take(pageSize)
            .ToListAsync();

        posts = await ps.LoadPostInfo(posts, null, true);

        var nextToken = offset + pageSize < totalSize ? (offset + pageSize).ToString() : string.Empty;

        var resp = new SearchPostsResponse();
        resp.Posts.AddRange(posts.Select(p => p.ToProtoValue()));
        resp.NextPageToken = nextToken;
        resp.TotalSize = totalSize;

        return resp;
    }

    public override async Task<ListPostsResponse> ListPosts(ListPostsRequest request, ServerCallContext context)
    {
        var query = db.Posts
            .Include(p => p.Publisher)
            .Include(p => p.Tags)
            .Include(p => p.Categories)
            .Include(p => p.RepliedPost)
            .Include(p => p.ForwardedPost)
            .Include(p => p.Awards)
            .Include(p => p.FeaturedRecords)
            .Where(p => p.DeletedAt == null)
            .AsQueryable();
        
        query = request.Shuffle
            ? query.OrderBy(e => EF.Functions.Random())
            : query.OrderByDescending(e => e.PublishedAt ?? e.CreatedAt);

        if (!string.IsNullOrWhiteSpace(request.PublisherId) && Guid.TryParse(request.PublisherId, out var pid))
            query = query.Where(p => p.PublisherId == pid);

        if (!string.IsNullOrWhiteSpace(request.RealmId) && Guid.TryParse(request.RealmId, out var rid))
            query = query.Where(p => p.RealmId == rid);

        if (request.Categories.Count > 0)
            query = query.Where(p => p.Categories.Any(c => request.Categories.Contains(c.Slug)));

        if (request.Tags.Count > 0)
            query = query.Where(p => p.Tags.Any(c => request.Tags.Contains(c.Slug)));

        if (request.Types_.Count > 0)
        {
            var types = request.Types_.Select(t => (Shared.Models.PostType)t).Distinct();
            query = query.Where(p => types.Contains(p.Type));
        }

        if (request.OnlyMedia)
        {
            query = query.Where(e => e.Attachments.Count > 0);
        }

        query = request.Pinned switch
        {
            // Pinned filtering
            Shared.Proto.PostPinMode.RealmPage when !string.IsNullOrWhiteSpace(request.RealmId) => query.Where(p =>
                p.PinMode == Shared.Models.PostPinMode.RealmPage),
            Shared.Proto.PostPinMode.PublisherPage when !string.IsNullOrWhiteSpace(request.PublisherId) =>
                query.Where(p => p.PinMode == Shared.Models.PostPinMode.PublisherPage),
            Shared.Proto.PostPinMode.ReplyPage => query.Where(p => p.PinMode == Shared.Models.PostPinMode.ReplyPage),
            _ => query.Where(p => p.PinMode == (Shared.Models.PostPinMode)request.Pinned)
        };

        // Include/exclude replies
        if (!request.IncludeReplies)
        {
            // Exclude reply posts, only root posts
            query = query.Where(e => e.RepliedPostId == null);
        }

        if (request.After != null)
        {
            var afterTime = request.After.ToInstant();
            query = query.Where(p => (p.CreatedAt >= afterTime) || (p.PublishedAt >= afterTime));
        }

        if (request.Before != null)
        {
            var beforeTime = request.Before.ToInstant();
            query = query.Where(p => (p.CreatedAt <= beforeTime) || (p.PublishedAt <= beforeTime));
        }

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            query = query.Where(p =>
                (p.Title != null && EF.Functions.ILike(p.Title, $"%{request.Query}%")) ||
                (p.Content != null && EF.Functions.ILike(p.Content, $"%{request.Query}%")) ||
                (p.Description != null && EF.Functions.ILike(p.Description, $"%{request.Query}%")));
        }

        // Visibility filter (simplified for grpc - no user context)
        query = query.FilterWithVisibility(null, [], []);

        var totalSize = await query.CountAsync();

        var pageSize = request.PageSize > 0 ? request.PageSize : 20;
        var pageToken = request.PageToken;
        var offset = string.IsNullOrEmpty(pageToken) ? 0 : int.Parse(pageToken);

        var posts = await query
            .Skip(offset)
            .Take(pageSize)
            .ToListAsync();

        posts = await ps.LoadPostInfo(posts, null, true);

        var nextToken = offset + pageSize < totalSize ? (offset + pageSize).ToString() : string.Empty;

        var resp = new ListPostsResponse();
        resp.Posts.AddRange(posts.Select(p => p.ToProtoValue()));
        resp.NextPageToken = nextToken;
        resp.TotalSize = totalSize;

        return resp;
    }
}