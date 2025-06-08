using DysonNetwork.Sphere.Account;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Activity;

/// <summary>
/// Activity is a universal feed that contains multiple kinds of data. Personalized and generated dynamically.
/// </summary>
[ApiController]
[Route("/activities")]
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
    public async Task<ActionResult<List<Activity>>> ListActivities([FromQuery] int? cursor, [FromQuery] int take = 20)
    {
        var cursorTimestamp = cursor is <= 1000
            ? SystemClock.Instance.GetCurrentInstant()
            : Instant.FromUnixTimeMilliseconds(cursor!.Value);

        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        if (currentUserValue is not Account.Account currentUser)
            return Ok(await acts.GetActivitiesForAnyone(take, cursorTimestamp));

        return Ok(await acts.GetActivities(take, cursorTimestamp, currentUser));
    }
}