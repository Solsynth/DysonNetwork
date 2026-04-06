using DysonNetwork.Shared.Proto;
using static DysonNetwork.Shared.Proto.DyFitnessVisibility;

namespace DysonNetwork.Shared.Registry;

public class RemoteFitnessService
{
    private readonly DyFitnessService.DyFitnessServiceClient _client;

    public RemoteFitnessService(DyFitnessService.DyFitnessServiceClient client)
    {
        _client = client;
    }

    public async Task<DyWorkout?> GetWorkoutAsync(Guid accountId, Guid workoutId)
    {
        try
        {
            return await _client.GetWorkoutAsync(new DyGetWorkoutRequest
            {
                AccountId = accountId.ToString(),
                WorkoutId = workoutId.ToString()
            });
        }
        catch
        {
            return null;
        }
    }

    public async Task<DyFitnessMetric?> GetMetricAsync(Guid accountId, Guid metricId)
    {
        try
        {
            return await _client.GetMetricAsync(new DyGetMetricRequest
            {
                AccountId = accountId.ToString(),
                MetricId = metricId.ToString()
            });
        }
        catch
        {
            return null;
        }
    }

    public async Task<DyFitnessGoal?> GetGoalAsync(Guid accountId, Guid goalId)
    {
        try
        {
            return await _client.GetGoalAsync(new DyGetGoalRequest
            {
                AccountId = accountId.ToString(),
                GoalId = goalId.ToString()
            });
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> ValidateAndGetOwnershipAsync(string fitnessType, Guid fitnessId, Guid userId)
    {
        return fitnessType.ToLowerInvariant() switch
        {
            "workout" => await ValidateWorkoutOwnershipAsync(fitnessId, userId),
            "metric" => await ValidateMetricOwnershipAsync(fitnessId, userId),
            "goal" => await ValidateGoalOwnershipAsync(fitnessId, userId),
            _ => false
        };
    }

    private async Task<bool> ValidateWorkoutOwnershipAsync(Guid workoutId, Guid userId)
    {
        var workout = await GetWorkoutAsync(userId, workoutId);
        if (workout is null) return false;
        if (workout.AccountId != userId.ToString()) return false;
        return workout.Visibility == DyFitnessVisibility.Public;
    }

    private async Task<bool> ValidateMetricOwnershipAsync(Guid metricId, Guid userId)
    {
        var metric = await GetMetricAsync(userId, metricId);
        if (metric is null) return false;
        if (metric.AccountId != userId.ToString()) return false;
        return metric.Visibility == DyFitnessVisibility.Public;
    }

    private async Task<bool> ValidateGoalOwnershipAsync(Guid goalId, Guid userId)
    {
        var goal = await GetGoalAsync(userId, goalId);
        if (goal is null) return false;
        if (goal.AccountId != userId.ToString()) return false;
        return goal.Visibility == DyFitnessVisibility.Public;
    }
}
