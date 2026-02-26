using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;

namespace DysonNetwork.Fitness.Workouts;

[ApiController]
[Route("/api/workouts")]
[Authorize]
public class WorkoutController(AppDatabase db, WorkoutService workoutService, ILogger<WorkoutController> logger) : ControllerBase
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
            CreatedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow),
            UpdatedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow)
        };

        var created = await workoutService.CreateWorkoutAsync(workout);
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
            Notes = request.Notes
        };

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

    [HttpPost("{workoutId:guid}/exercises")]
    public async Task<ActionResult<SnWorkoutExercise>> AddExercise(Guid workoutId, [FromBody] CreateExerciseRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        
        var workout = await workoutService.GetWorkoutByIdAsync(workoutId);
        if (workout is null) return NotFound();
        if (workout.AccountId != Guid.Parse(currentUser.Id)) return Forbid();

        var exercise = new SnWorkoutExercise
        {
            ExerciseName = request.ExerciseName,
            Sets = request.Sets,
            Reps = request.Reps,
            Weight = request.Weight,
            Duration = request.Duration,
            Notes = request.Notes,
            OrderIndex = request.OrderIndex,
            CreatedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow),
            UpdatedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow)
        };

        var created = await workoutService.AddExerciseToWorkoutAsync(workoutId, exercise);
        return Ok(created);
    }

    [HttpPut("exercises/{exerciseId:guid}")]
    public async Task<ActionResult<SnWorkoutExercise>> UpdateExercise(Guid exerciseId, [FromBody] UpdateExerciseRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var exercise = await db.WorkoutExercises
            .Include(e => e.Workout)
            .FirstOrDefaultAsync(e => e.Id == exerciseId && e.DeletedAt == null);
        
        if (exercise is null) return NotFound();
        if (exercise.Workout.AccountId != Guid.Parse(currentUser.Id)) return Forbid();

        var updated = new SnWorkoutExercise
        {
            ExerciseName = request.ExerciseName,
            Sets = request.Sets,
            Reps = request.Reps,
            Weight = request.Weight,
            Duration = request.Duration,
            Notes = request.Notes,
            OrderIndex = request.OrderIndex
        };

        var result = await workoutService.UpdateExerciseAsync(exerciseId, updated);
        return Ok(result);
    }

    [HttpDelete("exercises/{exerciseId:guid}")]
    public async Task<IActionResult> RemoveExercise(Guid exerciseId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var exercise = await db.WorkoutExercises
            .Include(e => e.Workout)
            .FirstOrDefaultAsync(e => e.Id == exerciseId && e.DeletedAt == null);
        
        if (exercise is null) return NotFound();
        if (exercise.Workout.AccountId != Guid.Parse(currentUser.Id)) return Forbid();

        var success = await workoutService.RemoveExerciseFromWorkoutAsync(exerciseId);
        return success ? NoContent() : NotFound();
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
        string? Notes = null
    );

    public record UpdateWorkoutRequest(
        string Name,
        WorkoutType Type,
        NodaTime.Instant StartTime,
        string? Description = null,
        NodaTime.Instant? EndTime = null,
        NodaTime.Duration? Duration = null,
        int? CaloriesBurned = null,
        string? Notes = null
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
}
