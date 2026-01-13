using System.ServiceModel.Syndication;
using System.Xml;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Models.Embed;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Insight.Reader;

public class WebFeedService(
    AppDatabase database,
    IHttpClientFactory httpClientFactory,
    ILogger<WebFeedService> logger,
    WebReaderService readerService,
    RemotePublisherService remotePublisherService
)
{
    private const string VerificationFileName = "solar-network-feed.txt";
    private static readonly TimeZoneInfo UtcZone = TimeZoneInfo.Utc;

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

    public async Task<WebFeedVerificationInitResult> GenerateVerificationCodeAsync(Guid feedId)
    {
        var feed = await database.WebFeeds.FindAsync(feedId);
        if (feed == null)
            throw new InvalidOperationException($"Feed with ID {feedId} not found");

        var domain = GetDomainFromUrl(feed.Url);
        var verificationCode = GenerateVerificationCode();
        var verificationUrl = $"https://{domain}/.well-known/{VerificationFileName}";

        feed.VerificationKey = verificationCode;
        await database.SaveChangesAsync();

        return new WebFeedVerificationInitResult
        {
            VerificationUrl = verificationUrl,
            Code = verificationCode,
            Instructions = $"Create a file at '{verificationUrl}' containing only this verification code."
        };
    }

    public async Task<WebFeedVerificationResult> VerifyOwnershipAsync(Guid feedId, CancellationToken cancellationToken = default)
    {
        var feed = await database.WebFeeds.FindAsync(feedId);
        if (feed == null)
            throw new InvalidOperationException($"Feed with ID {feedId} not found");

        if (string.IsNullOrEmpty(feed.VerificationKey))
            return new WebFeedVerificationResult
            {
                Success = false,
                Message = "No verification code generated. Please call the init endpoint first."
            };

        var domain = GetDomainFromUrl(feed.Url);
        var verificationUrl = $"https://{domain}/.well-known/{VerificationFileName}";

        try
        {
            using var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);
            var response = await httpClient.GetAsync(verificationUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                await RevokeVerificationAsync(feed, "Verification file not found or inaccessible");
                return new WebFeedVerificationResult
                {
                    Success = false,
                    Message = $"Verification file not found (HTTP {response.StatusCode}). Verification status has been revoked."
                };
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var trimmedContent = content.Trim();

            if (trimmedContent != feed.VerificationKey)
            {
                await RevokeVerificationAsync(feed, "Verification code mismatch");
                return new WebFeedVerificationResult
                {
                    Success = false,
                    Message = "Verification code does not match. Verification status has been revoked."
                };
            }

            feed.VerifiedAt = SystemClock.Instance.GetCurrentInstant();
            feed.VerificationKey = null;
            await database.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Successfully verified ownership of feed {FeedId} at {Url}", feedId, feed.Url);

            return new WebFeedVerificationResult
            {
                Success = true,
                VerifiedAt = feed.VerifiedAt.Value.ToDateTimeUtc(),
                Message = "Website ownership verified successfully."
            };
        }
        catch (TaskCanceledException)
        {
            await RevokeVerificationAsync(feed, "Verification request timed out");
            return new WebFeedVerificationResult
            {
                Success = false,
                Message = "Verification request timed out. Verification status has been revoked."
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error during verification for feed {FeedId}", feedId);
            await RevokeVerificationAsync(feed, $"Verification error: {ex.Message}");
            return new WebFeedVerificationResult
            {
                Success = false,
                Message = $"Error during verification: {ex.Message}. Verification status has been revoked."
            };
        }
    }

    public async Task RevokeVerificationAsync(SnWebFeed feed, string reason)
    {
        logger.LogWarning("Revoking verification for feed {FeedId}: {Reason}", feed.Id, reason);
        feed.VerifiedAt = null;
        feed.VerificationKey = null;
        await database.SaveChangesAsync();
    }

    public async Task VerifyAllFeedsAsync(CancellationToken cancellationToken = default)
    {
        var verifiedFeeds = await database.WebFeeds
            .Where(f => f.VerifiedAt.HasValue)
            .ToListAsync(cancellationToken);

        logger.LogInformation("Starting periodic verification check for {Count} feeds", verifiedFeeds.Count);

        foreach (var feed in verifiedFeeds)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            await VerifyOwnershipAsync(feed.Id, cancellationToken);
        }

        logger.LogInformation("Completed periodic verification check");
    }

    private static string GenerateVerificationCode()
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
        var randomPart = Guid.NewGuid().ToString("N")[..16];
        return $"dn_{timestamp}_{randomPart}";
    }

    private static string GetDomainFromUrl(string url)
    {
        var uri = new Uri(url);
        return uri.Host;
    }
}

public class WebFeedVerificationInitResult
{
    public string VerificationUrl { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
}

public class WebFeedVerificationResult
{
    public bool Success { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string Message { get; set; } = string.Empty;
}