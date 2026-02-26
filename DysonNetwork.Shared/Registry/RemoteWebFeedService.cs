using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;

namespace DysonNetwork.Shared.Registry;

public class RemoteWebFeedService(DyWebFeedService.DyWebFeedServiceClient webFeeds)
{
    public async Task<SnWebFeed> GetWebFeed(Guid id)
    {
        var request = new DyGetWebFeedRequest { Id = id.ToString() };
        var response = await webFeeds.GetWebFeedAsync(request);
        return response.Feed != null ? SnWebFeed.FromProtoValue(response.Feed) : null!;
    }

    public async Task<SnWebFeed> GetWebFeedByUrl(string url)
    {
        var request = new DyGetWebFeedRequest { Url = url };
        var response = await webFeeds.GetWebFeedAsync(request);
        return response.Feed != null ? SnWebFeed.FromProtoValue(response.Feed) : null!;
    }

    public async Task<List<SnWebFeed>> ListWebFeeds(Guid publisherId)
    {
        var request = new DyListWebFeedsRequest { PublisherId = publisherId.ToString() };
        var response = await webFeeds.ListWebFeedsAsync(request);
        return response.Feeds.Select(SnWebFeed.FromProtoValue).ToList();
    }
}
