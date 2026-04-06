using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Fitness.Goals;

namespace DysonNetwork.Fitness.Metrics;

[ApiController]
[Route("/api/metrics")]
[Authorize]
public class MetricController(AppDatabase db, MetricService metricService, GoalService goalService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<SnFitnessMetric>>> ListMetrics(
        [FromQuery] FitnessMetricType? type = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var metrics = await metricService.GetMetricsByAccountAsync(accountId, type, skip, take);
        var totalCount = await db.FitnessMetrics.CountAsync(m => m.AccountId == accountId && m.DeletedAt == null);
        
        Response.Headers.Append("X-Total", totalCount.ToString());
        return Ok(metrics);
    }

    [HttpGet("latest")]
    public async Task<ActionResult<Dictionary<FitnessMetricType, SnFitnessMetric>>> GetLatestMetrics()
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var latestMetrics = await metricService.GetLatestMetricsByTypeAsync(accountId);
        return Ok(latestMetrics);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SnFitnessMetric>> GetMetric(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        
        var metric = await metricService.GetMetricByIdAsync(id);
        if (metric is null) return NotFound();
        
        if (metric.AccountId != Guid.Parse(currentUser.Id)) return Forbid();
        
        return Ok(metric);
    }

    [HttpPost]
    public async Task<ActionResult<SnFitnessMetric>> CreateMetric([FromBody] CreateMetricRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var metric = new SnFitnessMetric
        {
            ExternalId = request.ExternalId,
            AccountId = accountId,
            MetricType = request.MetricType,
            Value = request.Value,
            Unit = request.Unit,
            RecordedAt = request.RecordedAt,
            Notes = request.Notes,
            Source = request.Source,
            CreatedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow),
            UpdatedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow)
        };

        var created = await metricService.CreateMetricAsync(metric);
        
        await goalService.RecalculateGoalsForMetricTypeAsync(accountId, request.MetricType);
        
        return CreatedAtAction(nameof(GetMetric), new { id = created.Id }, created);
    }

    [HttpPost("batch")]
    public async Task<ActionResult<List<SnFitnessMetric>>> CreateMetricsBatch([FromBody] CreateMetricsBatchRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var now = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow);
        var metrics = request.Metrics.Select(m => new SnFitnessMetric
        {
            ExternalId = m.ExternalId,
            AccountId = accountId,
            MetricType = m.MetricType,
            Value = m.Value,
            Unit = m.Unit,
            RecordedAt = m.RecordedAt,
            Notes = m.Notes,
            Source = m.Source,
            CreatedAt = now,
            UpdatedAt = now
        });

        var created = await metricService.CreateMetricsBatchAsync(metrics);
        
        var metricTypes = request.Metrics.Select(m => m.MetricType).Distinct();
        foreach (var type in metricTypes)
        {
            await goalService.RecalculateGoalsForMetricTypeAsync(accountId, type);
        }
        
        return Ok(created);
    }

    // DTOs
    public record CreateMetricRequest(
        FitnessMetricType MetricType,
        decimal Value,
        string Unit,
        NodaTime.Instant RecordedAt,
        string? Notes = null,
        string? Source = null,
        string? ExternalId = null
    );

    public record UpdateMetricRequest(
        FitnessMetricType MetricType,
        decimal Value,
        string Unit,
        NodaTime.Instant RecordedAt,
        string? Notes = null,
        string? Source = null
    );

    public record CreateMetricsBatchRequest(List<CreateMetricRequestItem> Metrics);

    public record CreateMetricRequestItem(
        FitnessMetricType MetricType,
        decimal Value,
        string Unit,
        NodaTime.Instant RecordedAt,
        string? Notes = null,
        string? Source = null,
        string? ExternalId = null
    );
}
