using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Pass.Account;

[ApiController]
[Route("/api/relationships")]
public class RelationshipController(AppDatabase db, RelationshipService rls) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<SnAccountRelationship>>> ListRelationships([FromQuery] int offset = 0,
        [FromQuery] int take = 20)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        var accountId = currentUser.Id;

        var query = db.AccountRelationships.AsQueryable()
            .OrderByDescending(r => r.CreatedAt)
            .Where(r => r.AccountId == accountId);
        var totalCount = await query.CountAsync();
        var relationships = await query
            .Include(r => r.Related)
            .ThenInclude(a => a.Profile)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        Response.Headers["X-Total"] = totalCount.ToString();

        return relationships;
    }

    [HttpGet("requests")]
    [Authorize]
    public async Task<ActionResult<List<SnAccountRelationship>>> ListRelationshipRequests()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var relationships = await db.AccountRelationships
            .Where(r => r.Status == RelationshipStatus.Pending)
            .Where(r => r.AccountId == currentUser.Id || r.RelatedId == currentUser.Id)
            .Include(r => r.Related)
            .ThenInclude(a => a.Profile)
            .Include(r => r.Account)
            .ThenInclude(a => a.Profile)
            .ToListAsync();

        return relationships;
    }

    public class RelationshipRequest
    {
        [Required] public RelationshipStatus Status { get; set; }
    }

    [HttpPost("{accountId:guid}")]
    [Authorize]
    public async Task<ActionResult<SnAccountRelationship>> CreateRelationship(Guid accountId,
        [FromBody] RelationshipRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var relatedUser = await db.Accounts.FindAsync(accountId);
        if (relatedUser is null) return NotFound("Account was not found.");

        try
        {
            var relationship = await rls.CreateRelationship(
                currentUser, relatedUser, request.Status
            );
            return relationship;
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(err.Message);
        }
    }

    [HttpPatch("{accountId:guid}")]
    [Authorize]
    public async Task<ActionResult<SnAccountRelationship>> UpdateRelationship(Guid accountId,
        [FromBody] RelationshipRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        try
        {
            var relationship = await rls.UpdateRelationship(currentUser.Id, accountId, request.Status);
            return relationship;
        }
        catch (ArgumentException err)
        {
            return NotFound(err.Message);
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(err.Message);
        }
    }

    [HttpDelete("{accountId:guid}")]
    [Authorize]
    public async Task<ActionResult<SnAccountRelationship>> DeleteRelationship(Guid accountId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        try
        {
            var relationship = await rls.DeleteRelationship(currentUser.Id, accountId);
            return Ok(relationship);
        }
        catch (ArgumentException err)
        {
            return BadRequest(err.Message);
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(err.Message);
        }
    }

    [HttpGet("{accountId:guid}")]
    [Authorize]
    public async Task<ActionResult<SnAccountRelationship>> GetRelationship(Guid accountId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
        var queries = db.AccountRelationships.AsQueryable()
            .Where(r => r.AccountId == currentUser.Id && r.RelatedId == accountId)
            .Where(r => r.ExpiredAt == null || r.ExpiredAt > now);
        var relationship = await queries
            .Include(r => r.Related)
            .Include(r => r.Related.Profile)
            .FirstOrDefaultAsync();
        if (relationship is null) return NotFound();

        relationship.Account = currentUser;
        return Ok(relationship);
    }

    [HttpPost("{accountId:guid}/friends")]
    [Authorize]
    public async Task<ActionResult<SnAccountRelationship>> SendFriendRequest(Guid accountId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var relatedUser = await db.Accounts.FindAsync(accountId);
        if (relatedUser is null) return NotFound("Account was not found.");

        var existing = await db.AccountRelationships.FirstOrDefaultAsync(r =>
            (r.AccountId == currentUser.Id && r.RelatedId == accountId) ||
            (r.AccountId == accountId && r.RelatedId == currentUser.Id));
        if (existing != null) return BadRequest("Relationship already exists.");

        try
        {
            var relationship = await rls.SendFriendRequest(currentUser, relatedUser);
            return relationship;
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(err.Message);
        }
    }

    [HttpDelete("{accountId:guid}/friends")]
    [Authorize]
    public async Task<ActionResult> DeleteFriendRequest(Guid accountId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        try
        {
            await rls.DeleteFriendRequest(currentUser.Id, accountId);
            return NoContent();
        }
        catch (ArgumentException err)
        {
            return NotFound(err.Message);
        }
    }

    [HttpPost("{accountId:guid}/friends/accept")]
    [Authorize]
    public async Task<ActionResult<SnAccountRelationship>> AcceptFriendRequest(Guid accountId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var relationship = await rls.GetRelationship(accountId, currentUser.Id, RelationshipStatus.Pending);
        if (relationship is null) return NotFound("Friend request was not found.");

        try
        {
            relationship = await rls.AcceptFriendRelationship(relationship);
            return relationship;
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(err.Message);
        }
    }

    [HttpPost("{accountId:guid}/friends/decline")]
    [Authorize]
    public async Task<ActionResult<SnAccountRelationship>> DeclineFriendRequest(Guid accountId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var relationship = await rls.GetRelationship(accountId, currentUser.Id, RelationshipStatus.Pending);
        if (relationship is null) return NotFound("Friend request was not found.");

        try
        {
            relationship = await rls.AcceptFriendRelationship(relationship, status: RelationshipStatus.Blocked);
            return relationship;
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(err.Message);
        }
    }

    [HttpPost("{accountId:guid}/block")]
    [Authorize]
    public async Task<ActionResult<SnAccountRelationship>> BlockUser(Guid accountId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var relatedUser = await db.Accounts.FindAsync(accountId);
        if (relatedUser is null) return NotFound("Account was not found.");

        try
        {
            var relationship = await rls.BlockAccount(currentUser, relatedUser);
            return relationship;
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(err.Message);
        }
    }

    [HttpDelete("{accountId:guid}/block")]
    [Authorize]
    public async Task<ActionResult<SnAccountRelationship>> UnblockUser(Guid accountId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var relatedUser = await db.Accounts.FindAsync(accountId);
        if (relatedUser is null) return NotFound("Account was not found.");

        try
        {
            var relationship = await rls.UnblockAccount(currentUser, relatedUser);
            return relationship;
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(err.Message);
        }
    }

    public class InspectRelationshipResponse
    {
        public List<SnAccount> Friends { get; set; } = [];
        public List<SnAccount> Blocked { get; set; } = [];
        public List<SnAccount> Pending { get; set; } = [];
    }

    [HttpGet("inspect/{accountId:guid}")]
    [Authorize]
    [AskPermission("relationships.inspect")]
    public async Task<ActionResult<InspectRelationshipResponse>> InspectRelationship(Guid accountId)
    {
        var relationships = await db.AccountRelationships
            .Where(r => r.AccountId == accountId)
            .Include(r => r.Related)
            .GroupBy(r => r.Status, r => r.Related)
            .ToDictionaryAsync(r => r.Key, r => r);

        return Ok(new InspectRelationshipResponse
        {
            Friends = relationships.TryGetValue(RelationshipStatus.Friends, out var friends) ? friends.ToList() : [],
            Blocked = relationships.TryGetValue(RelationshipStatus.Blocked, out var blocked) ? blocked.ToList() : [],
            Pending = relationships.TryGetValue(RelationshipStatus.Pending, out var pending) ? pending.ToList() : []
        });
    }
}