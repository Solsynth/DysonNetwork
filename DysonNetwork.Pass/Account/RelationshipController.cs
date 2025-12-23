using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Pass.Account;

[ApiController]
[Route("/api/relationships")]
public class RelationshipController(AppDatabase db, RelationshipService rels) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<SnAccountRelationship>>> ListRelationships([FromQuery] int offset = 0,
        [FromQuery] int take = 20)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var query = db.AccountRelationships.AsQueryable()
            .Where(r => r.RelatedId == userId);
        var totalCount = await query.CountAsync();
        var relationships = await query
            .Include(r => r.Related)
            .Include(r => r.Related.Profile)
            .Include(r => r.Account)
            .Include(r => r.Account.Profile)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        var statuses = await db.AccountRelationships
            .Where(r => r.AccountId == userId)
            .ToDictionaryAsync(r => r.RelatedId);
        foreach (var relationship in relationships)
            relationship.Status = statuses.TryGetValue(relationship.AccountId, out var status)
                ? status.Status
                : RelationshipStatus.Pending;

        Response.Headers["X-Total"] = totalCount.ToString();

        return relationships;
    }

    [HttpGet("requests")]
    [Authorize]
    public async Task<ActionResult<List<SnAccountRelationship>>> ListSentRequests()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var relationships = await db.AccountRelationships
            .Where(r => r.AccountId == currentUser.Id && r.Status == RelationshipStatus.Pending)
            .Include(r => r.Related)
            .Include(r => r.Related.Profile)
            .Include(r => r.Account)
            .Include(r => r.Account.Profile)
            .ToListAsync();

        return relationships;
    }

    public class RelationshipRequest
    {
        [Required] public RelationshipStatus Status { get; set; }
    }

    [HttpPost("{userId:guid}")]
    [Authorize]
    public async Task<ActionResult<SnAccountRelationship>> CreateRelationship(Guid userId,
        [FromBody] RelationshipRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var relatedUser = await db.Accounts.FindAsync(userId);
        if (relatedUser is null) return NotFound("Account was not found.");

        try
        {
            var relationship = await rels.CreateRelationship(
                currentUser, relatedUser, request.Status
            );
            return relationship;
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(err.Message);
        }
    }

    [HttpPatch("{userId:guid}")]
    [Authorize]
    public async Task<ActionResult<SnAccountRelationship>> UpdateRelationship(Guid userId,
        [FromBody] RelationshipRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        try
        {
            var relationship = await rels.UpdateRelationship(currentUser.Id, userId, request.Status);
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

    [HttpGet("{userId:guid}")]
    [Authorize]
    public async Task<ActionResult<SnAccountRelationship>> GetRelationship(Guid userId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
        var queries = db.AccountRelationships.AsQueryable()
            .Where(r => r.AccountId == currentUser.Id && r.RelatedId == userId)
            .Where(r => r.ExpiredAt == null || r.ExpiredAt > now);
        var relationship = await queries
            .Include(r => r.Related)
            .Include(r => r.Related.Profile)
            .FirstOrDefaultAsync();
        if (relationship is null) return NotFound();

        relationship.Account = currentUser;
        return Ok(relationship);
    }

    [HttpPost("{userId:guid}/friends")]
    [Authorize]
    public async Task<ActionResult<SnAccountRelationship>> SendFriendRequest(Guid userId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var relatedUser = await db.Accounts.FindAsync(userId);
        if (relatedUser is null) return NotFound("Account was not found.");

        var existing = await db.AccountRelationships.FirstOrDefaultAsync(r =>
            (r.AccountId == currentUser.Id && r.RelatedId == userId) ||
            (r.AccountId == userId && r.RelatedId == currentUser.Id));
        if (existing != null) return BadRequest("Relationship already exists.");

        try
        {
            var relationship = await rels.SendFriendRequest(currentUser, relatedUser);
            return relationship;
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(err.Message);
        }
    }

    [HttpDelete("{userId:guid}/friends")]
    [Authorize]
    public async Task<ActionResult> DeleteFriendRequest(Guid userId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        try
        {
            await rels.DeleteFriendRequest(currentUser.Id, userId);
            return NoContent();
        }
        catch (ArgumentException err)
        {
            return NotFound(err.Message);
        }
    }

    [HttpPost("{userId:guid}/friends/accept")]
    [Authorize]
    public async Task<ActionResult<SnAccountRelationship>> AcceptFriendRequest(Guid userId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var relationship = await rels.GetRelationship(userId, currentUser.Id, RelationshipStatus.Pending);
        if (relationship is null) return NotFound("Friend request was not found.");

        try
        {
            relationship = await rels.AcceptFriendRelationship(relationship);
            return relationship;
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(err.Message);
        }
    }

    [HttpPost("{userId:guid}/friends/decline")]
    [Authorize]
    public async Task<ActionResult<SnAccountRelationship>> DeclineFriendRequest(Guid userId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var relationship = await rels.GetRelationship(userId, currentUser.Id, RelationshipStatus.Pending);
        if (relationship is null) return NotFound("Friend request was not found.");

        try
        {
            relationship = await rels.AcceptFriendRelationship(relationship, status: RelationshipStatus.Blocked);
            return relationship;
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(err.Message);
        }
    }

    [HttpPost("{userId:guid}/block")]
    [Authorize]
    public async Task<ActionResult<SnAccountRelationship>> BlockUser(Guid userId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var relatedUser = await db.Accounts.FindAsync(userId);
        if (relatedUser is null) return NotFound("Account was not found.");

        try
        {
            var relationship = await rels.BlockAccount(currentUser, relatedUser);
            return relationship;
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(err.Message);
        }
    }
    
    [HttpDelete("{userId:guid}/block")]
    [Authorize]
    public async Task<ActionResult<SnAccountRelationship>> UnblockUser(Guid userId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var relatedUser = await db.Accounts.FindAsync(userId);
        if (relatedUser is null) return NotFound("Account was not found.");

        try
        {
            var relationship = await rels.UnblockAccount(currentUser, relatedUser);
            return relationship;
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(err.Message);
        }
    }
}