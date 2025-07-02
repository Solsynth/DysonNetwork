using System.ServiceModel.Syndication;
using System.Xml;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Connection.WebReader;

public class WebFeedService(
    AppDatabase database,
    IHttpClientFactory httpClientFactory,
    ILogger<WebFeedService> logger,
    WebReaderService webReaderService
)
{
    public async Task<WebFeed> CreateWebFeedAsync(Publisher.Publisher publisher,
        WebFeedController.WebFeedRequest request)
    {
        var feed = new WebFeed
        {
            Url = request.Url!,
            Title = request.Title!,
            Description = request.Description,
            Config = request.Config ?? new WebFeedConfig(),
            PublisherId = publisher.Id,
        };

        database.Set<WebFeed>().Add(feed);
        await database.SaveChangesAsync();

        return feed;
    }

    public async Task<WebFeed?> GetFeedAsync(Guid id, Guid? publisherId = null)
    {
        var query = database.WebFeeds.Where(a => a.Id == id).AsQueryable();
        if (publisherId.HasValue)
            query = query.Where(a => a.PublisherId == publisherId.Value);
        return await query.FirstOrDefaultAsync();
    }

    public async Task<List<WebFeed>> GetFeedsByPublisherAsync(Guid publisherId)
    {
        return await database.WebFeeds.Where(a => a.PublisherId == publisherId).ToListAsync();
    }

    public async Task<WebFeed> UpdateFeedAsync(WebFeed feed, WebFeedController.WebFeedRequest request)
    {
        if (request.Url is not null)
            feed.Url = request.Url;
        if (request.Title is not null)
            feed.Title = request.Title;
        if (request.Description is not null)
            feed.Description = request.Description;
        if (request.Config is not null)
            feed.Config = request.Config;

        database.Update(feed);
        await database.SaveChangesAsync();

        return feed;
    }

    public async Task<bool> DeleteFeedAsync(Guid id)
    {
        var feed = await database.WebFeeds.FindAsync(id);
        if (feed == null)
        {
            return false;
        }

        database.WebFeeds.Remove(feed);
        await database.SaveChangesAsync();

        return true;
    }

    public async Task ScrapeFeedAsync(WebFeed feed, CancellationToken cancellationToken = default)
    {
        var httpClient = httpClientFactory.CreateClient();
        var response = await httpClient.GetAsync(feed.Url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = XmlReader.Create(stream);
        var syndicationFeed = SyndicationFeed.Load(reader);

        if (syndicationFeed == null)
        {
            logger.LogWarning("Could not parse syndication feed for {FeedUrl}", feed.Url);
            return;
        }

        foreach (var item in syndicationFeed.Items)
        {
            var itemUrl = item.Links.FirstOrDefault()?.Uri.ToString();
            if (string.IsNullOrEmpty(itemUrl))
                continue;

            var articleExists = await database.Set<WebArticle>()
                .AnyAsync(a => a.FeedId == feed.Id && a.Url == itemUrl, cancellationToken);

            if (articleExists)
                continue;

            var content = (item.Content as TextSyndicationContent)?.Text ?? item.Summary.Text;
            LinkEmbed preview;

            if (feed.Config.ScrapPage)
            {
                var scrapedArticle = await webReaderService.ScrapeArticleAsync(itemUrl, cancellationToken);
                preview = scrapedArticle.LinkEmbed;
                if (scrapedArticle.Content is not null)
                    content = scrapedArticle.Content;
            }
            else
            {
                preview = await webReaderService.GetLinkPreviewAsync(itemUrl, cancellationToken);
            }

            var newArticle = new WebArticle
            {
                FeedId = feed.Id,
                Title = item.Title.Text,
                Url = itemUrl,
                Author = item.Authors.FirstOrDefault()?.Name,
                Content = content,
                PublishedAt = item.LastUpdatedTime.UtcDateTime,
                Preview = preview,
            };

            database.WebArticles.Add(newArticle);
        }

        await database.SaveChangesAsync(cancellationToken);
    }
}