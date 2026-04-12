using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Fitness.Goals;

namespace DysonNetwork.Fitness.Workouts;

[ApiController]
[Route("/api/workouts")]
[Authorize]
public class WorkoutController(AppDatabase db, WorkoutService workoutService, GoalService goalService, ILogger<WorkoutController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<SnWorkout>>> ListWorkouts([FromQuery] int skip = 0, [FromQuery] int take = 20)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var workouts = await workoutService.GetWorkoutsByAccountAsync(accountId, skip, take);
        var totalCount = await db.Workouts.CountAsync(w => w.AccountId == accountId && w.DeletedAt == null);
        
        Response.Headers.Append("X-Total", totalCount.ToString());
        return Ok(workouts);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SnWorkout>> GetWorkout(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        
        var workout = await workoutService.GetWorkoutByIdAsync(id);
        if (workout is null) return NotFound();
        
        // Verify ownership
        if (workout.AccountId != Guid.Parse(currentUser.Id)) return Forbid();
        
        return Ok(workout);
    }

    [HttpPost]
    public async Task<ActionResult<SnWorkout>> CreateWorkout([FromBody] CreateWorkoutRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var workout = new SnWorkout
        {
            AccountId = accountId,
            Name = request.Name,
            Description = request.Description,
            Type = request.Type,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            Duration = request.Duration,
            CaloriesBurned = request.CaloriesBurned,
            Notes = request.Notes,
            Visibility = request.Visibility ?? FitnessVisibility.Private,
            CreatedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow),
            UpdatedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow)
        };

        if (request.Meta != null)
        {
            workout.Meta = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(request.Meta));
        }

        var created = await workoutService.CreateWorkoutAsync(workout);
        
        await goalService.RecalculateGoalsForWorkoutTypeAsync(accountId, request.Type);
        
        return CreatedAtAction(nameof(GetWorkout), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SnWorkout>> UpdateWorkout(Guid id, [FromBody] UpdateWorkoutRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        
        var existing = await workoutService.GetWorkoutByIdAsync(id);
        if (existing is null) return NotFound();
        if (existing.AccountId != Guid.Parse(currentUser.Id)) return Forbid();

        var updated = new SnWorkout
        {
            Name = request.Name,
            Description = request.Description,
            Type = request.Type,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            Duration = request.Duration,
            CaloriesBurned = request.CaloriesBurned,
            Notes = request.Notes,
            Visibility = request.Visibility ?? FitnessVisibility.Private
        };

        if (request.Meta != null)
        {
            updated.Meta = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(request.Meta));
        }

        var result = await workoutService.UpdateWorkoutAsync(id, updated);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteWorkout(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        
        var workout = await workoutService.GetWorkoutByIdAsync(id);
        if (workout is null) return NotFound();
        if (workout.AccountId != Guid.Parse(currentUser.Id)) return Forbid();

        var success = await workoutService.DeleteWorkoutAsync(id);
        return success ? NoContent() : NotFound();
    }

    [HttpPost("batch")]
    public async Task<ActionResult<List<SnWorkout>>> CreateWorkoutsBatch([FromBody] CreateWorkoutsBatchRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var now = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow);
        var workouts = request.Workouts.Select(w =>
        {
            var duration = w.Duration;
            if (duration is null && w.EndTime.HasValue)
            {
                duration = w.EndTime.Value - w.StartTime;
            }

            return new SnWorkout
            {
                ExternalId = w.ExternalId,
                AccountId = accountId,
                Name = w.Name,
                Description = w.Description,
                Type = w.Type,
                StartTime = w.StartTime,
                EndTime = w.EndTime,
                Duration = duration,
                CaloriesBurned = w.CaloriesBurned,
                Notes = w.Notes,
                Visibility = w.Visibility ?? FitnessVisibility.Private,
                CreatedAt = now,
                UpdatedAt = now
            };
        });

        var created = await workoutService.CreateWorkoutsBatchAsync(workouts);
        
        var workoutTypes = request.Workouts.Select(w => w.Type).Distinct();
        foreach (var type in workoutTypes)
        {
            await goalService.RecalculateGoalsForWorkoutTypeAsync(accountId, type);
        }
        
        return Ok(created);
    }

    [HttpPatch("batch/visibility")]
    public async Task<ActionResult<int>> UpdateWorkoutsVisibility([FromBody] UpdateWorkoutsVisibilityBatchRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var updated = await workoutService.UpdateWorkoutsVisibilityAsync(accountId, request.WorkoutIds, request.Visibility);
        return Ok(updated);
    }

    // DTOs
    public record CreateWorkoutRequest(
        string Name,
        WorkoutType Type,
        NodaTime.Instant StartTime,
        string? Description = null,
        NodaTime.Instant? EndTime = null,
        NodaTime.Duration? Duration = null,
        int? CaloriesBurned = null,
        string? Notes = null,
        string? ExternalId = null,
        FitnessVisibility? Visibility = null,
        Dictionary<string, object>? Meta = null
    );

    public record UpdateWorkoutRequest(
        string Name,
        WorkoutType Type,
        NodaTime.Instant StartTime,
        string? Description = null,
        NodaTime.Instant? EndTime = null,
        NodaTime.Duration? Duration = null,
        int? CaloriesBurned = null,
        string? Notes = null,
        FitnessVisibility? Visibility = null,
        Dictionary<string, object>? Meta = null
    );

    public record CreateExerciseRequest(
        string ExerciseName,
        int? Sets = null,
        int? Reps = null,
        decimal? Weight = null,
        NodaTime.Duration? Duration = null,
        string? Notes = null,
        int OrderIndex = 0
    );

    public record UpdateExerciseRequest(
        string ExerciseName,
        int? Sets = null,
        int? Reps = null,
        decimal? Weight = null,
        NodaTime.Duration? Duration = null,
        string? Notes = null,
        int OrderIndex = 0
    );

    public record CreateWorkoutsBatchRequest(List<CreateWorkoutRequestItem> Workouts);

    public record CreateWorkoutRequestItem(
        string Name,
        WorkoutType Type,
        NodaTime.Instant StartTime,
        string? Description = null,
        NodaTime.Instant? EndTime = null,
        NodaTime.Duration? Duration = null,
        int? CaloriesBurned = null,
        string? Notes = null,
        string? ExternalId = null,
        FitnessVisibility? Visibility = null
    );

    public record UpdateWorkoutsVisibilityBatchRequest(List<Guid> WorkoutIds, FitnessVisibility Visibility);
}
