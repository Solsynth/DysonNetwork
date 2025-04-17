using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Build.Framework;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Account;

[ApiController]
[Route("/relationships")]
public class RelationshipController(AppDatabase db, AccountService accounts) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<Relationship>>> ListRelationships([FromQuery] int offset = 0,
        [FromQuery] int take = 20)
    {
        var userIdClaim = User.FindFirst("user_id")?.Value;
        long? userId = long.TryParse(userIdClaim, out var id) ? id : null;
        if (userId is null) return BadRequest("Invalid or missing user_id claim.");

        var totalCount = await db.AccountRelationships
            .CountAsync(r => r.Account.Id == userId);
        var relationships = await db.AccountRelationships
            .Where(r => r.Account.Id == userId)
            .Include(r => r.Related)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        Response.Headers["X-Total"] = totalCount.ToString();

        return relationships;
    }

    public class RelationshipCreateRequest
    {
        [Required] public long UserId { get; set; }
        [Required] public RelationshipStatus Status { get; set; }
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<Relationship>> CreateRelationship([FromBody] RelationshipCreateRequest request)
    {
        var userIdClaim = User.FindFirst("user_id")?.Value;
        long? userId = long.TryParse(userIdClaim, out var id) ? id : null;
        if (userId is null) return BadRequest("Invalid or missing user_id claim.");

        var currentUser = await db.Accounts.FindAsync(userId.Value);
        if (currentUser is null) return BadRequest("Failed to get your current user");
        var relatedUser = await db.Accounts.FindAsync(request.UserId);
        if (relatedUser is null) return BadRequest("Invalid related user");

        try
        {
            var relationship = await accounts.CreateRelationship(
                currentUser, relatedUser, request.Status
            );
            return relationship;
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(err.Message);
        }
    }
}