using System.Security.Claims;
using System.ServiceModel.Syndication;
using System.Xml;
using DysonNetwork.Sphere.Account;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DysonNetwork.Sphere.Connection.WebReader;

public class WebFeedService(
    AppDatabase database,
    IHttpClientFactory httpClientFactory,
    ILogger<WebFeedService> logger,
    AccountService accountService,
    WebReaderService webReaderService
)
{
    public async Task<WebFeed> CreateWebFeedAsync(WebFeedController.CreateWebFeedRequest request,
        ClaimsPrincipal claims)
    {
        if (claims.Identity?.Name == null)
        {
            throw new UnauthorizedAccessException();
        }

        var account = await accountService.LookupAccount(claims.Identity.Name);
        if (account == null)
        {
            throw new UnauthorizedAccessException();
        }

        var feed = new WebFeed
        {
            Url = request.Url,
            Title = request.Title,
            Description = request.Description,
            PublisherId = account.Id,
        };

        database.Set<WebFeed>().Add(feed);
        await database.SaveChangesAsync();

        return feed;
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
            {
                continue;
            }

            var articleExists = await database.Set<WebArticle>()
                .AnyAsync(a => a.FeedId == feed.Id && a.Url == itemUrl, cancellationToken);

            if (articleExists)
            {
                continue;
            }

            var content = (item.Content as TextSyndicationContent)?.Text ?? item.Summary.Text;
            LinkEmbed preview;

            if (feed.Config.ScrapPage)
            {
                var scrapedArticle = await webReaderService.ScrapeArticleAsync(itemUrl, cancellationToken);
                preview = scrapedArticle.LinkEmbed;
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
                PublishedAt = item.PublishDate.UtcDateTime,
                Preview = preview,
            };

            database.Set<WebArticle>().Add(newArticle);
        }

        await database.SaveChangesAsync(cancellationToken);
    }
}