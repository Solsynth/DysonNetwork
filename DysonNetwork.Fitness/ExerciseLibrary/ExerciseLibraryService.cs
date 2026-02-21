using DysonNetwork.Fitness.ExerciseLibrary;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Fitness.ExerciseLibrary;

public class ExerciseLibraryService(AppDatabase db, ILogger<ExerciseLibraryService> logger)
{
    public async Task<SnExerciseLibrary?> GetExerciseByIdAsync(Guid id)
    {
        return await db.ExerciseLibrary
            .FirstOrDefaultAsync(e => e.Id == id && e.DeletedAt == null);
    }

    public async Task<IEnumerable<SnExerciseLibrary>> GetPublicExercisesAsync(int skip = 0, int take = 50)
    {
        return await db.ExerciseLibrary
            .Where(e => e.IsPublic && e.DeletedAt == null)
            .OrderBy(e => e.Name)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    public async Task<IEnumerable<SnExerciseLibrary>> GetExercisesByAccountAsync(Guid accountId, int skip = 0, int take = 50)
    {
        return await db.ExerciseLibrary
            .Where(e => e.AccountId == accountId && e.DeletedAt == null)
            .OrderBy(e => e.Name)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    public async Task<IEnumerable<SnExerciseLibrary>> SearchExercisesAsync(string query, ExerciseCategory? category = null, DifficultyLevel? difficulty = null, int skip = 0, int take = 20)
    {
        var dbQuery = db.ExerciseLibrary
            .Where(e => (e.IsPublic || e.AccountId != null) && e.DeletedAt == null)
            .Where(e => EF.Functions.ILike(e.Name, $"%{query}%"))
            .AsQueryable();

        if (category.HasValue)
            dbQuery = dbQuery.Where(e => e.Category == category.Value);

        if (difficulty.HasValue)
            dbQuery = dbQuery.Where(e => e.Difficulty == difficulty.Value);

        return await dbQuery
            .OrderBy(e => e.Name)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    public async Task<SnExerciseLibrary> CreateExerciseAsync(SnExerciseLibrary exercise)
    {
        db.ExerciseLibrary.Add(exercise);
        await db.SaveChangesAsync();
        logger.LogInformation("Created exercise {ExerciseId} '{ExerciseName}'", exercise.Id, exercise.Name);
        return exercise;
    }

    public async Task<SnExerciseLibrary?> UpdateExerciseAsync(Guid id, SnExerciseLibrary updated)
    {
        var exercise = await db.ExerciseLibrary.FirstOrDefaultAsync(e => e.Id == id && e.DeletedAt == null);
        if (exercise is null) return null;

        exercise.Name = updated.Name;
        exercise.Description = updated.Description;
        exercise.Category = updated.Category;
        exercise.MuscleGroups = updated.MuscleGroups;
        exercise.Difficulty = updated.Difficulty;
        exercise.Equipment = updated.Equipment;
        exercise.IsPublic = updated.IsPublic;
        exercise.UpdatedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow);

        await db.SaveChangesAsync();
        logger.LogInformation("Updated exercise {ExerciseId}", id);
        return exercise;
    }

    public async Task<bool> DeleteExerciseAsync(Guid id)
    {
        var exercise = await db.ExerciseLibrary.FirstOrDefaultAsync(e => e.Id == id && e.DeletedAt == null);
        if (exercise is null) return false;

        exercise.DeletedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow);
        await db.SaveChangesAsync();
        logger.LogInformation("Soft deleted exercise {ExerciseId}", id);
        return true;
    }
}
