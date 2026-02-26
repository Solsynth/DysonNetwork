using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;

namespace DysonNetwork.Fitness.ExerciseLibrary;

[ApiController]
[Route("/api/exercises")]
public class ExerciseLibraryController(AppDatabase db, ExerciseLibraryService exerciseService) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<List<SnExerciseLibrary>>> ListPublicExercises(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50)
    {
        var exercises = await exerciseService.GetPublicExercisesAsync(skip, take);
        var totalCount = await db.ExerciseLibrary.CountAsync(e => e.IsPublic && e.DeletedAt == null);
        
        Response.Headers.Append("X-Total", totalCount.ToString());
        return Ok(exercises);
    }

    [HttpGet("search")]
    [AllowAnonymous]
    public async Task<ActionResult<List<SnExerciseLibrary>>> SearchExercises(
        [FromQuery] string query,
        [FromQuery] ExerciseCategory? category = null,
        [FromQuery] DifficultyLevel? difficulty = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 20)
    {
        if (string.IsNullOrWhiteSpace(query))
            return BadRequest("Search query is required");

        var exercises = await exerciseService.SearchExercisesAsync(query, category, difficulty, skip, take);
        return Ok(exercises);
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<SnExerciseLibrary>> GetExercise(Guid id)
    {
        var exercise = await exerciseService.GetExerciseByIdAsync(id);
        if (exercise is null) return NotFound();
        
        return Ok(exercise);
    }

    [HttpGet("my")]
    [Authorize]
    public async Task<ActionResult<List<SnExerciseLibrary>>> ListMyExercises(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var exercises = await exerciseService.GetExercisesByAccountAsync(accountId, skip, take);
        var totalCount = await db.ExerciseLibrary.CountAsync(e => e.AccountId == accountId && e.DeletedAt == null);
        
        Response.Headers.Append("X-Total", totalCount.ToString());
        return Ok(exercises);
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<SnExerciseLibrary>> CreateExercise([FromBody] CreateExerciseRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var exercise = new SnExerciseLibrary
        {
            Name = request.Name,
            Description = request.Description,
            Category = request.Category,
            MuscleGroups = request.MuscleGroups,
            Difficulty = request.Difficulty,
            Equipment = request.Equipment,
            IsPublic = request.IsPublic,
            AccountId = accountId,
            CreatedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow),
            UpdatedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow)
        };

        var created = await exerciseService.CreateExerciseAsync(exercise);
        return CreatedAtAction(nameof(GetExercise), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [Authorize]
    public async Task<ActionResult<SnExerciseLibrary>> UpdateExercise(Guid id, [FromBody] UpdateExerciseRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        
        var existing = await exerciseService.GetExerciseByIdAsync(id);
        if (existing is null) return NotFound();
        
        // Only owner or admin can update
        if (existing.AccountId != Guid.Parse(currentUser.Id))
        {
            // Could add admin check here
            return Forbid();
        }

        var updated = new SnExerciseLibrary
        {
            Name = request.Name,
            Description = request.Description,
            Category = request.Category,
            MuscleGroups = request.MuscleGroups,
            Difficulty = request.Difficulty,
            Equipment = request.Equipment,
            IsPublic = request.IsPublic
        };

        var result = await exerciseService.UpdateExerciseAsync(id, updated);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteExercise(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        
        var exercise = await exerciseService.GetExerciseByIdAsync(id);
        if (exercise is null) return NotFound();
        
        // Only owner or admin can delete
        if (exercise.AccountId != Guid.Parse(currentUser.Id))
        {
            // Could add admin check here
            return Forbid();
        }

        var success = await exerciseService.DeleteExerciseAsync(id);
        return success ? NoContent() : NotFound();
    }

    // DTOs
    public record CreateExerciseRequest(
        string Name,
        ExerciseCategory Category,
        DifficultyLevel Difficulty,
        string? Description = null,
        List<string>? MuscleGroups = null,
        List<string>? Equipment = null,
        bool IsPublic = true
    );

    public record UpdateExerciseRequest(
        string Name,
        ExerciseCategory Category,
        DifficultyLevel Difficulty,
        string? Description = null,
        List<string>? MuscleGroups = null,
        List<string>? Equipment = null,
        bool IsPublic = true
    );
}
