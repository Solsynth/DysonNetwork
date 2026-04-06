using DysonNetwork.Shared.Proto;
using Grpc.Core;
using DysonNetwork.Fitness.Workouts;
using DysonNetwork.Fitness.Metrics;
using DysonNetwork.Fitness.Goals;
using DysonNetwork.Fitness.ExerciseLibrary;

namespace DysonNetwork.Fitness;

public class FitnessGrpcService : DyFitnessService.DyFitnessServiceBase
{
    private readonly WorkoutService _workoutService;
    private readonly MetricService _metricService;
    private readonly GoalService _goalService;
    private readonly ExerciseLibraryService _exerciseLibraryService;
    private readonly ILogger<FitnessGrpcService> _logger;

    public FitnessGrpcService(
        WorkoutService workoutService,
        MetricService metricService,
        GoalService goalService,
        ExerciseLibraryService exerciseLibraryService,
        ILogger<FitnessGrpcService> logger)
    {
        _workoutService = workoutService;
        _metricService = metricService;
        _goalService = goalService;
        _exerciseLibraryService = exerciseLibraryService;
        _logger = logger;
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

    private static DyWorkout ToDyWorkout(SnWorkout w) => new()
    {
        Id = w.Id.ToString(),
        AccountId = w.AccountId.ToString(),
        Name = w.Name,
        Description = w.Description,
        Type = (DyWorkoutType)w.Type,
        StartTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(w.StartTime.ToDateTimeUtc()),
        EndTime = w.EndTime.HasValue ? Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(w.EndTime.Value.ToDateTimeUtc()) : null,
        Duration = w.Duration?.ToString(),
        CaloriesBurned = w.CaloriesBurned ?? 0,
        Notes = w.Notes,
        ExternalId = w.ExternalId,
        Visibility = (DyFitnessVisibility)w.Visibility,
        CreatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(w.CreatedAt.ToDateTimeUtc()),
        UpdatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(w.UpdatedAt.ToDateTimeUtc())
    };

    private static DyFitnessMetric ToDyFitnessMetric(SnFitnessMetric m) => new()
    {
        Id = m.Id.ToString(),
        AccountId = m.AccountId.ToString(),
        MetricType = (DyFitnessMetricType)m.MetricType,
        Value = (double)m.Value,
        Unit = m.Unit,
        RecordedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(m.RecordedAt.ToDateTimeUtc()),
        Notes = m.Notes,
        Source = m.Source,
        ExternalId = m.ExternalId,
        Visibility = (DyFitnessVisibility)m.Visibility,
        CreatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(m.CreatedAt.ToDateTimeUtc()),
        UpdatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(m.UpdatedAt.ToDateTimeUtc())
    };

    private static DyFitnessGoal ToDyFitnessGoal(SnFitnessGoal g) => new()
    {
        Id = g.Id.ToString(),
        AccountId = g.AccountId.ToString(),
        GoalType = (DyFitnessGoalType)g.GoalType,
        Title = g.Title,
        Description = g.Description,
        TargetValue = g.TargetValue.HasValue ? (double)g.TargetValue.Value : 0,
        CurrentValue = (double)g.CurrentValue,
        Unit = g.Unit,
        StartDate = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(g.StartDate.ToDateTimeUtc()),
        EndDate = g.EndDate.HasValue ? Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(g.EndDate.Value.ToDateTimeUtc()) : null,
        Status = (DyFitnessGoalStatus)g.Status,
        Notes = g.Notes,
        Visibility = (DyFitnessVisibility)g.Visibility,
        BoundWorkoutType = g.BoundWorkoutType.HasValue ? (DyWorkoutType)g.BoundWorkoutType.Value : 0,
        BoundMetricType = g.BoundMetricType.HasValue ? (DyFitnessMetricType)g.BoundMetricType.Value : 0,
        AutoUpdateProgress = g.AutoUpdateProgress,
        RepeatType = (DyRepeatType)g.RepeatType,
        RepeatInterval = g.RepeatInterval,
        RepeatCount = g.RepeatCount ?? 0,
        CurrentRepetition = g.CurrentRepetition,
        ParentGoalId = g.ParentGoalId?.ToString(),
        CreatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(g.CreatedAt.ToDateTimeUtc()),
        UpdatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(g.UpdatedAt.ToDateTimeUtc())
    };
}
