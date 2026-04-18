using System.ComponentModel;
using System.Text.Json;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.SemanticKernel;

namespace DysonNetwork.Insight.MiChan.Plugins;

/// <summary>
/// Plugin for managing fitness data including workouts, metrics, and goals.
/// Allows MiChan to help users track and manage their fitness journey.
/// </summary>
public class FitnessPlugin(RemoteFitnessService fitnessService, ILogger<FitnessPlugin> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private DyFitnessService.DyFitnessServiceClient GetClient()
    {
        var client = fitnessService.GetType()
            .GetField("_client", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(fitnessService) as DyFitnessService.DyFitnessServiceClient;

        return client ?? throw new InvalidOperationException("Unable to access fitness service client");
    }

    [KernelFunction("get_workout")]
    [Description("Get a specific workout by ID for an account. Returns workout details including name, type, duration, calories burned, and other metrics.")]
    public async Task<string> GetWorkout(
        [Description("The account ID of the user")] string accountId,
        [Description("The ID of the workout to retrieve")] string workoutId
    )
    {
        try
        {
            if (!Guid.TryParse(accountId, out _) || !Guid.TryParse(workoutId, out _))
            {
                return JsonSerializer.Serialize(new { error = "Invalid account ID or workout ID format" }, JsonOptions);
            }

            var workout = await fitnessService.GetWorkoutAsync(Guid.Parse(accountId), Guid.Parse(workoutId));

            if (workout == null)
            {
                return JsonSerializer.Serialize(new { error = "Workout not found" }, JsonOptions);
            }

            return JsonSerializer.Serialize(new { success = true, workout }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get workout {WorkoutId} for account {AccountId}", workoutId, accountId);
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }
    }

    [KernelFunction("get_metric")]
    [Description("Get a specific fitness metric by ID for an account. Returns metric details including type, value, unit, and recorded time.")]
    public async Task<string> GetMetric(
        [Description("The account ID of the user")] string accountId,
        [Description("The ID of the metric to retrieve")] string metricId
    )
    {
        try
        {
            if (!Guid.TryParse(accountId, out _) || !Guid.TryParse(metricId, out _))
            {
                return JsonSerializer.Serialize(new { error = "Invalid account ID or metric ID format" }, JsonOptions);
            }

            var metric = await fitnessService.GetMetricAsync(Guid.Parse(accountId), Guid.Parse(metricId));

            if (metric == null)
            {
                return JsonSerializer.Serialize(new { error = "Metric not found" }, JsonOptions);
            }

            return JsonSerializer.Serialize(new { success = true, metric }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get metric {MetricId} for account {AccountId}", metricId, accountId);
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }
    }

    [KernelFunction("get_goal")]
    [Description("Get a specific fitness goal by ID for an account. Returns goal details including title, target value, current progress, status, and deadlines.")]
    public async Task<string> GetGoal(
        [Description("The account ID of the user")] string accountId,
        [Description("The ID of the goal to retrieve")] string goalId
    )
    {
        try
        {
            if (!Guid.TryParse(accountId, out _) || !Guid.TryParse(goalId, out _))
            {
                return JsonSerializer.Serialize(new { error = "Invalid account ID or goal ID format" }, JsonOptions);
            }

            var goal = await fitnessService.GetGoalAsync(Guid.Parse(accountId), Guid.Parse(goalId));

            if (goal == null)
            {
                return JsonSerializer.Serialize(new { error = "Goal not found" }, JsonOptions);
            }

            return JsonSerializer.Serialize(new { success = true, goal }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get goal {GoalId} for account {AccountId}", goalId, accountId);
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }
    }

    [KernelFunction("list_workouts")]
    [Description("List workouts for an account with pagination. Returns a list of workouts sorted by start time.")]
    public async Task<string> ListWorkouts(
        [Description("The account ID of the user")] string accountId,
        [Description("Number of workouts to skip (for pagination)")] int skip = 0,
        [Description("Maximum number of workouts to return")] int take = 20
    )
    {
        try
        {
            if (!Guid.TryParse(accountId, out _))
            {
                return JsonSerializer.Serialize(new { error = "Invalid account ID format" }, JsonOptions);
            }

            var client = GetClient();

            var response = await client.ListWorkoutsAsync(new DyListWorkoutsRequest
            {
                AccountId = accountId,
                Skip = skip,
                Take = take
            });

            return JsonSerializer.Serialize(new { success = true, total = response.Total, workouts = response.Workouts }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list workouts for account {AccountId}", accountId);
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }
    }

    [KernelFunction("list_metrics")]
    [Description("List fitness metrics for an account with optional type filtering and pagination.")]
    public async Task<string> ListMetrics(
        [Description("The account ID of the user")] string accountId,
        [Description("Optional metric type filter (e.g., Weight, Steps, HeartRate, Calories, etc.)")] string? metricType = null,
        [Description("Number of metrics to skip (for pagination)")] int skip = 0,
        [Description("Maximum number of metrics to return")] int take = 50
    )
    {
        try
        {
            if (!Guid.TryParse(accountId, out _))
            {
                return JsonSerializer.Serialize(new { error = "Invalid account ID format" }, JsonOptions);
            }

            var client = GetClient();

            var request = new DyListMetricsRequest
            {
                AccountId = accountId,
                Skip = skip,
                Take = take
            };

            if (!string.IsNullOrEmpty(metricType) && Enum.TryParse<DyFitnessMetricType>(metricType, true, out var type))
            {
                request.Type = type;
            }

            var response = await client.ListMetricsAsync(request);

            return JsonSerializer.Serialize(new { success = true, total = response.Total, metrics = response.Metrics }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list metrics for account {AccountId}", accountId);
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }
    }

    [KernelFunction("list_goals")]
    [Description("List fitness goals for an account with optional status filtering and pagination.")]
    public async Task<string> ListGoals(
        [Description("The account ID of the user")] string accountId,
        [Description("Optional status filter (Active, Completed, Paused, Cancelled)")] string? status = null,
        [Description("Number of goals to skip (for pagination)")] int skip = 0,
        [Description("Maximum number of goals to return")] int take = 20
    )
    {
        try
        {
            if (!Guid.TryParse(accountId, out _))
            {
                return JsonSerializer.Serialize(new { error = "Invalid account ID format" }, JsonOptions);
            }

            var client = GetClient();

            var request = new DyListGoalsRequest
            {
                AccountId = accountId,
                Skip = skip,
                Take = take
            };

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<DyFitnessGoalStatus>(status, true, out var goalStatus))
            {
                request.Status = goalStatus;
            }

            var response = await client.ListGoalsAsync(request);

            return JsonSerializer.Serialize(new { success = true, total = response.Total, goals = response.Goals }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list goals for account {AccountId}", accountId);
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }
    }

    [KernelFunction("get_latest_metrics")]
    [Description("Get the latest metrics of each type for an account. Useful for getting current fitness snapshot.")]
    public async Task<string> GetLatestMetrics(
        [Description("The account ID of the user")] string accountId
    )
    {
        try
        {
            if (!Guid.TryParse(accountId, out _))
            {
                return JsonSerializer.Serialize(new { error = "Invalid account ID format" }, JsonOptions);
            }

            var client = GetClient();

            var response = await client.GetLatestMetricsAsync(new DyGetLatestMetricsRequest
            {
                AccountId = accountId
            });

            var metrics = response.Metrics.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value
            );

            return JsonSerializer.Serialize(new { success = true, metrics }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get latest metrics for account {AccountId}", accountId);
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }
    }

    [KernelFunction("get_goal_stats")]
    [Description("Get statistics about a user's goals including active and completed counts.")]
    public async Task<string> GetGoalStats(
        [Description("The account ID of the user")] string accountId
    )
    {
        try
        {
            if (!Guid.TryParse(accountId, out _))
            {
                return JsonSerializer.Serialize(new { error = "Invalid account ID format" }, JsonOptions);
            }

            var client = GetClient();

            var response = await client.GetGoalStatsAsync(new DyGetGoalStatsRequest
            {
                AccountId = accountId
            });

            return JsonSerializer.Serialize(new { success = true, activeCount = response.ActiveCount, completedCount = response.CompletedCount }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get goal stats for account {AccountId}", accountId);
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }
    }

    [KernelFunction("get_fitness_summary")]
    [Description("Get a summary of all fitness data for an account including counts of workouts, metrics, and goals.")]
    public async Task<string> GetFitnessSummary(
        [Description("The account ID of the user")] string accountId
    )
    {
        try
        {
            if (!Guid.TryParse(accountId, out _))
            {
                return JsonSerializer.Serialize(new { error = "Invalid account ID format" }, JsonOptions);
            }

            var client = GetClient();

            var response = await client.GetFitnessAccountDataAsync(new DyGetFitnessAccountDataRequest
            {
                AccountId = accountId
            });

            return JsonSerializer.Serialize(new
            {
                success = true,
                workoutsCount = response.WorkoutsCount,
                metricsCount = response.MetricsCount,
                goalsCount = response.GoalsCount,
                totalCount = response.TotalCount
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get fitness summary for account {AccountId}", accountId);
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }
    }

    [KernelFunction("create_workout")]
    [Description("Create a new workout for an account. Returns the created workout details.")]
    public async Task<string> CreateWorkout(
        [Description("The account ID of the user")] string accountId,
        [Description("The name of the workout")] string name,
        [Description("The type of workout (Strength, Cardio, Flexibility, Mixed, Other)")] string type,
        [Description("The start time of the workout (ISO 8601 format)")] string startTime,
        [Description("Optional end time of the workout (ISO 8601 format)")] string? endTime = null,
        [Description("Optional duration in ISO 8601 duration format (e.g., PT1H30M)")] string? duration = null,
        [Description("Optional calories burned")] int? caloriesBurned = null,
        [Description("Optional notes about the workout")] string? notes = null,
        [Description("Visibility (Private or Public)")] string visibility = "Private",
        [Description("Optional distance traveled")] double? distance = null,
        [Description("Optional distance unit (e.g., km, miles)")] string? distanceUnit = null,
        [Description("Optional average heart rate")] int? averageHeartRate = null,
        [Description("Optional max heart rate")] int? maxHeartRate = null
    )
    {
        try
        {
            if (!Guid.TryParse(accountId, out _))
            {
                return JsonSerializer.Serialize(new { error = "Invalid account ID format" }, JsonOptions);
            }

            if (!Enum.TryParse<DyWorkoutType>(type, true, out var workoutType))
            {
                return JsonSerializer.Serialize(new { error = $"Invalid workout type: {type}. Valid types: Strength, Cardio, Flexibility, Mixed, Other" }, JsonOptions);
            }

            if (!DateTime.TryParse(startTime, out var startDateTime))
            {
                return JsonSerializer.Serialize(new { error = "Invalid start time format. Use ISO 8601 format." }, JsonOptions);
            }

            if (!Enum.TryParse<DyFitnessVisibility>(visibility, true, out var visibilityType))
            {
                visibilityType = DyFitnessVisibility.Private;
            }

            var client = GetClient();

            var request = new DyCreateWorkoutRequest
            {
                AccountId = accountId,
                Name = name,
                Type = workoutType,
                StartTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(startDateTime.ToUniversalTime()),
                Visibility = visibilityType
            };

            if (endTime != null && DateTime.TryParse(endTime, out var endDateTime))
            {
                request.EndTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(endDateTime.ToUniversalTime());
            }

            if (!string.IsNullOrEmpty(duration))
            {
                request.Duration = duration;
            }

            if (caloriesBurned.HasValue)
            {
                request.CaloriesBurned = caloriesBurned.Value;
            }

            if (!string.IsNullOrEmpty(notes))
            {
                request.Notes = notes;
            }

            if (distance.HasValue)
            {
                request.Distance = distance.Value;
            }

            if (!string.IsNullOrEmpty(distanceUnit))
            {
                request.DistanceUnit = distanceUnit;
            }

            if (averageHeartRate.HasValue)
            {
                request.AverageHeartRate = averageHeartRate.Value;
            }

            if (maxHeartRate.HasValue)
            {
                request.MaxHeartRate = maxHeartRate.Value;
            }

            var response = await client.CreateWorkoutAsync(request);

            return JsonSerializer.Serialize(new { success = true, workout = response }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create workout for account {AccountId}", accountId);
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }
    }

    [KernelFunction("create_metric")]
    [Description("Create a new fitness metric for an account. Returns the created metric details.")]
    public async Task<string> CreateMetric(
        [Description("The account ID of the user")] string accountId,
        [Description("The type of metric (Weight, Steps, HeartRate, Calories, Distance, Duration, etc.)")] string metricType,
        [Description("The value of the metric")] double value,
        [Description("The unit of measurement (e.g., kg, lbs, steps, km, miles, minutes)")] string unit,
        [Description("The time the metric was recorded (ISO 8601 format)")] string? recordedAt = null,
        [Description("Optional notes about the metric")] string? notes = null,
        [Description("Visibility (Private or Public)")] string visibility = "Private"
    )
    {
        try
        {
            if (!Guid.TryParse(accountId, out _))
            {
                return JsonSerializer.Serialize(new { error = "Invalid account ID format" }, JsonOptions);
            }

            if (!Enum.TryParse<DyFitnessMetricType>(metricType, true, out var type))
            {
                var validTypes = string.Join(", ", Enum.GetNames<DyFitnessMetricType>());
                return JsonSerializer.Serialize(new { error = $"Invalid metric type: {metricType}. Valid types: {validTypes}" }, JsonOptions);
            }

            if (!Enum.TryParse<DyFitnessVisibility>(visibility, true, out var visibilityType))
            {
                visibilityType = DyFitnessVisibility.Private;
            }

            var client = GetClient();

            var request = new DyCreateMetricRequest
            {
                AccountId = accountId,
                MetricType = type,
                Value = value,
                Unit = unit,
                Visibility = visibilityType
            };

            if (!string.IsNullOrEmpty(recordedAt) && DateTime.TryParse(recordedAt, out var recordedDateTime))
            {
                request.RecordedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(recordedDateTime.ToUniversalTime());
            }
            else
            {
                request.RecordedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow);
            }

            if (!string.IsNullOrEmpty(notes))
            {
                request.Notes = notes;
            }

            var response = await client.CreateMetricAsync(request);

            return JsonSerializer.Serialize(new { success = true, metric = response }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create metric for account {AccountId}", accountId);
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }
    }

    [KernelFunction("update_goal_progress")]
    [Description("Update the progress of a fitness goal. Returns the updated goal details.")]
    public async Task<string> UpdateGoalProgress(
        [Description("The account ID of the user")] string accountId,
        [Description("The ID of the goal to update")] string goalId,
        [Description("The new current value/progress")] double currentValue
    )
    {
        try
        {
            if (!Guid.TryParse(accountId, out _) || !Guid.TryParse(goalId, out _))
            {
                return JsonSerializer.Serialize(new { error = "Invalid account ID or goal ID format" }, JsonOptions);
            }

            var client = GetClient();

            var response = await client.UpdateGoalProgressAsync(new DyUpdateGoalProgressRequest
            {
                AccountId = accountId,
                GoalId = goalId,
                CurrentValue = currentValue
            });

            return JsonSerializer.Serialize(new { success = true, goal = response }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update goal progress for goal {GoalId}", goalId);
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }
    }
}
