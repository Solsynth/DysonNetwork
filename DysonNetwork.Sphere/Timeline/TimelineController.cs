using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using NodaTime.Text;

namespace DysonNetwork.Sphere.Timeline;

/// <summary>
/// Activity is a universal feed that contains multiple kinds of data. Personalized and generated dynamically.
/// </summary>
[ApiController]
[Route("/api/timeline")]
public class ActivityController(TimelineService acts) : ControllerBase
{
    /// <summary>
    /// Listing the activities for the user, users may be logged in or not to use this API.
    /// When the users are not logged in, this API will return the posts that are public.
    /// When the users are logged in,
    /// the API will personalize the user's experience
    /// by ranking up the people they like and the posts they like.
    /// Besides, when users are logged in, it will also mix the other kinds of data and who're plying to them.
    /// Fediverse posts are shown to all users (unauthenticated see all, authenticated see posts from followed actors or friends' follows).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<SnTimelinePage>> ListEvents(
        [FromQuery] string? cursor,
        [FromQuery] string? filter,
        [FromQuery] int take = 20,
        [FromQuery] string? mode = null,
        [FromQuery] bool aggressive = true
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

        var timelineMode = ParseTimelineMode(mode);
        if (timelineMode == null)
            return BadRequest("Invalid mode. Expected personalized, top, or latest.");

        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        return currentUserValue is not DyAccount currentUser
            ? Ok(await acts.ListEventsForAnyone(take, cursorTimestamp, timelineMode.Value))
            : Ok(await acts.ListEvents(
                take,
                cursorTimestamp,
                currentUser,
                timelineMode.Value,
                filter,
                aggressive
            ));
    }

    private static SnTimelineMode? ParseTimelineMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
            return SnTimelineMode.Personalized;

        return mode.Trim().ToLowerInvariant() switch
        {
            "personalized" => SnTimelineMode.Personalized,
            "top" => SnTimelineMode.Top,
            "latest" => SnTimelineMode.Latest,
            _ => null,
        };
    }
}
