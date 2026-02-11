using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Insight.Reader;

public class WebArticleGrpcService(AppDatabase db) : WebArticleService.WebArticleServiceBase
{
    public override async Task<GetWebArticleResponse> GetWebArticle(
        GetWebArticleRequest request,
        ServerCallContext context
    )
    {
        if (!Guid.TryParse(request.Id, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid id"));

        var article = await db.FeedArticles
            .Include(a => a.Feed)
            .FirstOrDefaultAsync(a => a.Id == id);

        return article == null
            ? throw new RpcException(new Status(StatusCode.NotFound, "article not found"))
            : new GetWebArticleResponse { Article = article.ToProtoValue() };
    }

    public override async Task<GetWebArticleBatchResponse> GetWebArticleBatch(
        GetWebArticleBatchRequest request,
        ServerCallContext context
    )
    {
        var ids = request.Ids
            .Where(s => !string.IsNullOrWhiteSpace(s) && Guid.TryParse(s, out _))
            .Select(Guid.Parse)
            .ToList();

        if (ids.Count == 0)
            return new GetWebArticleBatchResponse();

        var articles = await db.FeedArticles
            .Include(a => a.Feed)
            .Where(a => ids.Contains(a.Id))
            .ToListAsync();

        var response = new GetWebArticleBatchResponse();
        response.Articles.AddRange(articles.Select(a => a.ToProtoValue()));
        return response;
    }

    public override async Task<ListWebArticlesResponse> ListWebArticles(
        ListWebArticlesRequest request,
        ServerCallContext context
    )
    {
        if (!Guid.TryParse(request.FeedId, out var feedId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid feed_id"));

        var query = db.FeedArticles
            .Include(a => a.Feed)
            .Where(a => a.FeedId == feedId);

        var articles = await query.ToListAsync();

        var response = new ListWebArticlesResponse
        {
            TotalSize = articles.Count
        };
        response.Articles.AddRange(articles.Select(a => a.ToProtoValue()));
        return response;
    }

    public override async Task<GetRecentArticlesResponse> GetRecentArticles(
        GetRecentArticlesRequest request,
        ServerCallContext context
    )
    {
        var limit = request.Limit > 0 ? request.Limit : 20;

        var articles = await db.FeedArticles
            .Include(a => a.Feed)
            .OrderByDescending(a => a.PublishedAt ?? DateTime.MinValue)
            .ThenByDescending(a => a.CreatedAt)
            .Take(limit)
            .ToListAsync();

        var response = new GetRecentArticlesResponse();
        response.Articles.AddRange(articles.Select(a => a.ToProtoValue()));
        return response;
    }
}