using DysonNetwork.Shared.Capabilities;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
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
[ApiFeature("timeline", Revision = 1)]
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
        [FromQuery(Name = "pub")] string? pubName,
        [FromQuery] string? collection,
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
                return BadRequest(new ApiError { Code = "TIMELINE_INVALID_CURSOR", Message = "Invalid cursor format", Status = 400 });
            }
        }

        var timelineMode = ParseTimelineMode(mode);
        if (timelineMode == null)
            return BadRequest(new ApiError { Code = "TIMELINE_INVALID_MODE", Message = "Invalid mode. Expected personalized, top, or latest.", Status = 400 });

        TimelineService.TimelineFeedScope scope;
        try
        {
            scope = await acts.ResolveFeedScopeAsync(pubName, collection);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ApiError { Code = "TIMELINE_INVALID_SCOPE", Message = ex.Message, Status = 400 });
        }

        if (scope.RequestedPublisherName is not null && scope.Publisher is null)
            return NotFound(new ApiError { Code = "PUBLISHER_NOT_FOUND", Message = "Publisher not found.", Status = 404 });
        if (scope.RequestedCollectionSlug is not null && scope.Collection is null)
            return NotFound(new ApiError { Code = "COLLECTION_NOT_FOUND", Message = "Collection not found.", Status = 404 });

        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var anonymousViewerKey = $"anon:{HttpContext.Connection.RemoteIpAddress}:{Request.Headers.UserAgent}";
        return currentUserValue is not DyAccount currentUser
            ? Ok(await acts.ListEventsForAnyone(take, cursorTimestamp, timelineMode.Value, anonymousViewerKey, scope))
            : Ok(await acts.ListEvents(
                take,
                cursorTimestamp,
                currentUser,
                timelineMode.Value,
                filter,
                scope,
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
