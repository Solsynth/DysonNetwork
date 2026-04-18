using DysonNetwork.Shared.Proto;
using Grpc.Core;
using DysonNetwork.Fitness.Workouts;
using DysonNetwork.Fitness.Metrics;
using DysonNetwork.Fitness.Goals;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Text;
using System.Text.Json;

namespace DysonNetwork.Fitness;

public class FitnessGrpcService : DyFitnessService.DyFitnessServiceBase
{
    private readonly WorkoutService _workoutService;
    private readonly MetricService _metricService;
    private readonly GoalService _goalService;
    private readonly AppDatabase _db;
    private readonly ILogger<FitnessGrpcService> _logger;

    public FitnessGrpcService(
        WorkoutService workoutService,
        MetricService metricService,
        GoalService goalService,
        AppDatabase db,
        ILogger<FitnessGrpcService> logger)
    {
        _workoutService = workoutService;
        _metricService = metricService;
        _goalService = goalService;
        _db = db;
        _logger = logger;
    }

    // Account
    public override async Task<DyFitnessAccountDataSummary> GetFitnessAccountData(DyGetFitnessAccountDataRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);

        var workoutCount = await _db.Workouts.CountAsync(w => w.AccountId == accountId && w.DeletedAt == null);
        var metricCount = await _db.FitnessMetrics.CountAsync(m => m.AccountId == accountId && m.DeletedAt == null);
        var goalCount = await _db.FitnessGoals.CountAsync(g => g.AccountId == accountId && g.DeletedAt == null);

