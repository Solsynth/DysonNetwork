using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Sphere.Publisher;

[ApiController]
[Route("/api/publishers")]
public class PublisherPublicController(AppDatabase db, RemoteAccountService accounts, PublisherService ps, PublisherRatingService ratingService, PublisherLeaderboardService leaderboardService) : ControllerBase
{
    [HttpGet("search")]
    public async Task<ActionResult<List<SnPublisher>>> SearchPublishers([FromQuery] string query, [FromQuery] int take = 20)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Ok(new List<SnPublisher>());
        
        // Use PublisherService to load individual publisher accounts efficiently
        var publishers = await db.Publishers
            .Where(a => EF.Functions.ILike(a.Name, $"%{query}%") ||
                        EF.Functions.ILike(a.Nick, $"%{query}%") ||
                        (a.Bio != null && EF.Functions.ILike(a.Bio, $"%{query}%")))
            .Take(take)
            .ToListAsync();
        
        // Load individual publisher accounts in batch to avoid N+1 queries
        var publishersWithAccounts = await ps.LoadIndividualPublisherAccounts(publishers);
        
        return Ok(publishersWithAccounts);
    }

    [HttpGet("{name}")]
    public async Task<ActionResult<SnPublisher>> GetPublisher(string name)
    {
        var publisher = await db.Publishers.Where(e => e.Name == name).FirstOrDefaultAsync();
        if (publisher is null)
            return NotFound();
        if (publisher.AccountId is null)
            return Ok(publisher);

        publisher.Account = SnAccount.FromProtoValue(await accounts.GetAccount(publisher.AccountId.Value));

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
        var publisher = await db.Publishers.Where(e => e.Name == name).FirstOrDefaultAsync();
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
        var publisher = await db.Publishers.Where(e => e.Name == name).FirstOrDefaultAsync();
        if (publisher is null)
            return NotFound();

        var total = await ratingService.GetRatingHistoryCount(publisher.Id);
        HttpContext.Response.Headers["X-Total"] = total.ToString();

        var records = await ratingService.GetRatingHistory(publisher.Id, take, offset);
        return Ok(records);
    }

    [HttpGet("leaderboard")]
    public async Task<ActionResult<List<PublisherLeaderboardService.LeaderboardEntry>>> GetLeaderboard(
        [FromQuery] int take = 20,
        [FromQuery] int offset = 0
    )
    {
        var total = await leaderboardService.GetTotalPublishers();
        HttpContext.Response.Headers["X-Total"] = total.ToString();

        var entries = await leaderboardService.GetLeaderboard(take, offset);
        return Ok(entries);
    }

    [HttpGet("{name}/rating/overview")]
    public async Task<ActionResult<PublisherLeaderboardService.RatingOverview>> GetRatingOverview(string name)
    {
        var overview = await leaderboardService.GetOverviewByName(name);
        if (overview is null)
            return NotFound();

        return Ok(overview);
    }
}
