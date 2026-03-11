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
}
