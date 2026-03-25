using DysonNetwork.Shared.Models;
using DysonNetwork.Sphere.Automod;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Automod;

[ApiController]
[Route("/api/automod")]
public class AutomodController(
    AppDatabase db,
    AutomodService automodService
) : ControllerBase
{
    [HttpGet("rules")]
    public async Task<ActionResult<List<SnAutomodRule>>> ListRules(
        [FromQuery] bool? enabledOnly = null
    )
    {
        var query = db.AutomodRules.AsQueryable();

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
    public async Task<ActionResult<SnAutomodRule>> GetRule(Guid id)
    {
        var rule = await db.AutomodRules.FindAsync(id);
        if (rule is null)
            return NotFound();

        return Ok(rule);
    }

    [HttpPost("rules")]
    public async Task<ActionResult<SnAutomodRule>> CreateRule([FromBody] CreateAutomodRuleRequest request)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var rule = new SnAutomodRule
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            Type = request.Type,
            DefaultAction = request.DefaultAction,
            Pattern = request.Pattern,
            IsRegex = request.IsRegex,
            DerankWeight = request.DerankWeight,
            IsEnabled = request.IsEnabled,
            Priority = request.Priority,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.AutomodRules.Add(rule);
        await db.SaveChangesAsync();

        await automodService.InvalidateRulesCacheAsync();

        return Created($"/api/automod/rules/{rule.Id}", rule);
    }

    [HttpPut("rules/{id:guid}")]
    public async Task<ActionResult<SnAutomodRule>> UpdateRule(
        Guid id,
        [FromBody] UpdateAutomodRuleRequest request
    )
    {
        var rule = await db.AutomodRules.FindAsync(id);
        if (rule is null)
            return NotFound();

        if (!string.IsNullOrEmpty(request.Name))
            rule.Name = request.Name;
        if (request.Description is not null)
            rule.Description = request.Description;
        if (request.Type is not null)
            rule.Type = request.Type.Value;
        if (request.DefaultAction is not null)
            rule.DefaultAction = request.DefaultAction.Value;
        if (!string.IsNullOrEmpty(request.Pattern))
            rule.Pattern = request.Pattern;
        if (request.IsRegex is not null)
            rule.IsRegex = request.IsRegex.Value;
        if (request.DerankWeight is not null)
            rule.DerankWeight = request.DerankWeight.Value;
        if (request.IsEnabled is not null)
            rule.IsEnabled = request.IsEnabled.Value;
        if (request.Priority is not null)
            rule.Priority = request.Priority.Value;

        rule.UpdatedAt = SystemClock.Instance.GetCurrentInstant();

        await db.SaveChangesAsync();
        await automodService.InvalidateRulesCacheAsync();

        return Ok(rule);
    }

    [HttpDelete("rules/{id:guid}")]
    public async Task<IActionResult> DeleteRule(Guid id)
    {
        var rule = await db.AutomodRules.FindAsync(id);
        if (rule is null)
            return NotFound();

        db.AutomodRules.Remove(rule);
        await db.SaveChangesAsync();

        await automodService.InvalidateRulesCacheAsync();

        return NoContent();
    }

    [HttpPost("rules/bulk")]
    public async Task<ActionResult<List<SnAutomodRule>>> BulkUpsert([FromBody] List<AutomodRuleDto> rules)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var results = new List<SnAutomodRule>();

        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            foreach (var ruleDto in rules)
            {
                var existingRule = await db.AutomodRules
                    .FirstOrDefaultAsync(r => r.Name == ruleDto.Name);

                if (existingRule is null)
                {
                    var newRule = new SnAutomodRule
                    {
                        Id = Guid.NewGuid(),
                        Name = ruleDto.Name,
                        Description = ruleDto.Description,
                        Type = ruleDto.Type,
                        DefaultAction = ruleDto.DefaultAction,
                        Pattern = ruleDto.Pattern,
                        IsRegex = ruleDto.IsRegex,
                        DerankWeight = ruleDto.DerankWeight,
                        IsEnabled = ruleDto.IsEnabled,
                        Priority = ruleDto.Priority,
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    db.AutomodRules.Add(newRule);
                    results.Add(newRule);
                }
                else
                {
                    existingRule.Description = ruleDto.Description;
                    existingRule.Type = ruleDto.Type;
                    existingRule.DefaultAction = ruleDto.DefaultAction;
                    existingRule.Pattern = ruleDto.Pattern;
                    existingRule.IsRegex = ruleDto.IsRegex;
                    existingRule.DerankWeight = ruleDto.DerankWeight;
                    existingRule.IsEnabled = ruleDto.IsEnabled;
                    existingRule.Priority = ruleDto.Priority;
                    existingRule.UpdatedAt = now;
                    results.Add(existingRule);
                }
            }

            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            await automodService.InvalidateRulesCacheAsync();

            return Ok(results);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    [HttpPost("rules/{id:guid}/test")]
    public async Task<ActionResult<List<AutomodRuleResult>>> TestRule(
        Guid id,
        [FromBody] TestAutomodRequest request
    )
    {
        var rule = await db.AutomodRules.FindAsync(id);
        if (rule is null)
            return NotFound();

        var contentToCheck = request.Content ?? string.Empty;
        var matchedText = MatchRuleContent(rule, contentToCheck);

        if (matchedText is null)
            return Ok(new List<AutomodRuleResult>());

        return Ok(new List<AutomodRuleResult>
        {
            new()
            {
                RuleId = rule.Id,
                RuleName = rule.Name,
                Type = rule.Type,
                Action = rule.DefaultAction,
                DerankWeight = rule.DerankWeight,
                MatchedText = matchedText
            }
        });
    }

    private static string? MatchRuleContent(SnAutomodRule rule, string content)
    {
        try
        {
            if (rule.IsRegex)
            {
                var options = System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                              System.Text.RegularExpressions.RegexOptions.Compiled;
                var match = System.Text.RegularExpressions.Regex.Match(content, rule.Pattern, options);
                return match.Success ? match.Value : null;
            }
            else
            {
                return content.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase)
                    ? rule.Pattern
                    : null;
            }
        }
        catch
        {
            return null;
        }
    }
}

public class CreateAutomodRuleRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public AutomodRuleType Type { get; set; }
    public AutomodRuleAction DefaultAction { get; set; }
    public string Pattern { get; set; } = string.Empty;
    public bool IsRegex { get; set; }
    public int DerankWeight { get; set; } = 10;
    public bool IsEnabled { get; set; } = true;
    public int Priority { get; set; } = 0;
}

public class UpdateAutomodRuleRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public AutomodRuleType? Type { get; set; }
    public AutomodRuleAction? DefaultAction { get; set; }
    public string? Pattern { get; set; }
    public bool? IsRegex { get; set; }
    public int? DerankWeight { get; set; }
    public bool? IsEnabled { get; set; }
    public int? Priority { get; set; }
}

public class AutomodRuleDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public AutomodRuleType Type { get; set; }
    public AutomodRuleAction DefaultAction { get; set; }
    public string Pattern { get; set; } = string.Empty;
    public bool IsRegex { get; set; }
    public int DerankWeight { get; set; } = 10;
    public bool IsEnabled { get; set; } = true;
    public int Priority { get; set; } = 0;
}

public class TestAutomodRequest
{
    public string? Content { get; set; }
}