using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;

namespace DysonNetwork.Fitness.Goals;

[ApiController]
[Route("/api/goals")]
[Authorize]
public class GoalController(AppDatabase db, GoalService goalService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<SnFitnessGoal>>> ListGoals(
        [FromQuery] FitnessGoalStatus? status = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 20)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var goals = await goalService.GetGoalsByAccountAsync(accountId, status, skip, take);
        var totalCount = await db.FitnessGoals.CountAsync(g => g.AccountId == accountId && g.DeletedAt == null);
        
        if (status.HasValue)
            totalCount = await db.FitnessGoals.CountAsync(g => g.AccountId == accountId && g.Status == status && g.DeletedAt == null);
        
        Response.Headers.Append("X-Total", totalCount.ToString());
        return Ok(goals);
    }

    [HttpGet("stats")]
    public async Task<ActionResult<GoalStats>> GetGoalStats()
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var activeCount = await goalService.GetActiveGoalsCountAsync(accountId);
        var completedCount = await db.FitnessGoals.CountAsync(g => g.AccountId == accountId && g.Status == FitnessGoalStatus.Completed && g.DeletedAt == null);

        return Ok(new GoalStats(activeCount, completedCount));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SnFitnessGoal>> GetGoal(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        
        var goal = await goalService.GetGoalByIdAsync(id);
        if (goal is null) return NotFound();
        
        if (goal.AccountId != Guid.Parse(currentUser.Id)) return Forbid();
        
        return Ok(goal);
    }

    [HttpPost]
    public async Task<ActionResult<SnFitnessGoal>> CreateGoal([FromBody] CreateGoalRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var goal = new SnFitnessGoal
        {
            AccountId = accountId,
            Title = request.Title,
            Description = request.Description,
            GoalType = request.GoalType,
            TargetValue = request.TargetValue,
            CurrentValue = request.CurrentValue ?? 0,
            Unit = request.Unit,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Status = FitnessGoalStatus.Active,
            Notes = request.Notes,
            CreatedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow),
            UpdatedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow)
        };

        var created = await goalService.CreateGoalAsync(goal);
        return CreatedAtAction(nameof(GetGoal), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SnFitnessGoal>> UpdateGoal(Guid id, [FromBody] UpdateGoalRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        
        var existing = await goalService.GetGoalByIdAsync(id);
        if (existing is null) return NotFound();
        if (existing.AccountId != Guid.Parse(currentUser.Id)) return Forbid();

        var updated = new SnFitnessGoal
        {
            Title = request.Title,
            Description = request.Description,
            GoalType = request.GoalType,
            TargetValue = request.TargetValue,
            CurrentValue = request.CurrentValue,
            Unit = request.Unit,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Status = request.Status,
            Notes = request.Notes
        };

        var result = await goalService.UpdateGoalAsync(id, updated);
        return Ok(result);
    }

    [HttpPatch("{id:guid}/progress")]
    public async Task<ActionResult<SnFitnessGoal>> UpdateProgress(Guid id, [FromBody] UpdateProgressRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        
        var goal = await goalService.GetGoalByIdAsync(id);
        if (goal is null) return NotFound();
        if (goal.AccountId != Guid.Parse(currentUser.Id)) return Forbid();

        var result = await goalService.UpdateGoalProgressAsync(id, request.CurrentValue);
        return Ok(result);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<SnFitnessGoal>> UpdateStatus(Guid id, [FromBody] UpdateStatusRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        
        var goal = await goalService.GetGoalByIdAsync(id);
        if (goal is null) return NotFound();
        if (goal.AccountId != Guid.Parse(currentUser.Id)) return Forbid();

        goal.Status = request.Status;
        goal.UpdatedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow);
        await db.SaveChangesAsync();

        return Ok(goal);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteGoal(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        
        var goal = await goalService.GetGoalByIdAsync(id);
        if (goal is null) return NotFound();
        if (goal.AccountId != Guid.Parse(currentUser.Id)) return Forbid();

        var success = await goalService.DeleteGoalAsync(id);
        return success ? NoContent() : NotFound();
    }

    // DTOs
    public record CreateGoalRequest(
        string Title,
        FitnessGoalType GoalType,
        NodaTime.Instant StartDate,
        string? Description = null,
        decimal? TargetValue = null,
        decimal? CurrentValue = null,
        string? Unit = null,
        NodaTime.Instant? EndDate = null,
        string? Notes = null
    );

    public record UpdateGoalRequest(
        string Title,
        FitnessGoalType GoalType,
        NodaTime.Instant StartDate,
        FitnessGoalStatus Status,
        string? Description = null,
        decimal? TargetValue = null,
        decimal? CurrentValue = null,
        string? Unit = null,
        NodaTime.Instant? EndDate = null,
        string? Notes = null
    );

    public record UpdateProgressRequest(decimal CurrentValue);

    public record UpdateStatusRequest(FitnessGoalStatus Status);

    public record GoalStats(int ActiveCount, int CompletedCount);
}
