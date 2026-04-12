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
        workout.Visibility = updated.Visibility;
        workout.Meta = updated.Meta;
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

    public async Task<List<SnWorkout>> CreateWorkoutsBatchAsync(IEnumerable<SnWorkout> workouts)
    {
        var now = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow);
        var workoutList = workouts.Select(w =>
        {
            w.Id = Guid.NewGuid();
            w.CreatedAt = now;
            w.UpdatedAt = now;
            return w;
        }).ToList();

        var externalIds = workoutList
            .Where(w => !string.IsNullOrEmpty(w.ExternalId))
            .Select(w => w.ExternalId!)
            .ToList();

        var existingByExternalId = new Dictionary<string, SnWorkout>(StringComparer.OrdinalIgnoreCase);
        if (externalIds.Any())
        {
            var existing = await db.Workouts
                .Where(w => externalIds.Contains(w.ExternalId!))
                .ToListAsync();
            foreach (var e in existing)
            {
                if (e.ExternalId != null)
                    existingByExternalId[e.ExternalId] = e;
            }
        }

        var toInsert = new List<SnWorkout>();
        var toUpdate = new List<SnWorkout>();

        foreach (var workout in workoutList)
        {
            if (!string.IsNullOrEmpty(workout.ExternalId) && existingByExternalId.TryGetValue(workout.ExternalId, out var existing))
            {
                existing.Name = workout.Name;
                existing.Description = workout.Description;
                existing.Type = workout.Type;
                existing.StartTime = workout.StartTime;
                existing.EndTime = workout.EndTime;
                existing.Duration = workout.Duration;
                existing.CaloriesBurned = workout.CaloriesBurned;
                existing.Notes = workout.Notes;
                existing.Visibility = workout.Visibility;
                existing.Meta = workout.Meta;
                existing.UpdatedAt = now;
                toUpdate.Add(existing);
            }
            else
            {
                toInsert.Add(workout);
            }
        }

        var accountId = toInsert.FirstOrDefault()?.AccountId ?? toUpdate.FirstOrDefault()?.AccountId;

        if (toInsert.Any())
        {
            db.Workouts.AddRange(toInsert);
        }

        if (toUpdate.Any())
        {
            foreach (var workout in toUpdate)
            {
                db.Entry(workout).State = EntityState.Modified;
            }
        }

        if (toInsert.Any() || toUpdate.Any())
        {
            await db.SaveChangesAsync();
        }

        logger.LogInformation("Created {CreatedCount} workouts, updated {UpdatedCount} workouts in batch for account {AccountId}", 
            toInsert.Count, toUpdate.Count, accountId);
        return toInsert.Concat(toUpdate).ToList();
    }

    public async Task<int> UpdateWorkoutsVisibilityAsync(Guid accountId, IEnumerable<Guid> workoutIds, FitnessVisibility visibility)
    {
        var ids = workoutIds.ToList();
        var count = await db.Workouts
            .Where(w => w.AccountId == accountId && ids.Contains(w.Id) && w.DeletedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(w => w.Visibility, visibility)
                .SetProperty(w => w.UpdatedAt, NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow)));

        logger.LogInformation("Updated visibility to {Visibility} for {Count} workouts for account {AccountId}",
            visibility, count, accountId);
        return count;
    }
}
