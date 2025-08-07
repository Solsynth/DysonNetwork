using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Develop.Identity;

[ApiController]
[Route("/api/developers")]
public class DeveloperController(
    AppDatabase db,
    PublisherService.PublisherServiceClient ps,
    ActionLogService.ActionLogServiceClient als,
    DeveloperService ds
)
    : ControllerBase
{
    [HttpGet("{name}")]
    public async Task<ActionResult<Developer>> GetDeveloper(string name)
    {
        var developer = await ds.GetDeveloperByName(name);
        if (developer is null) return NotFound();
        return Ok(developer);
    }

    [HttpGet("{name}/stats")]
    public async Task<ActionResult<DeveloperStats>> GetDeveloperStats(string name)
    {
        var developer = await ds.GetDeveloperByName(name);
        if (developer is null) return NotFound();

        // Get custom apps count
        var customAppsCount = await db.CustomApps
            .Where(a => a.DeveloperId == developer.Id)
            .CountAsync();

        var stats = new DeveloperStats
        {
            TotalCustomApps = customAppsCount
        };

        return Ok(stats);
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<Developer>>> ListJoinedDevelopers()
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);
        
        var pubResponse = await ps.ListPublishersAsync(new ListPublishersRequest { AccountId = currentUser.Id });
        var pubIds = pubResponse.Publishers.Select(p => p.Id).Select(Guid.Parse).ToList();

        var developerQuery = db.Developers
            .Where(d => pubIds.Contains(d.PublisherId))
            .AsQueryable();
        
        var totalCount = await developerQuery.CountAsync(); 
        Response.Headers.Append("X-Total", totalCount.ToString());
        
        var developers = await developerQuery.ToListAsync();

        return Ok(developers);
    }

    [HttpPost("{name}/enroll")]
    [Authorize]
    [RequiredPermission("global", "developers.create")]
    public async Task<ActionResult<Developer>> EnrollDeveloperProgram(string name)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        PublisherInfo? pub;
        try
        {
            var pubResponse = await ps.GetPublisherAsync(new GetPublisherRequest { Name = name });
            pub = PublisherInfo.FromProto(pubResponse.Publisher);
        } catch (RpcException ex)
        {
            return NotFound(ex.Status.Detail);
        }

        // Check if the user is an owner of the publisher
        var permResponse = await ps.IsPublisherMemberAsync(new IsPublisherMemberRequest
        {
            PublisherId = pub.Id.ToString(),
            AccountId = currentUser.Id,
            Role = PublisherMemberRole.Owner
        });
        if (!permResponse.Valid) return StatusCode(403, "You must be the owner of the publisher to join the developer program");

        var hasDeveloper = await db.Developers.AnyAsync(d => d.PublisherId == pub.Id);
        if (hasDeveloper) return BadRequest("Publisher is already in the developer program");
        
        var developer = new Developer
        {
            Id = Guid.NewGuid(),
            PublisherId = pub.Id
        };

        db.Developers.Add(developer);
        await db.SaveChangesAsync();

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = "developers.enroll",
            Meta = 
            { 
                { "publisher_id", Google.Protobuf.WellKnownTypes.Value.ForString(pub.Id.ToString()) },
                { "publisher_name", Google.Protobuf.WellKnownTypes.Value.ForString(pub.Name) }
            },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString()
        });

        return Ok(developer);
    }

    public class DeveloperStats
    {
        public int TotalCustomApps { get; set; }
    }
}