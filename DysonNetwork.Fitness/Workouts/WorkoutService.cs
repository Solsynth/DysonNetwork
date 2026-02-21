using DysonNetwork.Fitness.Workouts;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Fitness.Workouts;

public class WorkoutService(AppDatabase db, ILogger<WorkoutService> logger)
{
    public async Task<SnWorkout?> GetWorkoutByIdAsync(Guid id)
    {
        return await db.Workouts
            .Include(w => w.Exercises)
            .FirstOrDefaultAsync(w => w.Id == id && w.DeletedAt == null);
    }

    public async Task<IEnumerable<SnWorkout>> GetWorkoutsByAccountAsync(Guid accountId, int skip = 0, int take = 20)
    {
        return await db.Workouts
            .Where(w => w.AccountId == accountId && w.DeletedAt == null)
            .OrderByDescending(w => w.StartTime)
            .Skip(skip)
            .Take(take)
            .Include(w => w.Exercises)
            .ToListAsync();
    }

    public async Task<SnWorkout> CreateWorkoutAsync(SnWorkout workout)
    {
        db.Workouts.Add(workout);
        await db.SaveChangesAsync();
        logger.LogInformation("Created workout {WorkoutId} for account {AccountId}", workout.Id, workout.AccountId);
        return workout;
    }

    public async Task<SnWorkout?> UpdateWorkoutAsync(Guid id, SnWorkout updated)
    {
        var workout = await db.Workouts.FirstOrDefaultAsync(w => w.Id == id && w.DeletedAt == null);
        if (workout is null) return null;

        workout.Name = updated.Name;
        workout.Description = updated.Description;
        workout.Type = updated.Type;
        workout.StartTime = updated.StartTime;
        workout.EndTime = updated.EndTime;
        workout.Duration = updated.Duration;
        workout.CaloriesBurned = updated.CaloriesBurned;
        workout.Notes = updated.Notes;
        workout.UpdatedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow);

        await db.SaveChangesAsync();
        logger.LogInformation("Updated workout {WorkoutId}", id);
        return workout;
    }

    public async Task<bool> DeleteWorkoutAsync(Guid id)
    {
        var workout = await db.Workouts.FirstOrDefaultAsync(w => w.Id == id && w.DeletedAt == null);
        if (workout is null) return false;

        workout.DeletedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow);
        await db.SaveChangesAsync();
        logger.LogInformation("Soft deleted workout {WorkoutId}", id);
        return true;
    }

    public async Task<SnWorkoutExercise> AddExerciseToWorkoutAsync(Guid workoutId, SnWorkoutExercise exercise)
    {
        exercise.WorkoutId = workoutId;
        db.WorkoutExercises.Add(exercise);
        await db.SaveChangesAsync();
        logger.LogInformation("Added exercise {ExerciseId} to workout {WorkoutId}", exercise.Id, workoutId);
        return exercise;
    }

    public async Task<bool> RemoveExerciseFromWorkoutAsync(Guid exerciseId)
    {
        var exercise = await db.WorkoutExercises
            .Include(e => e.Workout)
            .FirstOrDefaultAsync(e => e.Id == exerciseId && e.DeletedAt == null);
        
        if (exercise is null) return false;

        exercise.DeletedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow);
        await db.SaveChangesAsync();
        logger.LogInformation("Removed exercise {ExerciseId} from workout {WorkoutId}", exerciseId, exercise.WorkoutId);
        return true;
    }

    public async Task<SnWorkoutExercise?> UpdateExerciseAsync(Guid exerciseId, SnWorkoutExercise updated)
    {
        var exercise = await db.WorkoutExercises
            .FirstOrDefaultAsync(e => e.Id == exerciseId && e.DeletedAt == null);
        
        if (exercise is null) return null;

        exercise.ExerciseName = updated.ExerciseName;
        exercise.Sets = updated.Sets;
        exercise.Reps = updated.Reps;
        exercise.Weight = updated.Weight;
        exercise.Duration = updated.Duration;
        exercise.Notes = updated.Notes;
        exercise.OrderIndex = updated.OrderIndex;
        exercise.UpdatedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow);

        await db.SaveChangesAsync();
        return exercise;
    }
}
