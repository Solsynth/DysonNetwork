using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Passport.DomainTrust;

[ApiController]
[Route("/api/domain-blocks")]
public class DomainTrustController(DomainTrustService service) : Controller
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<SnDomainBlock>>> ListRules(
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 50
    )
    {
        var rules = await service.GetAllRulesAsync(offset, limit);
        var total = await service.GetTotalCountAsync();

        Response.Headers["X-Total"] = total.ToString();
        return Ok(rules);
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<ActionResult<SnDomainBlock>> GetRule(Guid id)
    {
        var rule = await service.GetRuleAsync(id);
        if (rule == null) return NotFound();

        return Ok(rule);
    }

    public class CreateBlockRuleRequest
    {
        [Required, MaxLength(512)]
        public string DomainPattern { get; set; } = string.Empty;

        [MaxLength(16)]
        public string? Protocol { get; set; }

        public int? PortRestriction { get; set; }

        [MaxLength(256)]
        public string? Reason { get; set; }

        public int Priority { get; set; }
        public bool IsActive { get; set; } = true;
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<SnDomainBlock>> CreateRule([FromBody] CreateBlockRuleRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var rule = new SnDomainBlock
        {
            DomainPattern = request.DomainPattern,
            Protocol = request.Protocol,
            PortRestriction = request.PortRestriction,
            Reason = request.Reason,
            Priority = request.Priority,
            IsActive = request.IsActive
        };

        var created = await service.CreateRuleAsync(rule, currentUser.Id);
        return Ok(created);
    }

    public class UpdateBlockRuleRequest
    {
        [MaxLength(512)]
        public string? DomainPattern { get; set; }

        [MaxLength(16)]
        public string? Protocol { get; set; }

        public int? PortRestriction { get; set; }

        [MaxLength(256)]
        public string? Reason { get; set; }

        public int? Priority { get; set; }
        public bool? IsActive { get; set; }
    }

    [HttpPatch("{id:guid}")]
    [Authorize]
    public async Task<ActionResult<SnDomainBlock>> UpdateRule(Guid id, [FromBody] UpdateBlockRuleRequest request)
    {
        var rule = await service.UpdateRuleAsync(id, r =>
        {
            if (request.DomainPattern != null) r.DomainPattern = request.DomainPattern;
            if (request.Protocol != null) r.Protocol = request.Protocol;
            if (request.PortRestriction.HasValue) r.PortRestriction = request.PortRestriction;
            if (request.Reason != null) r.Reason = request.Reason;
            if (request.Priority.HasValue) r.Priority = request.Priority.Value;
            if (request.IsActive.HasValue) r.IsActive = request.IsActive.Value;
        });

        if (rule == null) return NotFound();
        return Ok(rule);
    }

    [HttpDelete("{id:guid}")]
    [Authorize]
    public async Task<ActionResult> DeleteRule(Guid id)
    {
        var deleted = await service.DeleteRuleAsync(id);
        if (!deleted) return NotFound();

        return NoContent();
    }

    public class ValidateUrlRequest
    {
        [Required]
        public string Url { get; set; } = string.Empty;
    }

    [HttpPost("validate")]
    public async Task<ActionResult<DomainValidationResult>> ValidateUrl([FromBody] ValidateUrlRequest request)
    {
        var result = await service.ValidateUrlAsync(request.Url);
        return Ok(result);
    }

    [HttpGet("check")]
    public async Task<ActionResult<object>> CheckUrl([FromQuery] string url)
    {
        var result = await service.ValidateUrlAsync(url);
        return Ok(new
        {
            is_allowed = result.IsAllowed,
            block_reason = result.BlockReason
        });
    }
}
