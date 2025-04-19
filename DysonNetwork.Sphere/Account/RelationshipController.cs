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
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var userId = currentUser.Id;

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
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        
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