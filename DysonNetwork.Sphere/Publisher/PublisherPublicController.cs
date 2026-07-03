using DysonNetwork.Shared.Models;
using DysonNetwork.Sphere.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Publisher;

[ApiController]
[Route("/api/publishers")]
public class PublisherPublicController(
    AppDatabase db,
    RemoteAccountService accounts,
    PublisherService ps,
    PublisherRatingService ratingService,
    PublisherLeaderboardService leaderboardService
) : ControllerBase
{
    private sealed record PublisherSearchContext(string Query, bool UseFuzzyMatch);

    private static PublisherSearchContext? CreateSearchContext(string? query)
    {
        var normalized = query?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : new PublisherSearchContext(normalized, normalized.Length >= 3);
    }

    private static IQueryable<SnPublisher> ApplyPublisherSearch(
        IQueryable<SnPublisher> query,
        PublisherSearchContext? searchContext
    )
    {
        if (searchContext is null)
            return query;

        var searchPattern = $"%{searchContext.Query}%";
        if (!searchContext.UseFuzzyMatch)
        {
            return query.Where(a =>
                EF.Functions.ILike(a.Name, searchPattern)
                || EF.Functions.ILike(a.Nick, searchPattern)
                || (a.Bio != null && EF.Functions.ILike(a.Bio, searchPattern))
            );
        }

        return query.Where(a =>
            EF.Functions.ILike(a.Name, searchPattern)
            || EF.Functions.ILike(a.Nick, searchPattern)
            || (a.Bio != null && EF.Functions.ILike(a.Bio, searchPattern))
            || EF.Functions.TrigramsAreSimilar(a.Name, searchContext.Query)
            || EF.Functions.TrigramsAreSimilar(a.Nick, searchContext.Query)
            || (a.Bio != null && EF.Functions.TrigramsAreWordSimilar(searchContext.Query, a.Bio))
        );
    }

    private static IQueryable<SnPublisher> ApplyPublisherSearchOrdering(
        IQueryable<SnPublisher> query,
        PublisherSearchContext? searchContext
    )
    {
        if (searchContext is not { UseFuzzyMatch: true })
            return query.OrderBy(a => a.Name);

        var searchPattern = $"%{searchContext.Query}%";
        return query
            .OrderByDescending(a =>
                EF.Functions.ILike(a.Name, searchPattern)
                || EF.Functions.ILike(a.Nick, searchPattern)
                || (a.Bio != null && EF.Functions.ILike(a.Bio, searchPattern))
            )
            .ThenByDescending(a => EF.Functions.TrigramsSimilarity(a.Name, searchContext.Query))
            .ThenByDescending(a => EF.Functions.TrigramsSimilarity(a.Nick, searchContext.Query))
            .ThenByDescending(a => a.Bio != null ? EF.Functions.TrigramsWordSimilarity(searchContext.Query, a.Bio) : 0.0f)
            .ThenBy(a => a.Name);
    }

    [HttpGet("search")]
    public async Task<ActionResult<List<SnPublisher>>> SearchPublishers(
        [FromQuery] string query,
        [FromQuery] int take = 20
    )
    {
        var searchContext = CreateSearchContext(query);
        if (searchContext is null)
            return Ok(new List<SnPublisher>());

        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as DyAccount;

        var publishersQueryable = ApplyPublisherSearch(db.Publishers, searchContext);

        if (currentUser is not null)
        {
            var blockedIds = await accounts.ListAllBlockedAccountIds(Guid.Parse(currentUser.Id));
            var mutedIds = await accounts.ListMutedAccountIds(Guid.Parse(currentUser.Id));
            var hiddenIds = blockedIds.Concat(mutedIds).ToHashSet();
            if (hiddenIds.Count > 0)
                publishersQueryable = publishersQueryable.Where(p => p.AccountId == null || !hiddenIds.Contains(p.AccountId.Value));
        }

        var publishers = await ApplyPublisherSearchOrdering(
                publishersQueryable,
                searchContext
            )
            .Take(take)
            .ToListAsync();

        var publishersWithAccounts = await ps.LoadIndividualPublisherAccounts(publishers);

        return Ok(publishersWithAccounts);
    }

    [HttpGet("{name}")]
    public async Task<ActionResult<SnPublisher>> GetPublisher(string name)
    {
        var publisher = await db.Publishers.Where(e => e.Name.ToLower() == name.ToLowerInvariant()).FirstOrDefaultAsync();
        if (publisher is null)
            return NotFound();

        var data = await ps.HydratePublisherRealm([publisher]);
        publisher = data.First();

        if (publisher.AccountId is not null)
        {
            publisher.Account = SnAccount.FromProtoValue(
                await accounts.GetAccount(publisher.AccountId.Value)
            );
        }

        return Ok(publisher);
    }

    [HttpGet("{name}/heatmap")]
    public async Task<ActionResult<ActivityHeatmap>> GetPublisherHeatmap(string name)
    {
        var heatmap = await ps.GetPublisherHeatmap(name);
        if (heatmap is null)
            return NotFound();
        return Ok(heatmap);
    }

    [HttpGet("{name}/stats")]
    public async Task<ActionResult<PublisherService.PublisherStats>> GetPublisherStats(string name)
    {
        var stats = await ps.GetPublisherStats(name);
        if (stats is null)
            return NotFound();
        return Ok(stats);
    }

    [HttpGet("of/{accountId:guid}")]
    public async Task<ActionResult<List<SnPublisher>>> GetAccountManagedPublishers(Guid accountId)
    {
        var members = await db
            .PublisherMembers.Where(m => m.AccountId == accountId)
            .Where(m => m.JoinedAt != null)
            .Include(e => e.Publisher)
            .ToListAsync();

        return members.Select(m => m.Publisher).ToList();
    }

    [HttpGet("{name}/rating")]
    public async Task<ActionResult<double>> GetPublisherRating(string name)
    {
        var publisher = await db.Publishers.Where(e => e.Name.ToLower() == name.ToLowerInvariant()).FirstOrDefaultAsync();
        if (publisher is null)
            return NotFound();

        var rating = await ratingService.GetRating(publisher.Id);
        return Ok(rating);
    }

    [HttpGet("{name}/rating/history")]
    public async Task<ActionResult<List<SnPublisherRatingRecord>>> GetPublisherRatingHistory(
        string name,
        [FromQuery] int take = 20,
        [FromQuery] int offset = 0
    )
    {
        var publisher = await db.Publishers.Where(e => e.Name.ToLower() == name.ToLowerInvariant()).FirstOrDefaultAsync();
        if (publisher is null)
            return NotFound();

        var total = await ratingService.GetRatingHistoryCount(publisher.Id);
        HttpContext.Response.Headers["X-Total"] = total.ToString();

        var records = await ratingService.GetRatingHistory(publisher.Id, take, offset);
        return Ok(records);
    }

    [HttpGet("leaderboard")]
    public async Task<
        ActionResult<List<PublisherLeaderboardService.LeaderboardEntry>>
    > GetLeaderboard([FromQuery] int take = 20, [FromQuery] int offset = 0)
    {
        var total = await leaderboardService.GetTotalPublishers();
        HttpContext.Response.Headers["X-Total"] = total.ToString();

        var entries = await leaderboardService.GetLeaderboard(take, offset);
        return Ok(entries);
    }

    [HttpGet("{name}/rating/overview")]
    public async Task<ActionResult<PublisherLeaderboardService.RatingOverview>> GetRatingOverview(
        string name
    )
    {
        var overview = await leaderboardService.GetOverviewByName(name);
        if (overview is null)
            return NotFound();

        return Ok(overview);
    }
}
