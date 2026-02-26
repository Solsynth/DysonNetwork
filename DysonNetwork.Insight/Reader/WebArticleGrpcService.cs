using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Insight.Reader;

public class WebArticleGrpcService(AppDatabase db) : DyWebArticleService.DyWebArticleServiceBase
{
    public override async Task<DyGetWebArticleResponse> GetWebArticle(
        DyGetWebArticleRequest request,
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
            : new DyGetWebArticleResponse { Article = article.ToProtoValue() };
    }

    public override async Task<DyGetWebArticleBatchResponse> GetWebArticleBatch(
        DyGetWebArticleBatchRequest request,
        ServerCallContext context
    )
    {
        var ids = request.Ids
            .Where(s => !string.IsNullOrWhiteSpace(s) && Guid.TryParse(s, out _))
            .Select(Guid.Parse)
            .ToList();

        if (ids.Count == 0)
            return new DyGetWebArticleBatchResponse();

        var articles = await db.FeedArticles
            .Include(a => a.Feed)
            .Where(a => ids.Contains(a.Id))
            .ToListAsync();

        var response = new DyGetWebArticleBatchResponse();
        response.Articles.AddRange(articles.Select(a => a.ToProtoValue()));
        return response;
    }

    public override async Task<DyListWebArticlesResponse> ListWebArticles(
        DyListWebArticlesRequest request,
        ServerCallContext context
    )
    {
        if (!Guid.TryParse(request.FeedId, out var feedId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid feed_id"));

        var query = db.FeedArticles
            .Include(a => a.Feed)
            .Where(a => a.FeedId == feedId);

        var articles = await query.ToListAsync();

        var response = new DyListWebArticlesResponse
        {
            TotalSize = articles.Count
        };
        response.Articles.AddRange(articles.Select(a => a.ToProtoValue()));
        return response;
    }

    public override async Task<DyGetRecentArticlesResponse> GetRecentArticles(
        DyGetRecentArticlesRequest request,
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

        var response = new DyGetRecentArticlesResponse();
        response.Articles.AddRange(articles.Select(a => a.ToProtoValue()));
        return response;
    }
}