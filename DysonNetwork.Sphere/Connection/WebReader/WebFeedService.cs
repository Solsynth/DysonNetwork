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
    AccountService accountService
)
{
    public async Task<WebFeed> CreateWebFeedAsync(WebFeedController.CreateWebFeedRequest dto, ClaimsPrincipal claims)
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
            Url = dto.Url,
            Title = dto.Title,
            Description = dto.Description,
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

            var newArticle = new WebArticle
            {
                FeedId = feed.Id,
                Title = item.Title.Text,
                Url = itemUrl,
                Author = item.Authors.FirstOrDefault()?.Name,
                Content = (item.Content as TextSyndicationContent)?.Text ?? item.Summary.Text,
                PublishedAt = item.PublishDate.UtcDateTime,
            };

            database.Set<WebArticle>().Add(newArticle);
        }

        await database.SaveChangesAsync(cancellationToken);
    }
}