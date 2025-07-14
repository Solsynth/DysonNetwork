using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using NodaTime.Text;

namespace DysonNetwork.Sphere.Activity;

/// <summary>
/// Activity is a universal feed that contains multiple kinds of data. Personalized and generated dynamically.
/// </summary>
[ApiController]
[Route("/api/activities")]
public class ActivityController(
    ActivityService acts
) : ControllerBase
{
    /// <summary>
    /// Listing the activities for the user, users may be logged in or not to use this API.
    /// When the users are not logged in, this API will return the posts that are public.
    /// When the users are logged in,
    /// the API will personalize the user's experience
    /// by ranking up the people they like and the posts they like.
    /// Besides, when users are logged in, it will also mix the other kinds of data and who're plying to them.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<Activity>>> ListActivities(
        [FromQuery] string? cursor,
        [FromQuery] string? filter,
        [FromQuery] int take = 20,
        [FromQuery] string? debugInclude = null
    )
    {
        Instant? cursorTimestamp = null;
        if (!string.IsNullOrEmpty(cursor))
        {
            try
            {
                cursorTimestamp = InstantPattern.ExtendedIso.Parse(cursor).GetValueOrThrow();
            }
            catch
            {
                return BadRequest("Invalid cursor format");
            }
        }

        var debugIncludeSet = debugInclude?.Split(',').ToHashSet() ?? new HashSet<string>();

        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        return currentUserValue is not Account currentUser
            ? Ok(await acts.GetActivitiesForAnyone(take, cursorTimestamp, debugIncludeSet))
            : Ok(await acts.GetActivities(take, cursorTimestamp, currentUser, filter, debugIncludeSet));
    }
}