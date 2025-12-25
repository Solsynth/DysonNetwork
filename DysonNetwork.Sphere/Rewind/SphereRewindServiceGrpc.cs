using System.Globalization;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Rewind;

public class SphereRewindServiceGrpc(
    AppDatabase db,
    RemoteAccountService remoteAccounts,
    Publisher.PublisherService ps
) : RewindService.RewindServiceBase
{
    public override async Task<RewindEvent> GetRewindEvent(RequestRewindEvent request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var year = request.Year;

        var startDate = Instant.FromDateTimeUtc(new DateTime(year - 1, 12, 26));
        var endDate = Instant.FromDateTimeUtc(new DateTime(year, 12, 26));

        // Audience data
        var mostLovedPublisherClue =
            await db.PostReactions
                .Where(a => a.CreatedAt >= startDate && a.CreatedAt < endDate)
                .Where(p => p.AccountId == accountId && p.Attitude == Shared.Models.PostReactionAttitude.Positive)
                .GroupBy(p => p.Post.PublisherId)
                .OrderByDescending(g => g.Count())
                .Select(g => new { PublisherId = g.Key, ReactionCount = g.Count() })
                .FirstOrDefaultAsync();
        var mostLovedPublisher = mostLovedPublisherClue is not null
            ? ps.GetPublisherLoaded(mostLovedPublisherClue.PublisherId)
            : null;

        // Creator data
        var publishers = await db.PublisherMembers
            .Where(pm => pm.AccountId == accountId)
            .Select(pm => pm.PublisherId)
            .ToListAsync();

        var mostLovedAudienceClue =
            await db.PostReactions
                .Where(a => a.CreatedAt >= startDate && a.CreatedAt < endDate)
                .Where(pr =>
                    pr.Attitude == Shared.Models.PostReactionAttitude.Positive &&
                    publishers.Contains(pr.Post.PublisherId))
                .GroupBy(pr => pr.AccountId)
                .OrderByDescending(g => g.Count())
                .Select(g => new { AccountId = g.Key, ReactionCount = g.Count() })
                .FirstOrDefaultAsync();
        var mostLovedAudience = mostLovedAudienceClue is not null
            ? await remoteAccounts.GetAccount(mostLovedAudienceClue.AccountId)
            : null;

        var posts = db.Posts
            .Where(a => a.CreatedAt >= startDate && a.CreatedAt < endDate)
            .Where(p => publishers.Contains(p.PublisherId))
            .AsQueryable();
        var postTotalCount = await posts.CountAsync();
        var mostPopularPost = await posts
            .OrderByDescending(p => p.Upvotes - p.Downvotes)
            .FirstOrDefaultAsync();
        var mostProductiveDay = (await posts.Select(p => new { p.CreatedAt }).ToListAsync())
            .GroupBy(p => p.CreatedAt.ToDateTimeUtc().Date)
            .OrderByDescending(g => g.Count())
            .Select(g => new { Date = g.Key, PostCount = g.Count() })
            .FirstOrDefault();

        var data = new Dictionary<string, object?>
        {
            ["total_count"] = postTotalCount,
            ["most_popular_post"] = mostPopularPost,
            ["most_productive_day"] = mostProductiveDay is not null
                ? new Dictionary<string, object?>
                {
                    ["date"] = mostProductiveDay.Date.ToString(CultureInfo.InvariantCulture),
                    ["post_count"] = mostProductiveDay.PostCount,
                }
                : null,
            ["most_loved_publisher"] = mostLovedPublisherClue is not null
                ? new Dictionary<string, object?>
                {
                    ["publisher"] = mostLovedPublisher,
                    ["upvote_counts"] = mostLovedPublisherClue.ReactionCount,
                }
                : null,
            ["most_loved_audience"] = mostLovedAudienceClue is not null
                ? new Dictionary<string, object?>
                {
                    ["account"] = mostLovedAudience,
                    ["upvote_counts"] = mostLovedAudienceClue.ReactionCount,
                }
                : null,
        };

        return new RewindEvent
        {
            ServiceId = "sphere",
            AccountId = request.AccountId,
            Data = GrpcTypeHelper.ConvertObjectToByteString(data)
        };
    }
}