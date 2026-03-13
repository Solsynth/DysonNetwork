using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
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
            return Unauthorized();

        return Ok(await timeline.GetDiscoveryProfile(currentUser));
    }

    [HttpPost("uninterested")]
    public async Task<ActionResult<SnDiscoveryPreference>> MarkUninterested(
        [FromBody] DiscoveryPreferenceRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var kind = ParseKind(request.Kind);
        if (kind == null)
            return BadRequest("Invalid kind. Expected publisher, realm, or account.");

        var preference = await timeline.MarkDiscoveryPreferenceAsync(
            currentUser,
            kind.Value,
            request.ReferenceId,
            request.Reason
        );
        return Ok(preference);
    }

    [HttpDelete("uninterested")]
    public async Task<ActionResult> RemoveUninterested(
        [FromQuery] string kind,
        [FromQuery] Guid referenceId
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var parsedKind = ParseKind(kind);
        if (parsedKind == null)
            return BadRequest("Invalid kind. Expected publisher, realm, or account.");

        var removed = await timeline.RemoveDiscoveryPreferenceAsync(
            currentUser,
            parsedKind.Value,
            referenceId
        );
        return removed ? Ok() : NotFound();
    }

    [HttpPost("feedback")]
    public async Task<ActionResult<RecommendationFeedbackResult>> LeaveFeedback(
        [FromBody] RecommendationFeedbackRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var feedback = ParseFeedback(request.Feedback);
        if (feedback == null)
            return BadRequest("Invalid feedback. Expected positive or negative.");

        if (!IsFeedbackKindSupported(request.Kind))
            return BadRequest("Invalid kind. Expected post, publisher, tag, or category.");

        var result = await timeline.ApplyRecommendationFeedbackAsync(
            currentUser,
            request.Kind,
            request.ReferenceId,
            feedback.Value,
            request.Reason,
            request.Suppress
        );
        if (result == null)
            return NotFound("Referenced content was not found.");

        return Ok(result);
    }

    [HttpPut("weights")]
    public async Task<ActionResult<SnPostInterestProfile>> AdjustWeight(
        [FromBody] RecommendationWeightChangeRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var kind = ParseInterestKind(request.Kind);
        if (kind == null)
            return BadRequest("Invalid kind. Expected publisher, tag, or category.");

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

        return kind.Trim().ToLowerInvariant() is "post" or "publisher" or "tag" or "category";
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
            "tag" => PostInterestKind.Tag,
            "category" => PostInterestKind.Category,
            _ => null,
        };
    }
}
