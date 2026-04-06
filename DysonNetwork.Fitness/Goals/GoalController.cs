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
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
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
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var activeCount = await goalService.GetActiveGoalsCountAsync(accountId);
        var completedCount = await db.FitnessGoals.CountAsync(g => g.AccountId == accountId && g.Status == FitnessGoalStatus.Completed && g.DeletedAt == null);

        return Ok(new GoalStats(activeCount, completedCount));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SnFitnessGoal>> GetGoal(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        
        var goal = await goalService.GetGoalByIdAsync(id);
        if (goal is null) return NotFound();
        
        if (goal.AccountId != Guid.Parse(currentUser.Id)) return Forbid();
        
        return Ok(goal);
    }

    [HttpPost]
    public async Task<ActionResult<SnFitnessGoal>> CreateGoal([FromBody] CreateGoalRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
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
            BoundWorkoutType = request.BoundWorkoutType,
            BoundMetricType = request.BoundMetricType,
            AutoUpdateProgress = request.AutoUpdateProgress,
            RepeatType = request.RepeatType,
            RepeatInterval = request.RepeatInterval,
            RepeatCount = request.RepeatCount,
            CurrentRepetition = 1,
            CreatedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow),
            UpdatedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow)
        };

        var created = await goalService.CreateGoalAsync(goal);
        return CreatedAtAction(nameof(GetGoal), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SnFitnessGoal>> UpdateGoal(Guid id, [FromBody] UpdateGoalRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        
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
            Notes = request.Notes,
            BoundWorkoutType = request.BoundWorkoutType,
            BoundMetricType = request.BoundMetricType,
            AutoUpdateProgress = request.AutoUpdateProgress,
            RepeatType = request.RepeatType,
            RepeatInterval = request.RepeatInterval,
            RepeatCount = request.RepeatCount
        };

        var result = await goalService.UpdateGoalAsync(id, updated);
        return Ok(result);
    }

    [HttpPatch("{id:guid}/progress")]
    public async Task<ActionResult<SnFitnessGoal>> UpdateProgress(Guid id, [FromBody] UpdateProgressRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        
        var goal = await goalService.GetGoalByIdAsync(id);
        if (goal is null) return NotFound();
        if (goal.AccountId != Guid.Parse(currentUser.Id)) return Forbid();

        if (goal.AutoUpdateProgress && (goal.BoundWorkoutType.HasValue || goal.BoundMetricType.HasValue))
        {
            return BadRequest(new { error = "Progress is auto-updated from bound workout/metric. Set auto_update_progress to false to update manually." });
        }

        var result = await goalService.UpdateGoalProgressAsync(id, request.CurrentValue);
        
        if (result?.Status == FitnessGoalStatus.Completed)
        {
            await goalService.CreateNextRepeatingGoalAsync(id);
        }
        
        return Ok(result);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<SnFitnessGoal>> UpdateStatus(Guid id, [FromBody] UpdateStatusRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        
        var goal = await goalService.GetGoalByIdAsync(id);
        if (goal is null) return NotFound();
        if (goal.AccountId != Guid.Parse(currentUser.Id)) return Forbid();

        goal.Status = request.Status;
        
        if (request.Status == FitnessGoalStatus.Paused || request.Status == FitnessGoalStatus.Cancelled)
        {
            var rootGoalId = goal.ParentGoalId ?? goal.Id;
            var relatedGoals = await goalService.GetGoalHistoryAsync(id);
            foreach (var g in relatedGoals.Where(g => g.Status == FitnessGoalStatus.Active))
            {
                g.Status = request.Status;
                g.UpdatedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow);
            }
        }
        
        goal.UpdatedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow);
        await db.SaveChangesAsync();

        if (request.Status == FitnessGoalStatus.Completed)
        {
            await goalService.CreateNextRepeatingGoalAsync(id);
        }

        return Ok(goal);
    }

    [HttpGet("{id:guid}/history")]
    public async Task<ActionResult<List<SnFitnessGoal>>> GetGoalHistory(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        
        var goal = await goalService.GetGoalByIdAsync(id);
        if (goal is null) return NotFound();
        if (goal.AccountId != Guid.Parse(currentUser.Id)) return Forbid();

        var history = await goalService.GetGoalHistoryAsync(id);
        return Ok(history);
    }

    [HttpPatch("{id:guid}/recalculate")]
    public async Task<ActionResult<SnFitnessGoal>> RecalculateGoal(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        
        var goal = await goalService.GetGoalByIdAsync(id);
        if (goal is null) return NotFound();
        if (goal.AccountId != Guid.Parse(currentUser.Id)) return Forbid();

        await goalService.UpdateGoalProgressFromDataAsync(id);
        
        var updated = await goalService.GetGoalByIdAsync(id);
        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteGoal(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        
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
        string? Notes = null,
        WorkoutType? BoundWorkoutType = null,
        FitnessMetricType? BoundMetricType = null,
        bool AutoUpdateProgress = true,
        RepeatType RepeatType = RepeatType.None,
        int RepeatInterval = 1,
        int? RepeatCount = null
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
        string? Notes = null,
        WorkoutType? BoundWorkoutType = null,
        FitnessMetricType? BoundMetricType = null,
        bool AutoUpdateProgress = true,
        RepeatType RepeatType = RepeatType.None,
        int RepeatInterval = 1,
        int? RepeatCount = null
    );

    public record UpdateProgressRequest(decimal CurrentValue);

    public record UpdateStatusRequest(FitnessGoalStatus Status);

    public record GoalStats(int ActiveCount, int CompletedCount);
}
