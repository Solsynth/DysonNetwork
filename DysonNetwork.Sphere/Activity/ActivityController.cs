using DysonNetwork.Sphere.Account;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Activity;

[ApiController]
[Route("/activities")]
public class ActivityController(
    AppDatabase db,
    ActivityReaderService reader,
    RelationshipService rels) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<Activity>>> ListActivities([FromQuery] int offset, [FromQuery] int take = 20)
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as Account.Account;
        var userFriends = currentUser is null ? [] : await rels.ListAccountFriends(currentUser);

        var totalCount = await db.Activities
            .FilterWithVisibility(currentUser, userFriends)
            .CountAsync();
        var activities = await db.Activities
            .Include(e => e.Account)
            .Include(e => e.Account.Profile)
            .FilterWithVisibility(currentUser, userFriends)
            .OrderByDescending(e => e.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();
        activities = await reader.LoadActivityData(activities, currentUser, userFriends);

        Response.Headers["X-Total"] = totalCount.ToString();

        return Ok(activities);
    }
}