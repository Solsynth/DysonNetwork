using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Sphere.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Sphere.Timeline;

[ApiController]
[Route("/api/timeline/discovery")]
[Authorize]
public class TimelineDiscoveryController(TimelineService timeline) : ControllerBase
{
    [HttpGet("profile")]
    public async Task<ActionResult<SnDiscoveryProfile>> GetProfile()
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        return Ok(await timeline.GetDiscoveryProfile(currentUser));
    }

    [HttpPost("uninterested")]
    [AskPermission(PermissionKeys.TimelinesFeedback)]
    public async Task<ActionResult<SnDiscoveryPreference>> MarkUninterested(
        [FromBody] DiscoveryPreferenceRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var kind = ParseKind(request.Kind);
        if (kind == null)
            return BadRequest(new ApiError { Code = "DISCOVERY_INVALID_KIND", Message = "Invalid kind. Expected publisher, realm, or account.", Status = 400 });

        var preference = await timeline.MarkDiscoveryPreferenceAsync(
            currentUser,
            kind.Value,
            request.ReferenceId,
            request.Reason
        );
        return Ok(preference);
    }

    [HttpDelete("uninterested")]
    [AskPermission(PermissionKeys.TimelinesFeedback)]
    public async Task<ActionResult> RemoveUninterested(
        [FromQuery] string kind,
        [FromQuery] Guid referenceId
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var parsedKind = ParseKind(kind);
        if (parsedKind == null)
            return BadRequest(new ApiError { Code = "DISCOVERY_INVALID_KIND", Message = "Invalid kind. Expected publisher, realm, or account.", Status = 400 });

        var removed = await timeline.RemoveDiscoveryPreferenceAsync(
            currentUser,
            parsedKind.Value,
            referenceId
        );
        return removed ? Ok() : NotFound();
    }

    [HttpPost("feedback")]
    [AskPermission(PermissionKeys.TimelinesFeedback)]
    public async Task<ActionResult<RecommendationFeedbackResult>> LeaveFeedback(
        [FromBody] RecommendationFeedbackRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var feedback = ParseFeedback(request.Feedback);
        if (feedback == null)
            return BadRequest(new ApiError { Code = "DISCOVERY_INVALID_FEEDBACK", Message = "Invalid feedback. Expected positive or negative.", Status = 400 });

        if (!IsFeedbackKindSupported(request.Kind))
            return BadRequest(new ApiError { Code = "DISCOVERY_INVALID_KIND", Message = "Invalid kind. Expected post, publisher, collection, tag, or category.", Status = 400 });

        var result = await timeline.ApplyRecommendationFeedbackAsync(
            currentUser,
            request.Kind,
            request.ReferenceId,
            feedback.Value,
            request.Reason,
            request.Suppress
        );
        if (result == null)
            return NotFound(new ApiError { Code = "DISCOVERY_CONTENT_NOT_FOUND", Message = "Referenced content was not found.", Status = 404 });

        return Ok(result);
    }

    [HttpPut("weights")]
    [AskPermission(PermissionKeys.TimelinesWeightsManage)]
    public async Task<ActionResult<SnPostInterestProfile>> AdjustWeight(
        [FromBody] RecommendationWeightChangeRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var kind = ParseInterestKind(request.Kind);
        if (kind == null)
            return BadRequest(new ApiError { Code = "DISCOVERY_INVALID_KIND", Message = "Invalid kind. Expected publisher, collection, tag, or category.", Status = 400 });

        var profile = await timeline.AdjustRecommendationWeightAsync(
            currentUser,
            kind.Value,
            request.ReferenceId,
            request.ScoreDelta,
            request.InteractionCount,
            request.SignalType
        );
        return Ok(profile);
    }

    [HttpPost("reset")]
    [AskPermission(PermissionKeys.TimelinesReset)]
    public async Task<ActionResult<int>> ResetInterests()
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var count = await timeline.ResetInterestProfileAsync(currentUser);
        return Ok(count);
    }

    private static DiscoveryTargetKind? ParseKind(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
            return null;

        return kind.Trim().ToLowerInvariant() switch
        {
            "publisher" => DiscoveryTargetKind.Publisher,
            "realm" => DiscoveryTargetKind.Realm,
            "account" => DiscoveryTargetKind.Account,
            _ => null,
        };
    }

    private static bool IsFeedbackKindSupported(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
            return false;

        return kind.Trim().ToLowerInvariant() is "post" or "publisher" or "collection" or "tag" or "category";
    }

    private static RecommendationFeedbackValue? ParseFeedback(string? feedback)
    {
        if (string.IsNullOrWhiteSpace(feedback))
            return null;

        return feedback.Trim().ToLowerInvariant() switch
        {
            "positive" or "good" or "like" => RecommendationFeedbackValue.Positive,
            "negative" or "bad" or "dislike" => RecommendationFeedbackValue.Negative,
            _ => null,
        };
    }

    private static PostInterestKind? ParseInterestKind(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
            return null;

        return kind.Trim().ToLowerInvariant() switch
        {
            "publisher" => PostInterestKind.Publisher,
            "collection" => PostInterestKind.Collection,
            "tag" => PostInterestKind.Tag,
            "category" => PostInterestKind.Category,
            _ => null,
        };
    }
}
