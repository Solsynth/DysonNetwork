using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.ActivityPub;

[ApiController]
[Route("/api/fediverse/moderation")]
public class FediverseModerationController(
    AppDatabase db,
    FediverseModerationService moderationService
) : ControllerBase
{
    [HttpGet("rules")]
    public async Task<ActionResult<List<SnFediverseModerationRule>>> ListRules(
        [FromQuery] bool? enabledOnly = null
    )
    {
        var query = db.FediverseModerationRules.AsQueryable();

        if (enabledOnly == true)
        {
            var now = SystemClock.Instance.GetCurrentInstant();
            query = query.Where(r => r.IsEnabled)
                .Where(r => r.ExpiresAt == null || r.ExpiresAt > now);
        }

        var rules = await query
            .OrderBy(r => r.Priority)
            .ToListAsync();

        return Ok(rules);
    }

    [HttpGet("rules/{id:guid}")]
    public async Task<ActionResult<SnFediverseModerationRule>> GetRule(Guid id)
    {
        var rule = await db.FediverseModerationRules.FindAsync(id);
        if (rule is null)
            return NotFound();

        return Ok(rule);
    }

    [HttpPost("rules")]
    public async Task<ActionResult<SnFediverseModerationRule>> CreateRule([FromBody] CreateFediverseModerationRuleRequest request)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var rule = new SnFediverseModerationRule
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            Type = request.Type,
            Action = request.Action,
            Domain = request.Domain,
            KeywordPattern = request.KeywordPattern,
            IsRegex = request.IsRegex,
            IsEnabled = request.IsEnabled,
            ReportThreshold = request.ReportThreshold,
            Priority = request.Priority,
            IsSystemRule = false,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.FediverseModerationRules.Add(rule);
        await db.SaveChangesAsync();

        moderationService.InvalidateCache();

        return Created($"/api/fediverse/moderation/rules/{rule.Id}", rule);
    }

    [HttpPut("rules/{id:guid}")]
    public async Task<ActionResult<SnFediverseModerationRule>> UpdateRule(
        Guid id,
        [FromBody] UpdateFediverseModerationRuleRequest request
    )
    {
        var rule = await db.FediverseModerationRules.FindAsync(id);
        if (rule is null)
            return NotFound();

        if (rule.IsSystemRule)
            return BadRequest(new { error = "Cannot modify system rules" });

        if (!string.IsNullOrEmpty(request.Name))
            rule.Name = request.Name;
        if (request.Description != null)
            rule.Description = request.Description;
        if (request.Type != null)
            rule.Type = request.Type.Value;
        if (request.Action != null)
            rule.Action = request.Action.Value;
        if (request.Domain != null)
            rule.Domain = request.Domain;
        if (request.KeywordPattern != null)
            rule.KeywordPattern = request.KeywordPattern;
        if (request.IsRegex != null)
            rule.IsRegex = request.IsRegex.Value;
        if (request.IsEnabled != null)
            rule.IsEnabled = request.IsEnabled.Value;
        if (request.ReportThreshold != null)
            rule.ReportThreshold = request.ReportThreshold;
        if (request.Priority != null)
            rule.Priority = request.Priority.Value;

        rule.UpdatedAt = SystemClock.Instance.GetCurrentInstant();

        await db.SaveChangesAsync();
        moderationService.InvalidateCache();

        return Ok(rule);
    }

    [HttpDelete("rules/{id:guid}")]
    public async Task<IActionResult> DeleteRule(Guid id)
    {
        var rule = await db.FediverseModerationRules.FindAsync(id);
        if (rule is null)
            return NotFound();

        if (rule.IsSystemRule)
            return BadRequest(new { error = "Cannot delete system rules" });

        db.FediverseModerationRules.Remove(rule);
        await db.SaveChangesAsync();
        moderationService.InvalidateCache();

        return NoContent();
    }

    [HttpPost("rules/{id:guid}/toggle")]
    public async Task<IActionResult> ToggleRule(Guid id, [FromBody] ToggleRuleRequest request)
    {
        var rule = await db.FediverseModerationRules.FindAsync(id);
        if (rule is null)
            return NotFound();

        if (rule.IsSystemRule)
            return BadRequest(new { error = "Cannot toggle system rules" });

        rule.IsEnabled = request.Enabled;
        rule.UpdatedAt = SystemClock.Instance.GetCurrentInstant();

        await db.SaveChangesAsync();
        moderationService.InvalidateCache();

        return Ok(rule);
    }

    [HttpPost("check-domain")]
    public async Task<ActionResult<FediverseModerationResult>> CheckDomain([FromBody] CheckDomainRequest request)
    {
        var result = await moderationService.CheckDomainAsync(request.Domain);
        return Ok(result);
    }

    [HttpPost("check-actor")]
    public async Task<ActionResult<FediverseModerationResult>> CheckActor([FromBody] CheckActorRequest request)
    {
        var result = await moderationService.CheckActorAsync(request.ActorUri, request.Content, request.ActorDomain);
        return Ok(result);
    }
}

public class CreateFediverseModerationRuleRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public FediverseModerationRuleType Type { get; set; }
    public FediverseModerationAction Action { get; set; }
    public string? Domain { get; set; }
    public string? KeywordPattern { get; set; }
    public bool IsRegex { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int? ReportThreshold { get; set; }
    public int Priority { get; set; }
}

public class UpdateFediverseModerationRuleRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public FediverseModerationRuleType? Type { get; set; }
    public FediverseModerationAction? Action { get; set; }
    public string? Domain { get; set; }
    public string? KeywordPattern { get; set; }
    public bool? IsRegex { get; set; }
    public bool? IsEnabled { get; set; }
    public int? ReportThreshold { get; set; }
    public int? Priority { get; set; }
}

public class ToggleRuleRequest
{
    public bool Enabled { get; set; }
}

public class CheckDomainRequest
{
    public string Domain { get; set; } = string.Empty;
}

public class CheckActorRequest
{
    public string? ActorUri { get; set; }
    public string? Content { get; set; }
    public string? ActorDomain { get; set; }
}
