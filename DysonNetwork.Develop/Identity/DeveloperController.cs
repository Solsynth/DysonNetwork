using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Extensions;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Develop.Identity;

[ApiController]
[Route("/api/developers")]
public class DeveloperController(
    AppDatabase db,
    DyPublisherService.DyPublisherServiceClient ps,
    RemoteActionLogService als,
    DeveloperService ds
)
    : ControllerBase
{
    [HttpGet("{name}")]
    public async Task<ActionResult<SnDeveloper>> GetDeveloper(string name)
    {
        var developer = await ds.GetDeveloperByName(name);
        if (developer is null) return NotFound(new ApiError { Code = "DEV_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });
        return Ok(await ds.LoadDeveloperPublisher(developer));
    }

    [HttpGet("{name}/stats")]
    public async Task<ActionResult<DeveloperStats>> GetDeveloperStats(string name)
    {
        var developer = await ds.GetDeveloperByName(name);
        if (developer is null) return NotFound(new ApiError { Code = "DEV_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });

        // Get custom apps count
        var customAppsCount = await db.CustomApps
            .Include(a => a.Project)
            .Where(a => a.Project.DeveloperId == developer.Id)
            .CountAsync();

        var stats = new DeveloperStats
        {
            TotalCustomApps = customAppsCount
        };

        return Ok(stats);
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<SnDeveloper>>> ListJoinedDevelopers()
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var pubResponse = await ps.ListPublishersAsync(new DyListPublishersRequest { AccountId = currentUser.Id });
        var pubIds = pubResponse.Publishers.Select(p => p.Id).Select(Guid.Parse).ToList();

        var developerQuery = db.Developers
            .Where(d => pubIds.Contains(d.PublisherId))
            .AsQueryable();
        
        var totalCount = await developerQuery.CountAsync(); 
        Response.Headers.Append("X-Total", totalCount.ToString());
        
        var developers = await developerQuery.ToListAsync();

        return Ok(await ds.LoadDeveloperPublisher(developers));
    }

    [HttpPost("{name}/enroll")]
    [Authorize]
    [AskPermission("developers.create")]
    public async Task<ActionResult<SnDeveloper>> EnrollDeveloperProgram(string name)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });
        var accountId = Guid.Parse(currentUser.Id);

        SnPublisher? pub;
        try
        {
            var pubResponse = await ps.GetPublisherAsync(new DyGetPublisherRequest { Name = name });
            pub = SnPublisher.FromProtoValue(pubResponse.Publisher);
        } catch (RpcException ex)
        {
            return NotFound(new ApiError { Code = "DEV_DEVELOPER_PUBLISHER_NOT_FOUND", Message = ex.Status.Detail, Status = 404 });
        }

        // Check if the user is an owner of the publisher
        var permResponse = await ps.IsPublisherMemberAsync(new DyIsPublisherMemberRequest
        {
            PublisherId = pub.Id.ToString(),
            AccountId = currentUser.Id,
            Role = DyPublisherMemberRole.DyOwner
        });
        if (!permResponse.Valid) return StatusCode(403, ApiError.Unauthorized("You must be the owner of the publisher to join the developer program", forbidden: true));

        var hasDeveloper = await db.Developers.AnyAsync(d => d.PublisherId == pub.Id);
        if (hasDeveloper) return BadRequest(new ApiError { Code = "DEV_DEVELOPER_ALREADY_ENROLLED", Message = "Publisher is already in the developer program", Status = 400 });
        
        var developer = new SnDeveloper
        {
            Id = Guid.NewGuid(),
            PublisherId = pub.Id
        };

        db.Developers.Add(developer);
        await db.SaveChangesAsync();

        als.CreateActionLog(
            accountId,
            "developers.enroll",
            new Dictionary<string, object>
            {
                { "publisher_id", pub.Id.ToString() },
                { "publisher_name", pub.Name }
            },
            userAgent: Request.Headers.UserAgent,
            ipAddress: Request.GetClientIpAddress()
        );

        return Ok(await ds.LoadDeveloperPublisher(developer));
    }

    public class DeveloperStats
    {
        public int TotalCustomApps { get; set; }
    }
}