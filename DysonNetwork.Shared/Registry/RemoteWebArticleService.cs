using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;

namespace DysonNetwork.Shared.Registry;

public class RemoteWebArticleService(DyWebArticleService.DyWebArticleServiceClient webArticles)
{
    public async Task<SnWebArticle> GetWebArticle(Guid id)
    {
        var request = new DyGetWebArticleRequest { Id = id.ToString() };
        var response = await webArticles.GetWebArticleAsync(request);
        return response.Article != null ? SnWebArticle.FromProtoValue(response.Article) : null!;
    }

    public async Task<List<SnWebArticle>> GetWebArticleBatch(List<Guid> ids)
    {
        var request = new DyGetWebArticleBatchRequest();
        request.Ids.AddRange(ids.Select(id => id.ToString()));
        var response = await webArticles.GetWebArticleBatchAsync(request);
        return response.Articles.Select(SnWebArticle.FromProtoValue).ToList();
    }

    public async Task<List<SnWebArticle>> ListWebArticles(Guid feedId)
    {
        var request = new DyListWebArticlesRequest { FeedId = feedId.ToString() };
        var response = await webArticles.ListWebArticlesAsync(request);
        return response.Articles.Select(SnWebArticle.FromProtoValue).ToList();
    }

    public async Task<List<SnWebArticle>> GetRecentArticles(int limit = 20)
    {
        var request = new DyGetRecentArticlesRequest { Limit = limit };
        var response = await webArticles.GetRecentArticlesAsync(request);
        return response.Articles.Select(SnWebArticle.FromProtoValue).ToList();
    }
}
