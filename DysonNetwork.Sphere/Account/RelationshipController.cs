using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Build.Framework;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Account;

[ApiController]
[Route("/relationships")]
public class RelationshipController(AppDatabase db, RelationshipService rels) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<Relationship>>> ListRelationships([FromQuery] int offset = 0,
        [FromQuery] int take = 20)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
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

        Response.Headers["X-Total"] = totalCount.ToString();

        return relationships;
    }
    
    [HttpGet("requests")]
    [Authorize]
    public async Task<ActionResult<List<Relationship>>> ListSentRequests()
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
    
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
    public async Task<ActionResult<Relationship>> CreateRelationship(Guid userId, [FromBody] RelationshipRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

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
    public async Task<ActionResult<Relationship>> UpdateRelationship(Guid userId, [FromBody] RelationshipRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
    
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
    
    [HttpPost("{userId:guid}/friends")]
    [Authorize]
    public async Task<ActionResult<Relationship>> SendFriendRequest(Guid userId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var relatedUser = await db.Accounts.FindAsync(userId);
        if (relatedUser is null) return NotFound("Account was not found.");

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

    [HttpPost("{userId:guid}/friends/accept")]
    [Authorize]
    public async Task<ActionResult<Relationship>> AcceptFriendRequest(Guid userId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

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
    public async Task<ActionResult<Relationship>> DeclineFriendRequest(Guid userId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

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
    public async Task<ActionResult<Relationship>> BlockUser(Guid userId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
    
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
}