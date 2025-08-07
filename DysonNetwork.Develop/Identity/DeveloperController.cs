using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NodaTime;

namespace DysonNetwork.Develop.Identity;

[ApiController]
[Route("/api/developers")]
public class DeveloperController(
    AppDatabase db,
    PublisherService.PublisherServiceClient ps,
    ActionLogService.ActionLogServiceClient als
)
    : ControllerBase
{
    [HttpGet("{name}")]
    public async Task<ActionResult<Publisher.Publisher>> GetDeveloper(string name)
    {
        var publisher = await db.Publishers
            .Where(e => e.Name == name)
            .FirstOrDefaultAsync();
        if (publisher is null) return NotFound();

        return Ok(publisher);
    }

    [HttpGet("{name}/stats")]
    public async Task<ActionResult<DeveloperStats>> GetDeveloperStats(string name)
    {
        var publisher = await db.Publishers
            .Where(p => p.Name == name)
            .FirstOrDefaultAsync();
        if (publisher is null) return NotFound();

        // Check if publisher has developer feature
        var now = SystemClock.Instance.GetCurrentInstant();
        var hasDeveloperFeature = await db.PublisherFeatures
            .Where(f => f.PublisherId == publisher.Id)
            .Where(f => f.Flag == PublisherFeatureFlag.Develop)
            .Where(f => f.ExpiredAt == null || f.ExpiredAt > now)
            .AnyAsync();

        if (!hasDeveloperFeature) return NotFound("Not a developer account");

        // Get custom apps count
        var customAppsCount = await db.CustomApps
            .Where(a => a.PublisherId == publisher.Id)
            .CountAsync();

        var stats = new DeveloperStats
        {
            TotalCustomApps = customAppsCount
        };

        return Ok(stats);
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<Publisher.Publisher>>> ListJoinedDevelopers()
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var members = await db.PublisherMembers
            .Where(m => m.AccountId == accountId)
            .Where(m => m.JoinedAt != null)
            .Include(e => e.Publisher)
            .ToListAsync();

        // Filter to only include publishers with the developer feature flag
        var now = SystemClock.Instance.GetCurrentInstant();
        var publisherIds = members.Select(m => m.Publisher.Id).ToList();
        var developerPublisherIds = await db.PublisherFeatures
            .Where(f => publisherIds.Contains(f.PublisherId))
            .Where(f => f.Flag == PublisherFeatureFlag.Develop)
            .Where(f => f.ExpiredAt == null || f.ExpiredAt > now)
            .Select(f => f.PublisherId)
            .ToListAsync();

        return members
            .Where(m => developerPublisherIds.Contains(m.Publisher.Id))
            .Select(m => m.Publisher)
            .ToList();
    }

    [HttpPost("{name}/enroll")]
    [Authorize]
    [RequiredPermission("global", "developers.create")]
    public async Task<ActionResult<Publisher.Publisher>> EnrollDeveloperProgram(string name)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var publisher = await db.Publishers
            .Where(p => p.Name == name)
            .FirstOrDefaultAsync();
        if (publisher is null) return NotFound();

        // Check if the user is an owner of the publisher
        var isOwner = await db.PublisherMembers
            .AnyAsync(m =>
                m.PublisherId == publisher.Id &&
                m.AccountId == accountId &&
                m.Role == PublisherMemberRole.Owner &&
                m.JoinedAt != null);

        if (!isOwner) return StatusCode(403, "You must be the owner of the publisher to join the developer program");

        // Check if already has a developer feature
        var now = SystemClock.Instance.GetCurrentInstant();
        var hasDeveloperFeature = await db.PublisherFeatures
            .AnyAsync(f =>
                f.PublisherId == publisher.Id &&
                f.Flag == PublisherFeatureFlag.Develop &&
                (f.ExpiredAt == null || f.ExpiredAt > now));

        if (hasDeveloperFeature) return BadRequest("Publisher is already in the developer program");

        // Add developer feature flag
        var feature = new PublisherFeature
        {
            PublisherId = publisher.Id,
            Flag = PublisherFeatureFlag.Develop,
            ExpiredAt = null
        };

        db.PublisherFeatures.Add(feature);
        await db.SaveChangesAsync();

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = "developers.enroll",
            Meta = 
            { 
                { "publisher_id", Google.Protobuf.WellKnownTypes.Value.ForString(publisher.Id.ToString()) },
                { "publisher_name", Google.Protobuf.WellKnownTypes.Value.ForString(publisher.Name) }
            },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString()
        });

        return Ok(publisher);
    }

    public class DeveloperStats
    {
        public int TotalCustomApps { get; set; }
    }
}