        return new DyFitnessAccountDataSummary
        {
            WorkoutsCount = workoutCount,
            MetricsCount = metricCount,
            GoalsCount = goalCount,
            TotalCount = workoutCount + metricCount + goalCount
        };
    }

    public override async Task<DyDeleteFitnessAccountDataResponse> DeleteFitnessAccountData(DyDeleteFitnessAccountDataRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);

        var deletedWorkouts = await _db.Workouts.Where(w => w.AccountId == accountId).ExecuteDeleteAsync();
        var deletedMetrics = await _db.FitnessMetrics.Where(m => m.AccountId == accountId).ExecuteDeleteAsync();
        var deletedGoals = await _db.FitnessGoals.Where(g => g.AccountId == accountId).ExecuteDeleteAsync();

        _logger.LogInformation("Permanently deleted fitness data for account {AccountId}. Workouts: {Workouts}, Metrics: {Metrics}, Goals: {Goals}",
            accountId, deletedWorkouts, deletedMetrics, deletedGoals);

        return new DyDeleteFitnessAccountDataResponse
        {
            Message = "All fitness data permanently deleted",
            DeletedWorkouts = deletedWorkouts,
            DeletedMetrics = deletedMetrics,
            DeletedGoals = deletedGoals,
            DeletedExercises = 0
        };
    }

    // Workouts
    public override async Task<DyListWorkoutsResponse> ListWorkouts(DyListWorkoutsRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var workouts = await _workoutService.GetWorkoutsByAccountAsync(accountId, request.Skip, request.Take);

        return new DyListWorkoutsResponse
        {
            Workouts = { workouts.Select(ToDyWorkout) },
            Total = workouts.Count()
        };
    }

    public override async Task<DyWorkout> GetWorkout(DyGetWorkoutRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var workoutId = Guid.Parse(request.WorkoutId);

        var workout = await _workoutService.GetWorkoutByIdAsync(workoutId);
        if (workout is null || workout.AccountId != accountId)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Workout not found"));
        }

        return ToDyWorkout(workout);
    }

    public override async Task<DyWorkout> CreateWorkout(DyCreateWorkoutRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);

        var workout = new SnWorkout
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Name = request.Name,
            Description = request.Description,
            Type = (WorkoutType)request.Type,
            StartTime = Instant.FromDateTimeUtc(request.StartTime.ToDateTime()),
            EndTime = request.EndTime?.ToDateTime() != null ? Instant.FromDateTimeUtc(request.EndTime.ToDateTime()) : null,
            Duration = request.HasDuration ? ParseDuration(request.Duration) : null,
            CaloriesBurned = request.HasCaloriesBurned ? request.CaloriesBurned : null,
            Notes = request.HasNotes ? request.Notes : null,
            ExternalId = request.HasExternalId ? request.ExternalId : null,
            Visibility = (FitnessVisibility)request.Visibility,
            Meta = request.HasMeta ? JsonDocument.Parse(request.Meta) : null,
            Distance = request.HasDistance ? (decimal)request.Distance : null,
            DistanceUnit = request.HasDistanceUnit ? request.DistanceUnit : null,
            AverageSpeed = request.HasAverageSpeed ? (decimal)request.AverageSpeed : null,
            AverageHeartRate = request.HasAverageHeartRate ? request.AverageHeartRate : null,
            MaxHeartRate = request.HasMaxHeartRate ? request.MaxHeartRate : null,
            ElevationGain = request.HasElevationGain ? (decimal)request.ElevationGain : null,
            MaxSpeed = request.HasMaxSpeed ? (decimal)request.MaxSpeed : null,
            CreatedAt = now,
            UpdatedAt = now
        };

        var created = await _workoutService.CreateWorkoutAsync(workout);
        return ToDyWorkout(created);
    }

    public override async Task<DyWorkout> UpdateWorkout(DyUpdateWorkoutRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var workoutId = Guid.Parse(request.WorkoutId);

        var existing = await _workoutService.GetWorkoutByIdAsync(workoutId);
        if (existing is null || existing.AccountId != accountId)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Workout not found"));
        }

        var updated = new SnWorkout
        {
            Id = workoutId,
            AccountId = accountId,
            Name = request.HasName ? request.Name : existing.Name,
            Description = request.HasDescription ? request.Description : existing.Description,
            Type = request.HasType ? (WorkoutType)request.Type : existing.Type,
            StartTime = request.StartTime != null ? Instant.FromDateTimeUtc(request.StartTime.ToDateTime()) : existing.StartTime,
            EndTime = request.EndTime != null ? Instant.FromDateTimeUtc(request.EndTime.ToDateTime()) : existing.EndTime,
            Duration = request.HasDuration ? ParseDuration(request.Duration) : existing.Duration,
            CaloriesBurned = request.HasCaloriesBurned ? request.CaloriesBurned : existing.CaloriesBurned,
            Notes = request.HasNotes ? request.Notes : existing.Notes,
            ExternalId = request.HasExternalId ? request.ExternalId : existing.ExternalId,
            Visibility = request.HasVisibility ? (FitnessVisibility)request.Visibility : existing.Visibility,
            Meta = request.HasMeta ? JsonDocument.Parse(request.Meta) : existing.Meta,
            Distance = request.HasDistance ? (decimal)request.Distance : existing.Distance,
            DistanceUnit = request.HasDistanceUnit ? request.DistanceUnit : existing.DistanceUnit,
            AverageSpeed = request.HasAverageSpeed ? (decimal)request.AverageSpeed : existing.AverageSpeed,
            AverageHeartRate = request.HasAverageHeartRate ? request.AverageHeartRate : existing.AverageHeartRate,
            MaxHeartRate = request.HasMaxHeartRate ? request.MaxHeartRate : existing.MaxHeartRate,
            ElevationGain = request.HasElevationGain ? (decimal)request.ElevationGain : existing.ElevationGain,
            MaxSpeed = request.HasMaxSpeed ? (decimal)request.MaxSpeed : existing.MaxSpeed,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
        };

        var result = await _workoutService.UpdateWorkoutAsync(workoutId, updated);
        if (result is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Workout not found"));
        }

        return ToDyWorkout(result);
    }

    public override async Task<DyDeleteWorkoutResponse> DeleteWorkout(DyDeleteWorkoutRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var workoutId = Guid.Parse(request.WorkoutId);

        var workout = await _workoutService.GetWorkoutByIdAsync(workoutId);
        if (workout is null || workout.AccountId != accountId)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Workout not found"));
        }

        var success = await _workoutService.DeleteWorkoutAsync(workoutId);
        return new DyDeleteWorkoutResponse { Success = success };
    }

    public override async Task<DyBatchCreateWorkoutsResponse> BatchCreateWorkouts(DyBatchCreateWorkoutsRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);

        var workouts = request.Workouts.Select(r => new SnWorkout
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Name = r.Name,
            Description = r.Description,
            Type = (WorkoutType)r.Type,
            StartTime = Instant.FromDateTimeUtc(r.StartTime.ToDateTime()),
            EndTime = r.EndTime?.ToDateTime() != null ? Instant.FromDateTimeUtc(r.EndTime.ToDateTime()) : null,
            Duration = r.HasDuration ? ParseDuration(r.Duration) : null,
            CaloriesBurned = r.HasCaloriesBurned ? r.CaloriesBurned : null,
            Notes = r.HasNotes ? r.Notes : null,
            ExternalId = r.HasExternalId ? r.ExternalId : null,
            Visibility = (FitnessVisibility)r.Visibility,
            Meta = r.HasMeta ? JsonDocument.Parse(r.Meta) : null,
            Distance = r.HasDistance ? (decimal)r.Distance : null,
            DistanceUnit = r.HasDistanceUnit ? r.DistanceUnit : null,
            AverageSpeed = r.HasAverageSpeed ? (decimal)r.AverageSpeed : null,
            AverageHeartRate = r.HasAverageHeartRate ? r.AverageHeartRate : null,
            MaxHeartRate = r.HasMaxHeartRate ? r.MaxHeartRate : null,
            ElevationGain = r.HasElevationGain ? (decimal)r.ElevationGain : null,
            MaxSpeed = r.HasMaxSpeed ? (decimal)r.MaxSpeed : null,
            CreatedAt = now,
            UpdatedAt = now
        }).ToList();

        var result = await _workoutService.CreateWorkoutsBatchAsync(workouts);

        return new DyBatchCreateWorkoutsResponse
        {
            Workouts = { result.Select(ToDyWorkout) },
            CreatedCount = result.Count,
            UpdatedCount = 0
        };
    }

    public override async Task<DyUpdateVisibilityResponse> UpdateWorkoutsVisibility(DyUpdateWorkoutsVisibilityRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var workoutIds = request.WorkoutIds.Select(Guid.Parse);
        var visibility = (FitnessVisibility)request.Visibility;

        var count = await _workoutService.UpdateWorkoutsVisibilityAsync(accountId, workoutIds, visibility);

        return new DyUpdateVisibilityResponse
        {
            Success = count > 0,
            UpdatedCount = count
        };
    }

    // Metrics
    public override async Task<DyListMetricsResponse> ListMetrics(DyListMetricsRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        FitnessMetricType? metricType = null;
        if (request.HasType)
        {
            metricType = (FitnessMetricType)request.Type;
        }
        var metrics = await _metricService.GetMetricsByAccountAsync(accountId, metricType, request.Skip, request.Take);

        return new DyListMetricsResponse
        {
            Metrics = { metrics.Select(ToDyFitnessMetric) },
            Total = metrics.Count()
        };
    }

    public override async Task<DyGetLatestMetricsResponse> GetLatestMetrics(DyGetLatestMetricsRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var latestMetrics = await _metricService.GetLatestMetricsByTypeAsync(accountId);

        var response = new DyGetLatestMetricsResponse();
        foreach (var kvp in latestMetrics)
        {
            response.Metrics[kvp.Key.ToString()] = ToDyFitnessMetric(kvp.Value);
        }

        return response;
    }

    public override async Task<DyFitnessMetric> GetMetric(DyGetMetricRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var metricId = Guid.Parse(request.MetricId);

        var metric = await _metricService.GetMetricByIdAsync(metricId);
        if (metric is null || metric.AccountId != accountId)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Metric not found"));
        }

        return ToDyFitnessMetric(metric);
    }

    public override async Task<DyFitnessMetric> CreateMetric(DyCreateMetricRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);

        var metric = new SnFitnessMetric
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            MetricType = (FitnessMetricType)request.MetricType,
            Value = (decimal)request.Value,
            Unit = request.Unit,
            RecordedAt = Instant.FromDateTimeUtc(request.RecordedAt.ToDateTime()),
            Notes = request.HasNotes ? request.Notes : null,
            Source = request.HasSource ? request.Source : null,
            ExternalId = request.HasExternalId ? request.ExternalId : null,
            Visibility = (FitnessVisibility)request.Visibility,
            CreatedAt = now,
            UpdatedAt = now
        };

        var created = await _metricService.CreateMetricAsync(metric);
        return ToDyFitnessMetric(created);
    }

    public override async Task<DyFitnessMetric> UpdateMetric(DyUpdateMetricRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var metricId = Guid.Parse(request.MetricId);

        var existing = await _metricService.GetMetricByIdAsync(metricId);
        if (existing is null || existing.AccountId != accountId)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Metric not found"));
        }

        var updated = new SnFitnessMetric
        {
            Id = metricId,
            AccountId = accountId,
            MetricType = request.HasMetricType ? (FitnessMetricType)request.MetricType : existing.MetricType,
            Value = request.HasValue ? (decimal)request.Value : existing.Value,
            Unit = request.HasUnit ? request.Unit : existing.Unit,
            RecordedAt = request.RecordedAt != null ? Instant.FromDateTimeUtc(request.RecordedAt.ToDateTime()) : existing.RecordedAt,
            Notes = request.HasNotes ? request.Notes : existing.Notes,
            Source = request.HasSource ? request.Source : existing.Source,
            ExternalId = request.HasExternalId ? request.ExternalId : existing.ExternalId,
            Visibility = request.HasVisibility ? (FitnessVisibility)request.Visibility : existing.Visibility,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
        };

        var result = await _metricService.UpdateMetricAsync(metricId, updated);
        if (result is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Metric not found"));
        }

        return ToDyFitnessMetric(result);
    }

    public override async Task<DyDeleteMetricResponse> DeleteMetric(DyDeleteMetricRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var metricId = Guid.Parse(request.MetricId);

        var metric = await _metricService.GetMetricByIdAsync(metricId);
        if (metric is null || metric.AccountId != accountId)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Metric not found"));
        }

        var success = await _metricService.DeleteMetricAsync(metricId);
        return new DyDeleteMetricResponse { Success = success };
    }

    public override async Task<DyBatchCreateMetricsResponse> BatchCreateMetrics(DyBatchCreateMetricsRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);

        var metrics = request.Metrics.Select(r => new SnFitnessMetric
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            MetricType = (FitnessMetricType)r.MetricType,
            Value = (decimal)r.Value,
            Unit = r.Unit,
            RecordedAt = Instant.FromDateTimeUtc(r.RecordedAt.ToDateTime()),
            Notes = r.HasNotes ? r.Notes : null,
            Source = r.HasSource ? r.Source : null,
            ExternalId = r.HasExternalId ? r.ExternalId : null,
            Visibility = (FitnessVisibility)r.Visibility,
            CreatedAt = now,
            UpdatedAt = now
        }).ToList();

        var result = await _metricService.CreateMetricsBatchAsync(metrics);

        return new DyBatchCreateMetricsResponse
        {
            Metrics = { result.Select(ToDyFitnessMetric) },
            CreatedCount = result.Count,
            UpdatedCount = 0
        };
    }

    public override async Task<DyUpdateVisibilityResponse> UpdateMetricsVisibility(DyUpdateMetricsVisibilityRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var metricIds = request.MetricIds.Select(Guid.Parse);
        var visibility = (FitnessVisibility)request.Visibility;

        var count = await _metricService.UpdateMetricsVisibilityAsync(accountId, metricIds, visibility);

        return new DyUpdateVisibilityResponse
        {
            Success = count > 0,
            UpdatedCount = count
        };
    }

    // Goals
    public override async Task<DyListGoalsResponse> ListGoals(DyListGoalsRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        FitnessGoalStatus? status = null;
        if (request.HasStatus)
        {
            status = (FitnessGoalStatus)request.Status;
        }
        var goals = await _goalService.GetGoalsByAccountAsync(accountId, status, request.Skip, request.Take);

        return new DyListGoalsResponse
        {
            Goals = { goals.Select(ToDyFitnessGoal) },
            Total = goals.Count()
        };
    }

    public override async Task<DyFitnessGoal> GetGoal(DyGetGoalRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var goalId = Guid.Parse(request.GoalId);

        var goal = await _goalService.GetGoalByIdAsync(goalId);
        if (goal is null || goal.AccountId != accountId)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Goal not found"));
        }

        return ToDyFitnessGoal(goal);
    }

    public override async Task<DyGoalStats> GetGoalStats(DyGetGoalStatsRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var activeCount = await _goalService.GetActiveGoalsCountAsync(accountId);
        var completedCount = await _goalService.GetCompletedGoalsCountAsync(accountId);

        return new DyGoalStats
        {
            ActiveCount = activeCount,
            CompletedCount = completedCount
        };
    }

    public override async Task<DyGetGoalHistoryResponse> GetGoalHistory(DyGetGoalHistoryRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var goalId = Guid.Parse(request.GoalId);

        var goal = await _goalService.GetGoalByIdAsync(goalId);
        if (goal is null || goal.AccountId != accountId)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Goal not found"));
        }

        var history = await _goalService.GetGoalHistoryAsync(goalId);

        return new DyGetGoalHistoryResponse
        {
            Goals = { history.Select(ToDyFitnessGoal) }
        };
    }

    public override async Task<DyFitnessGoal> CreateGoal(DyCreateGoalRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);

        var goal = new SnFitnessGoal
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            GoalType = (FitnessGoalType)request.GoalType,
            Title = request.Title,
            Description = request.HasDescription ? request.Description : null,
            TargetValue = request.HasTargetValue ? (decimal)request.TargetValue : null,
            CurrentValue = request.HasCurrentValue ? (decimal)request.CurrentValue : 0,
            Unit = request.HasUnit ? request.Unit : null,
            StartDate = Instant.FromDateTimeUtc(request.StartDate.ToDateTime()),
            EndDate = request.EndDate?.ToDateTime() != null ? Instant.FromDateTimeUtc(request.EndDate.ToDateTime()) : null,
            Status = (FitnessGoalStatus)request.Status,
            Notes = request.HasNotes ? request.Notes : null,
            Visibility = (FitnessVisibility)request.Visibility,
            BoundWorkoutType = request.HasBoundWorkoutType ? (WorkoutType)request.BoundWorkoutType : null,
            BoundMetricType = request.HasBoundMetricType ? (FitnessMetricType)request.BoundMetricType : null,
            AutoUpdateProgress = request.AutoUpdateProgress,
            RepeatType = (RepeatType)request.RepeatType,
            RepeatInterval = request.RepeatInterval,
            RepeatCount = request.HasRepeatCount ? request.RepeatCount : null,
            CurrentRepetition = request.CurrentRepetition,
            ParentGoalId = request.HasParentGoalId ? Guid.Parse(request.ParentGoalId) : null,
            CreatedAt = now,
            UpdatedAt = now
        };

        var created = await _goalService.CreateGoalAsync(goal);
        return ToDyFitnessGoal(created);
    }

    public override async Task<DyFitnessGoal> UpdateGoal(DyUpdateGoalRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var goalId = Guid.Parse(request.GoalId);

        var existing = await _goalService.GetGoalByIdAsync(goalId);
        if (existing is null || existing.AccountId != accountId)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Goal not found"));
        }

        var updated = new SnFitnessGoal
        {
            Id = goalId,
            AccountId = accountId,
            GoalType = request.HasGoalType ? (FitnessGoalType)request.GoalType : existing.GoalType,
            Title = request.HasTitle ? request.Title : existing.Title,
            Description = request.HasDescription ? request.Description : existing.Description,
            TargetValue = request.HasTargetValue ? (decimal)request.TargetValue : existing.TargetValue,
            CurrentValue = request.HasCurrentValue ? (decimal)request.CurrentValue : existing.CurrentValue,
            Unit = request.HasUnit ? request.Unit : existing.Unit,
            StartDate = request.StartDate != null ? Instant.FromDateTimeUtc(request.StartDate.ToDateTime()) : existing.StartDate,
            EndDate = request.EndDate != null ? Instant.FromDateTimeUtc(request.EndDate.ToDateTime()) : existing.EndDate,
            Status = request.HasStatus ? (FitnessGoalStatus)request.Status : existing.Status,
            Notes = request.HasNotes ? request.Notes : existing.Notes,
            Visibility = request.HasVisibility ? (FitnessVisibility)request.Visibility : existing.Visibility,
            BoundWorkoutType = request.HasBoundWorkoutType ? (WorkoutType)request.BoundWorkoutType : existing.BoundWorkoutType,
            BoundMetricType = request.HasBoundMetricType ? (FitnessMetricType)request.BoundMetricType : existing.BoundMetricType,
            AutoUpdateProgress = request.HasAutoUpdateProgress ? request.AutoUpdateProgress : existing.AutoUpdateProgress,
            RepeatType = request.HasRepeatType ? (RepeatType)request.RepeatType : existing.RepeatType,
            RepeatInterval = request.HasRepeatInterval ? request.RepeatInterval : existing.RepeatInterval,
            RepeatCount = request.HasRepeatCount ? request.RepeatCount : existing.RepeatCount,
            CurrentRepetition = request.HasCurrentRepetition ? request.CurrentRepetition : existing.CurrentRepetition,
            ParentGoalId = request.HasParentGoalId ? Guid.Parse(request.ParentGoalId) : existing.ParentGoalId,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
        };

        var result = await _goalService.UpdateGoalAsync(goalId, updated);
        if (result is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Goal not found"));
        }

        return ToDyFitnessGoal(result);
    }

    public override async Task<DyFitnessGoal> UpdateGoalProgress(DyUpdateGoalProgressRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var goalId = Guid.Parse(request.GoalId);

        var goal = await _goalService.GetGoalByIdAsync(goalId);
        if (goal is null || goal.AccountId != accountId)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Goal not found"));
        }

        var result = await _goalService.UpdateGoalProgressAsync(goalId, (decimal)request.CurrentValue);
        if (result is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Goal not found"));
        }

        return ToDyFitnessGoal(result);
    }

    public override async Task<DyDeleteGoalResponse> DeleteGoal(DyDeleteGoalRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var goalId = Guid.Parse(request.GoalId);

        var goal = await _goalService.GetGoalByIdAsync(goalId);
        if (goal is null || goal.AccountId != accountId)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Goal not found"));
        }

        var success = await _goalService.DeleteGoalAsync(goalId);
        return new DyDeleteGoalResponse { Success = success };
    }

    public override async Task<DyFitnessGoal> CreateNextRepeatingGoal(DyCreateNextRepeatingGoalRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var goalId = Guid.Parse(request.GoalId);

        var goal = await _goalService.GetGoalByIdAsync(goalId);
        if (goal is null || goal.AccountId != accountId)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Goal not found"));
        }

        var result = await _goalService.CreateNextRepeatingGoalAsync(goalId);
        if (result is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Cannot create next repeating goal. Check if goal is repeatable and hasn't reached max repetitions."));
        }

        return ToDyFitnessGoal(result);
    }

    public override async Task<DyUpdateVisibilityResponse> UpdateGoalsVisibility(DyUpdateGoalsVisibilityRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var goalIds = request.GoalIds.Select(Guid.Parse);
        var visibility = (FitnessVisibility)request.Visibility;

        var count = await _goalService.UpdateGoalsVisibilityAsync(accountId, goalIds, visibility);

        return new DyUpdateVisibilityResponse
        {
            Success = count > 0,
            UpdatedCount = count
        };
    }

    // Exercise Library - Not implemented yet (no backend service exists)
    public override Task<DyListExercisesResponse> ListExercises(DyListExercisesRequest request, ServerCallContext context)
    {
        throw new RpcException(new Status(StatusCode.Unimplemented, "Exercise library not implemented yet"));
    }

    public override Task<DyExerciseLibrary> GetExercise(DyGetExerciseRequest request, ServerCallContext context)
    {
        throw new RpcException(new Status(StatusCode.Unimplemented, "Exercise library not implemented yet"));
    }

    public override Task<DyExerciseLibrary> CreateExercise(DyCreateExerciseRequest request, ServerCallContext context)
    {
        throw new RpcException(new Status(StatusCode.Unimplemented, "Exercise library not implemented yet"));
    }

    public override Task<DyExerciseLibrary> UpdateExercise(DyUpdateExerciseRequest request, ServerCallContext context)
    {
        throw new RpcException(new Status(StatusCode.Unimplemented, "Exercise library not implemented yet"));
    }

    public override Task<DyDeleteExerciseResponse> DeleteExercise(DyDeleteExerciseRequest request, ServerCallContext context)
    {
        throw new RpcException(new Status(StatusCode.Unimplemented, "Exercise library not implemented yet"));
    }

    // Mapping helpers
    private static DyWorkout ToDyWorkout(SnWorkout w) => new()
    {
        Id = w.Id.ToString(),
        AccountId = w.AccountId.ToString(),
        Name = w.Name,
        Description = w.Description ?? string.Empty,
        Type = (DyWorkoutType)w.Type,
        StartTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(w.StartTime.ToDateTimeUtc()),
        EndTime = w.EndTime.HasValue ? Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(w.EndTime.Value.ToDateTimeUtc()) : null,
        Duration = w.Duration?.ToString() ?? string.Empty,
        CaloriesBurned = w.CaloriesBurned ?? 0,
        Notes = w.Notes ?? string.Empty,
        ExternalId = w.ExternalId ?? string.Empty,
        Visibility = ToDyFitnessVisibility(w.Visibility),
        CreatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(w.CreatedAt.ToDateTimeUtc()),
        UpdatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(w.UpdatedAt.ToDateTimeUtc()),
        Meta = w.Meta?.RootElement.GetRawText() ?? string.Empty,
        Distance = w.Distance.HasValue ? (double)w.Distance.Value : 0,
        DistanceUnit = w.DistanceUnit ?? string.Empty,
        AverageSpeed = w.AverageSpeed.HasValue ? (double)w.AverageSpeed.Value : 0,
        AverageHeartRate = w.AverageHeartRate ?? 0,
        MaxHeartRate = w.MaxHeartRate ?? 0,
        ElevationGain = w.ElevationGain.HasValue ? (double)w.ElevationGain.Value : 0,
        MaxSpeed = w.MaxSpeed.HasValue ? (double)w.MaxSpeed.Value : 0
    };

    private static DyFitnessMetric ToDyFitnessMetric(SnFitnessMetric m) => new()
    {
        Id = m.Id.ToString(),
        AccountId = m.AccountId.ToString(),
        MetricType = (DyFitnessMetricType)m.MetricType,
        Value = (double)m.Value,
        Unit = m.Unit ?? string.Empty,
        RecordedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(m.RecordedAt.ToDateTimeUtc()),
        Notes = m.Notes ?? string.Empty,
        Source = m.Source ?? string.Empty,
        ExternalId = m.ExternalId ?? string.Empty,
        Visibility = ToDyFitnessVisibility(m.Visibility),
        CreatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(m.CreatedAt.ToDateTimeUtc()),
        UpdatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(m.UpdatedAt.ToDateTimeUtc())
    };

    private static DyFitnessGoal ToDyFitnessGoal(SnFitnessGoal g) => new()
    {
        Id = g.Id.ToString(),
        AccountId = g.AccountId.ToString(),
        GoalType = (DyFitnessGoalType)g.GoalType,
        Title = g.Title,
        Description = g.Description ?? string.Empty,
        TargetValue = g.TargetValue.HasValue ? (double)g.TargetValue.Value : 0,
        CurrentValue = g.CurrentValue.HasValue ? (double)g.CurrentValue.Value : 0,
        Unit = g.Unit ?? string.Empty,
        StartDate = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(g.StartDate.ToDateTimeUtc()),
        EndDate = g.EndDate.HasValue ? Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(g.EndDate.Value.ToDateTimeUtc()) : null,
        Status = (DyFitnessGoalStatus)g.Status,
        Notes = g.Notes ?? string.Empty,
        Visibility = ToDyFitnessVisibility(g.Visibility),
        BoundWorkoutType = g.BoundWorkoutType.HasValue ? (DyWorkoutType)g.BoundWorkoutType.Value : 0,
        BoundMetricType = g.BoundMetricType.HasValue ? (DyFitnessMetricType)g.BoundMetricType.Value : 0,
        AutoUpdateProgress = g.AutoUpdateProgress,
        RepeatType = (DyRepeatType)g.RepeatType,
        RepeatInterval = g.RepeatInterval,
        RepeatCount = g.RepeatCount ?? 0,
        CurrentRepetition = g.CurrentRepetition,
        ParentGoalId = g.ParentGoalId.HasValue ? g.ParentGoalId.Value.ToString() : string.Empty,
        CreatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(g.CreatedAt.ToDateTimeUtc()),
        UpdatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(g.UpdatedAt.ToDateTimeUtc())
    };

    private static DyFitnessVisibility ToDyFitnessVisibility(FitnessVisibility visibility) => visibility switch
    {
        FitnessVisibility.Private => DyFitnessVisibility.Private,
        FitnessVisibility.Public => DyFitnessVisibility.Public,
        _ => DyFitnessVisibility.Unspecified
    };

    private static NodaTime.Duration? ParseDuration(string? durationStr)
    {
        if (string.IsNullOrEmpty(durationStr)) return null;
        var pattern = DurationPattern.Roundtrip;
        var result = pattern.Parse(durationStr);
        return result.Success ? result.Value : null;
    }
}
