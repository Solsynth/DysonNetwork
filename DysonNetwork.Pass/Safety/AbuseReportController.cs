using System.ComponentModel.DataAnnotations;
using DysonNetwork.Pass.Permission;
using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Pass.Safety;

[ApiController]
[Route("/api/safety/reports")]
public class SnAbuseReportController(
    SafetyService safety
) : ControllerBase
{
    public class CreateReportRequest
    {
        [Required] public string ResourceIdentifier { get; set; } = null!;

        [Required] public AbuseReportType Type { get; set; }

        [Required]
        [MinLength(10)]
        [MaxLength(1000)]
        public string Reason { get; set; } = null!;
    }

    [HttpPost("")]
    [Authorize]
    [ProducesResponseType<SnAbuseReport>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SnAbuseReport>> CreateReport([FromBody] CreateReportRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        try
        {
            var report = await safety.CreateReport(
                request.ResourceIdentifier,
                request.Type,
                request.Reason,
                currentUser.Id
            );

            return Ok(report);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("")]
    [Authorize]
    [RequiredPermission("safety", "reports.view")]
    [ProducesResponseType<List<SnAbuseReport>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<SnAbuseReport>>> GetReports(
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20,
        [FromQuery] bool includeResolved = false
    )
    {
        var totalCount = await safety.CountReports(includeResolved);
        var reports = await safety.GetReports(offset, take, includeResolved);
        Response.Headers["X-Total"] = totalCount.ToString();
        return Ok(reports);
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<List<SnAbuseReport>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<SnAbuseReport>>> GetMyReports(
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20,
        [FromQuery] bool includeResolved = false
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var totalCount = await safety.CountUserReports(currentUser.Id, includeResolved);
        var reports = await safety.GetUserReports(currentUser.Id, offset, take, includeResolved);
        Response.Headers["X-Total"] = totalCount.ToString();
        return Ok(reports);
    }

    [HttpGet("{id}")]
    [Authorize]
    [RequiredPermission("safety", "reports.view")]
    [ProducesResponseType<SnAbuseReport>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SnAbuseReport>> GetReportById(Guid id)
    {
        var report = await safety.GetReportById(id);
        return report == null ? NotFound() : Ok(report);
    }

    [HttpGet("me/{id}")]
    [Authorize]
    [ProducesResponseType<SnAbuseReport>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SnAbuseReport>> GetMyReportById(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var report = await safety.GetReportById(id);
        if (report == null) return NotFound();

        // Ensure the user only accesses their own reports
        if (report.AccountId != currentUser.Id) return Forbid();

        return Ok(report);
    }

    public class ResolveReportRequest
    {
        [Required]
        [MinLength(5)]
        [MaxLength(1000)]
        public string Resolution { get; set; } = null!;
    }

    [HttpPost("{id}/resolve")]
    [Authorize]
    [RequiredPermission("safety", "reports.resolve")]
    [ProducesResponseType<SnAbuseReport>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SnAbuseReport>> ResolveReport(Guid id, [FromBody] ResolveReportRequest request)
    {
        try
        {
            var report = await safety.ResolveReport(id, request.Resolution);
            return Ok(report);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("count")]
    [Authorize]
    [RequiredPermission("safety", "reports.view")]
    [ProducesResponseType<object>(StatusCodes.Status200OK)]
    public async Task<ActionResult<object>> GetReportsCount()
    {
        var count = await safety.GetPendingReportsCount();
        return Ok(new { pendingCount = count });
    }
}