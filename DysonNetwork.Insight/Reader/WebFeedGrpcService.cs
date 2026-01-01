using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Insight.Reader;

public class WebFeedGrpcService(WebFeedService service, AppDatabase db)
    : Shared.Proto.WebFeedService.WebFeedServiceBase
{
    public override async Task<GetWebFeedResponse> GetWebFeed(
        GetWebFeedRequest request,
        ServerCallContext context
    )
    {
        SnWebFeed? feed = null;

        switch (request.IdentifierCase)
        {
            case GetWebFeedRequest.IdentifierOneofCase.Id:
                if (!string.IsNullOrWhiteSpace(request.Id) && Guid.TryParse(request.Id, out var id))
                    feed = await service.GetFeedAsync(id);
                break;
            case GetWebFeedRequest.IdentifierOneofCase.Url:
                feed = await db.WebFeeds.FirstOrDefaultAsync(f => f.Url == request.Url);
                break;
            case GetWebFeedRequest.IdentifierOneofCase.None:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        return feed == null
            ? throw new RpcException(new Status(StatusCode.NotFound, "feed not found"))
            : new GetWebFeedResponse { Feed = feed.ToProtoValue() };
    }

    public override async Task<ListWebFeedsResponse> ListWebFeeds(
        ListWebFeedsRequest request,
        ServerCallContext context
    )
    {
        if (!Guid.TryParse(request.PublisherId, out var publisherId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid publisher_id"));

        var feeds = await service.GetFeedsByPublisherAsync(publisherId);

        var response = new ListWebFeedsResponse
        {
            TotalSize = feeds.Count
        };
        response.Feeds.AddRange(feeds.Select(f => f.ToProtoValue()));
        return response;
    }
}