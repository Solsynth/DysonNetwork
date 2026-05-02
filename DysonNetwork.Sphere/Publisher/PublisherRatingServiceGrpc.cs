using DysonNetwork.Shared.Proto;
using Grpc.Core;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Sphere.Publisher;

public class PublisherRatingServiceGrpc(PublisherRatingService ratingService)
    : DyPublisherRatingService.DyPublisherRatingServiceBase
{
    public override async Task<DyPublisherRatingRecord> AddRecord(
        DyAddPublisherRatingRecordRequest request,
        ServerCallContext context
    )
    {
        var publisherId = Guid.Parse(request.PublisherId);
        var record = await ratingService.AddRecord(
            request.ReasonType,
            request.Reason,
            request.Delta,
            publisherId
        );

        return new DyPublisherRatingRecord
        {
            Id = record.Id.ToString(),
            ReasonType = record.ReasonType,
            Reason = record.Reason,
            Delta = record.Delta,
            PublisherId = record.PublisherId.ToString(),
            CreatedAt = record.CreatedAt.ToTimestamp(),
            UpdatedAt = record.UpdatedAt.ToTimestamp()
        };
    }

    public override async Task<DyPublisherRatingResponse> GetRating(
        DyGetPublisherRatingRequest request,
        ServerCallContext context
    )
    {
        var publisherId = Guid.Parse(request.PublisherId);
        var amount = await ratingService.GetRating(publisherId);

        return new DyPublisherRatingResponse { Amount = amount };
    }
}
