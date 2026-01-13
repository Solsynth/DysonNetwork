using System.ServiceModel.Syndication;
using System.Xml;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Models.Embed;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Insight.Reader;

public class WebFeedService(
    AppDatabase database,
    IHttpClientFactory httpClientFactory,
    ILogger<WebFeedService> logger,
    WebReaderService readerService,
    RemotePublisherService remotePublisherService
)
{
    public async Task<SnWebFeed> CreateWebFeedAsync(SnPublisher publisher, WebFeedController.WebFeedRequest request)
    {
        var feed = new SnWebFeed
        {
            Url = request.Url!,
            Title = request.Title!,
            Description = request.Description,
            Config = request.Config ?? new WebFeedConfig(),
            PublisherId = publisher.Id,
            Publisher = publisher
        };

        database.WebFeeds.Add(feed);
        await database.SaveChangesAsync();

        return feed;
    }

    private async Task<SnPublisher?> LoadPublisherAsync(Guid publisherId, CancellationToken cancellationToken)
    {
        try
        {
            return await remotePublisherService.GetPublisher(id: publisherId.ToString(), cancellationToken: cancellationToken);
        }
        catch (Grpc.Core.RpcException)
        {
            return null;
        }
    }

    public async Task<SnWebFeed?> GetFeedAsync(Guid id, Guid? publisherId = null)
    {
        var query = database.WebFeeds
            .Where(a => a.Id == id)
            .AsQueryable();
        if (publisherId.HasValue)
            query = query.Where(a => a.PublisherId == publisherId.Value);
        var feed = await query.FirstOrDefaultAsync();
        if (feed != null)
        {
            feed.Publisher = await LoadPublisherAsync(feed.PublisherId, CancellationToken.None) ?? new SnPublisher();
        }
        return feed;
    }

    public async Task<List<SnWebFeed>> GetFeedsByPublisherAsync(Guid publisherId)
    {
        var feeds = await database.WebFeeds.Where(a => a.PublisherId == publisherId).ToListAsync();
        foreach (var feed in feeds)
        {
            feed.Publisher = await LoadPublisherAsync(feed.PublisherId, CancellationToken.None) ?? new SnPublisher();
        }
        return feeds;
    }

    public async Task<SnWebFeed> UpdateFeedAsync(SnWebFeed feed, WebFeedController.WebFeedRequest request)
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

        feed.Publisher = await LoadPublisherAsync(feed.PublisherId, CancellationToken.None) ?? new SnPublisher();

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

    public async Task ScrapeFeedAsync(SnWebFeed feed, CancellationToken cancellationToken = default)
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

            var articleExists = await database.Set<SnWebArticle>()
                .AnyAsync(a => a.FeedId == feed.Id && a.Url == itemUrl, cancellationToken);

            if (articleExists)
                continue;

            var content = (item.Content as TextSyndicationContent)?.Text ?? item.Summary.Text;
            LinkEmbed preview;

            if (feed.Config.ScrapPage)
            {
                var scrapedArticle = await readerService.ScrapeArticleAsync(itemUrl, cancellationToken);
                preview = scrapedArticle.LinkEmbed;
                if (scrapedArticle.Content is not null)
                    content = scrapedArticle.Content;
            }
            else
            {
                preview = await readerService.GetLinkPreviewAsync(itemUrl, cancellationToken);
            }

            var newArticle = new SnWebArticle
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