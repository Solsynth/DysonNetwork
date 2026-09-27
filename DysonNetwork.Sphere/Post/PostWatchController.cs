using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Capabilities;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Sphere.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Sphere.Post;

[ApiController]
[Route("/api/posts/watch")]
[ApiFeature("posts.watch", Revision = 1)]
public class PostWatchController(PostWatchService watches) : ControllerBase
{
    public class PostWatchFiltersRequest
    {
        public bool? Reactions { get; set; }
        public bool? Replies { get; set; }
        public bool? Chains { get; set; }
        public bool? Forwards { get; set; }
        public bool? Edits { get; set; }
    }

    public class PostWatchPreferencesRequest
    {
        public PostWatchFiltersRequest? Bookmark { get; set; }
        public PostWatchFiltersRequest? Reaction { get; set; }
        public PostWatchFiltersRequest? Reply { get; set; }
    }

    public class PostWatchPreferenceResponse
    {
        public required string Source { get; set; }
        public bool NotifyReactions { get; set; }
        public bool NotifyReplies { get; set; }
        public bool NotifyChains { get; set; }
        public bool NotifyForwards { get; set; }
        public bool NotifyEdits { get; set; }
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<PostWatchPreferenceResponse>>> GetPostWatchPreferences()
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var preferences = await watches.GetEffectivePreferencesAsync(Guid.Parse(currentUser.Id));

        return Ok(preferences.Select(ToResponse).ToList());
    }

    [HttpPut]
    [AskPermission(PermissionKeys.PostSubscriptionsManage)]
    [Authorize]
    public async Task<ActionResult<List<PostWatchPreferenceResponse>>> UpdatePostWatchPreferences(
        [FromBody] PostWatchPreferencesRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var updates = new List<PostWatchPreferenceUpdate>();
        if (request.Bookmark is not null)
            updates.Add(ToUpdate(PostWatchSource.Bookmark, request.Bookmark));
        if (request.Reaction is not null)
            updates.Add(ToUpdate(PostWatchSource.Reaction, request.Reaction));
        if (request.Reply is not null)
            updates.Add(ToUpdate(PostWatchSource.Reply, request.Reply));

        var preferences = await watches.UpdatePreferencesAsync(Guid.Parse(currentUser.Id), updates);

        return Ok(preferences.Select(ToResponse).ToList());
    }

    private static PostWatchPreferenceUpdate ToUpdate(
        PostWatchSource source,
        PostWatchFiltersRequest filters
    ) =>
        new(
            source,
            filters.Reactions,
            filters.Replies,
            filters.Chains,
            filters.Forwards,
            filters.Edits
        );

    private static PostWatchPreferenceResponse ToResponse(SnPostWatchPreference preference) =>
        new()
        {
            Source = preference.Source.ToString().ToLowerInvariant(),
            NotifyReactions = preference.NotifyReactions,
            NotifyReplies = preference.NotifyReplies,
            NotifyChains = preference.NotifyChains,
            NotifyForwards = preference.NotifyForwards,
            NotifyEdits = preference.NotifyEdits,
        };
}
