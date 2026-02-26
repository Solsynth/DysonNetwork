using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Insight.Reader;

public class WebFeedGrpcService(WebFeedService service, AppDatabase db)
    : DyWebFeedService.DyWebFeedServiceBase
{
    public override async Task<DyGetWebFeedResponse> GetWebFeed(
        DyGetWebFeedRequest request,
        ServerCallContext context
    )
    {
        SnWebFeed? feed = null;

        switch (request.IdentifierCase)
        {
            case DyGetWebFeedRequest.IdentifierOneofCase.Id:
                if (!string.IsNullOrWhiteSpace(request.Id) && Guid.TryParse(request.Id, out var id))
                    feed = await service.GetFeedAsync(id);
                break;
            case DyGetWebFeedRequest.IdentifierOneofCase.Url:
                feed = await db.Feeds.FirstOrDefaultAsync(f => f.Url == request.Url);
                break;
            case DyGetWebFeedRequest.IdentifierOneofCase.None:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        return feed == null
            ? throw new RpcException(new Status(StatusCode.NotFound, "feed not found"))
            : new DyGetWebFeedResponse { Feed = feed.ToProtoValue() };
    }

    public override async Task<DyListWebFeedsResponse> ListWebFeeds(
        DyListWebFeedsRequest request,
        ServerCallContext context
    )
    {
        if (!Guid.TryParse(request.PublisherId, out var publisherId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid publisher_id"));

        var feeds = await service.GetFeedsByPublisherAsync(publisherId);

        var response = new DyListWebFeedsResponse
        {
            TotalSize = feeds.Count
        };
        response.Feeds.AddRange(feeds.Select(f => f.ToProtoValue()));
        return response;
    }
}