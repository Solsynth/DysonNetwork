using DysonNetwork.Fitness.Goals;
using DysonNetwork.Fitness.Workouts;
using DysonNetwork.Fitness.Metrics;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Fitness.Goals;

public class GoalService(AppDatabase db, ILogger<GoalService> logger)
{
    public async Task<SnFitnessGoal?> GetGoalByIdAsync(Guid id)
    {
        return await db.FitnessGoals
            .FirstOrDefaultAsync(g => g.Id == id && g.DeletedAt == null);
    }

    public async Task<IEnumerable<SnFitnessGoal>> GetGoalsByAccountAsync(Guid accountId, FitnessGoalStatus? status = null, int skip = 0, int take = 20)
    {
        var query = db.FitnessGoals
            .Where(g => g.AccountId == accountId && g.DeletedAt == null)
            .AsQueryable();

        if (status.HasValue)
            query = query.Where(g => g.Status == status.Value);

        return await query
            .OrderByDescending(g => g.StartDate)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    public async Task<SnFitnessGoal> CreateGoalAsync(SnFitnessGoal goal)
    {
        db.FitnessGoals.Add(goal);
        await db.SaveChangesAsync();
        logger.LogInformation("Created goal {GoalId} of type {GoalType} for account {AccountId}", 
            goal.Id, goal.GoalType, goal.AccountId);
        
        if (goal.AutoUpdateProgress)
        {
            await UpdateGoalProgressFromDataAsync(goal.Id);
        }
        
        return goal;
    }

    public async Task<SnFitnessGoal?> UpdateGoalAsync(Guid id, SnFitnessGoal updated)
    {
        var goal = await db.FitnessGoals.FirstOrDefaultAsync(g => g.Id == id && g.DeletedAt == null);
        if (goal is null) return null;

        goal.Title = updated.Title;
        goal.Description = updated.Description;
        goal.GoalType = updated.GoalType;
        goal.TargetValue = updated.TargetValue;
        goal.CurrentValue = updated.CurrentValue;
        goal.Unit = updated.Unit;
        goal.StartDate = updated.StartDate;
        goal.EndDate = updated.EndDate;
        goal.Status = updated.Status;
        goal.Notes = updated.Notes;
        goal.BoundWorkoutType = updated.BoundWorkoutType;
        goal.BoundMetricType = updated.BoundMetricType;
        goal.AutoUpdateProgress = updated.AutoUpdateProgress;
        goal.UpdatedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow);

        await db.SaveChangesAsync();
        logger.LogInformation("Updated goal {GoalId}", id);
        return goal;
    }

    public async Task<SnFitnessGoal?> UpdateGoalProgressAsync(Guid id, decimal currentValue)
    {
        var goal = await db.FitnessGoals.FirstOrDefaultAsync(g => g.Id == id && g.DeletedAt == null);
        if (goal is null) return null;

        goal.CurrentValue = currentValue;
        
        // Auto-complete if target reached
        if (goal.TargetValue.HasValue && currentValue >= goal.TargetValue.Value && goal.Status == FitnessGoalStatus.Active)
        {
            goal.Status = FitnessGoalStatus.Completed;
            logger.LogInformation("Goal {GoalId} automatically marked as completed", id);
        }
        
        goal.UpdatedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow);
        await db.SaveChangesAsync();
        
        return goal;
    }

    public async Task<bool> DeleteGoalAsync(Guid id)
    {
        var goal = await db.FitnessGoals.FirstOrDefaultAsync(g => g.Id == id && g.DeletedAt == null);
        if (goal is null) return false;

        goal.DeletedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow);
        await db.SaveChangesAsync();
        logger.LogInformation("Soft deleted goal {GoalId}", id);
        return true;
    }

    public async Task<int> GetActiveGoalsCountAsync(Guid accountId)
    {
        return await db.FitnessGoals
            .CountAsync(g => g.AccountId == accountId && g.Status == FitnessGoalStatus.Active && g.DeletedAt == null);
    }

    public async Task UpdateGoalProgressFromDataAsync(Guid goalId)
    {
        var goal = await db.FitnessGoals.FirstOrDefaultAsync(g => g.Id == goalId && g.DeletedAt == null);
        if (goal is null || !goal.AutoUpdateProgress) return;

        decimal newValue = 0;

        if (goal.BoundWorkoutType.HasValue)
        {
            newValue = await db.Workouts
                .Where(w => w.AccountId == goal.AccountId 
                    && w.Type == goal.BoundWorkoutType.Value
                    && w.StartTime >= goal.StartDate
                    && (goal.EndDate == null || w.StartTime <= goal.EndDate)
                    && w.DeletedAt == null)
                .SumAsync(w => (int?)w.CaloriesBurned ?? 0);
            
            if (goal.Unit == "count")
            {
                newValue = await db.Workouts
                    .Where(w => w.AccountId == goal.AccountId 
                        && w.Type == goal.BoundWorkoutType.Value
                        && w.StartTime >= goal.StartDate
                        && (goal.EndDate == null || w.StartTime <= goal.EndDate)
                        && w.DeletedAt == null)
                    .CountAsync();
            }
        }
        else if (goal.BoundMetricType.HasValue)
        {
            newValue = await db.FitnessMetrics
                .Where(m => m.AccountId == goal.AccountId 
                    && m.MetricType == goal.BoundMetricType.Value
                    && m.RecordedAt >= goal.StartDate
                    && (goal.EndDate == null || m.RecordedAt <= goal.EndDate)
                    && m.DeletedAt == null)
                .SumAsync(m => m.Value);
        }

        if (newValue != goal.CurrentValue)
        {
            goal.CurrentValue = newValue;
            
            if (goal.TargetValue.HasValue && newValue >= goal.TargetValue.Value && goal.Status == FitnessGoalStatus.Active)
            {
                goal.Status = FitnessGoalStatus.Completed;
                logger.LogInformation("Goal {GoalId} automatically marked as completed (auto-update)", goalId);
            }
            
            goal.UpdatedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow);
            await db.SaveChangesAsync();
            logger.LogInformation("Auto-updated goal {GoalId} progress to {CurrentValue}", goalId, newValue);
        }
    }

    public async Task RecalculateAllGoalsForAccountAsync(Guid accountId)
    {
        var goals = await db.FitnessGoals
            .Where(g => g.AccountId == accountId && g.AutoUpdateProgress && g.DeletedAt == null)
            .ToListAsync();

        foreach (var goal in goals)
        {
            await UpdateGoalProgressFromDataAsync(goal.Id);
        }
    }
}
