using System.Globalization;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Grpc.Core;
using JiebaNet.Segmenter;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using PostReactionAttitude = DysonNetwork.Shared.Models.PostReactionAttitude;

namespace DysonNetwork.Sphere.Rewind;

public class SphereRewindServiceGrpc(
    AppDatabase db,
    RemoteAccountService remoteAccounts,
    Publisher.PublisherService ps
) : DyRewindService.DyRewindServiceBase
{
    public override async Task<DyRewindEvent> GetRewindEvent(
        DyRequestRewindEvent request,
        ServerCallContext context
    )
    {
        var accountId = Guid.Parse(request.AccountId);
        var year = request.Year;

        var startDate = new LocalDate(year - 1, 12, 26).AtMidnight().InUtc().ToInstant();
        var endDate = new LocalDate(year, 12, 26).AtMidnight().InUtc().ToInstant();

        // Audience data
        var mostLovedPublisherClue = await db
            .PostReactions.Where(a => a.CreatedAt >= startDate && a.CreatedAt < endDate)
            .Where(p => p.AccountId == accountId && p.Attitude == PostReactionAttitude.Positive)
            .Where(p => p.Post.PublisherId.HasValue)
            .GroupBy(p => p.Post.PublisherId!.Value)
            .OrderByDescending(g => g.Count())
            .Select(g => new { PublisherId = g.Key, ReactionCount = g.Count() })
            .FirstOrDefaultAsync();
        var mostLovedPublisher = mostLovedPublisherClue is not null
            ? await ps.GetPublisherLoaded(mostLovedPublisherClue.PublisherId)
            : null;

        // Creator data
        var publishers = await db
            .PublisherMembers.Where(pm => pm.AccountId == accountId)
            .Select(pm => pm.PublisherId)
            .ToListAsync();

        var mostLovedAudienceClue = await db
            .PostReactions.Where(a => a.CreatedAt >= startDate && a.CreatedAt < endDate)
            .Where(pr =>
                pr.Attitude == PostReactionAttitude.Positive
                && pr.AccountId.HasValue
                && pr.Post.PublisherId.HasValue
                && publishers.Contains(pr.Post.PublisherId.Value)
            )
            .GroupBy(pr => pr.AccountId!.Value)
            .OrderByDescending(g => g.Count())
            .Select(g => new { AccountId = g.Key, ReactionCount = g.Count() })
            .FirstOrDefaultAsync();
        var mostLovedAudience = mostLovedAudienceClue is not null
            ? await remoteAccounts.GetAccount(mostLovedAudienceClue.AccountId)
            : null;

        var posts = db
            .Posts.Where(a => a.CreatedAt >= startDate && a.CreatedAt < endDate)
            .Where(p => p.PublisherId.HasValue && publishers.Contains(p.PublisherId.Value))
            .AsQueryable();
        var postTotalCount = await posts.CountAsync();
        var postTotalUpvotes = await db
            .PostReactions.Where(a => a.CreatedAt >= startDate && a.CreatedAt < endDate)
            .Where(p => p.Post.PublisherId.HasValue && publishers.Contains(p.Post.PublisherId.Value))
            .Where(r => r.Attitude == PostReactionAttitude.Positive)
            .CountAsync();
        var mostPopularPost = await posts
            .OrderByDescending(p => p.Upvotes - p.Downvotes)
            .FirstOrDefaultAsync();
        var mostProductiveDay = (await posts.Select(p => new { p.CreatedAt }).ToListAsync())
            .GroupBy(p => p.CreatedAt.ToDateTimeUtc().Date)
            .OrderByDescending(g => g.Count())
            .Select(g => new { Date = g.Key, PostCount = g.Count() })
            .FirstOrDefault();
        // Contents to create word cloud
        ConfigManager.ConfigFileBaseDir = @"/app/Resources/cndicts";
        var postContents = await posts
            .Where(p => p.Content != null)
            .Select(p => p.Content)
            .OrderByDescending(p => p!.Length)
            .Take(1000)
            .ToListAsync();
        var segmenter = new JiebaSegmenter();
        var words = segmenter.CutForSearchInParallel(postContents);
        var allWords = words.SelectMany(w => w)
            .Where(word => !word.All(c => char.IsPunctuation(c) || char.IsWhiteSpace(c)));
        var topWords = allWords
            .GroupBy(w => w)
            .Select(g => new { Word = g.Key, Count = g.Count() })
            .OrderByDescending(wc => wc.Count)
            .Take(100)
            .ToList();

        var data = new Dictionary<string, object?>
        {
            ["total_post_count"] = postTotalCount,
            ["total_upvote_count"] = postTotalUpvotes,
            ["top_words"] = topWords
                .Select(wc => new Dictionary<string, object?>
                {
                    ["word"] = wc.Word,
                    ["count"] = wc.Count,
                })
                .ToList(),
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

        return new DyRewindEvent
        {
            ServiceId = "sphere",
            AccountId = request.AccountId,
            Data = InfraObjectCoder.ConvertObjectToByteString(data, withoutIgnore: true),
        };
    }
}