using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;

namespace DysonNetwork.Shared.Registry;

public class RemoteWebArticleService(WebArticleService.WebArticleServiceClient webArticles)
{
    public async Task<SnWebArticle> GetWebArticle(Guid id)
    {
        var request = new GetWebArticleRequest { Id = id.ToString() };
        var response = await webArticles.GetWebArticleAsync(request);
        return response.Article != null ? SnWebArticle.FromProtoValue(response.Article) : null!;
    }

    public async Task<List<SnWebArticle>> GetWebArticleBatch(List<Guid> ids)
    {
        var request = new GetWebArticleBatchRequest();
        request.Ids.AddRange(ids.Select(id => id.ToString()));
        var response = await webArticles.GetWebArticleBatchAsync(request);
        return response.Articles.Select(SnWebArticle.FromProtoValue).ToList();
    }

    public async Task<List<SnWebArticle>> ListWebArticles(Guid feedId)
    {
        var request = new ListWebArticlesRequest { FeedId = feedId.ToString() };
        var response = await webArticles.ListWebArticlesAsync(request);
        return response.Articles.Select(SnWebArticle.FromProtoValue).ToList();
    }

    public async Task<List<SnWebArticle>> GetRecentArticles(int limit = 20)
    {
        var request = new GetRecentArticlesRequest { Limit = limit };
        var response = await webArticles.GetRecentArticlesAsync(request);
        return response.Articles.Select(SnWebArticle.FromProtoValue).ToList();
    }
}
