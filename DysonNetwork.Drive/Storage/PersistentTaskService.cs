using DysonNetwork.Drive.Storage.Model;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Proto;
using Microsoft.EntityFrameworkCore;
using NanoidDotNet;
using NodaTime;
using TaskStatus = DysonNetwork.Drive.Storage.Model.TaskStatus;

namespace DysonNetwork.Drive.Storage;

/// <summary>
/// Generic task service for handling various types of background operations
/// </summary>
public class PersistentTaskService(
    AppDatabase db,
    ICacheService cache,
    ILogger<PersistentTaskService> logger,
    RingService.RingServiceClient ringService
)
{
    private const string CacheKeyPrefix = "task:";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Creates a new task of any type
    /// </summary>
    public async Task<T> CreateTaskAsync<T>(T task) where T : PersistentTask
    {
        task.TaskId = NanoidDotNet.Nanoid.Generate();
        var now = SystemClock.Instance.GetCurrentInstant();
        task.CreatedAt = now;
        task.UpdatedAt = now;
        task.LastActivity = now;
        task.StartedAt = now;

        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        await SetCacheAsync(task);
        await SendTaskCreatedNotificationAsync(task);

        return task;
    }

    /// <summary>
    /// Gets a task by ID
    /// </summary>
    public async Task<T?> GetTaskAsync<T>(string taskId) where T : PersistentTask
    {
        var cacheKey = $"{CacheKeyPrefix}{taskId}";
        var cachedTask = await cache.GetAsync<T>(cacheKey);
        if (cachedTask is not null)
            return cachedTask;

        var task = await db.Tasks
            .FirstOrDefaultAsync(t => t.TaskId == taskId);

        if (task is T typedTask)
        {
            await SetCacheAsync(typedTask);
            return typedTask;
        }

        return null;
    }

    /// <summary>
    /// Updates task progress
    /// </summary>
    public async Task UpdateTaskProgressAsync(string taskId, double progress, string? statusMessage = null)
    {
        var task = await GetTaskAsync<PersistentTask>(taskId);
        if (task is null) return;

        var previousProgress = task.Progress;
        task.Progress = Math.Clamp(progress, 0, 100);
        task.LastActivity = SystemClock.Instance.GetCurrentInstant();
        task.UpdatedAt = task.LastActivity;

        if (statusMessage is not null)
        {
            task.Description = statusMessage;
        }

        await db.SaveChangesAsync();
        await SetCacheAsync(task);

        // Send progress update notification
        await SendTaskProgressUpdateAsync(task, task.Progress, previousProgress);
    }

    /// <summary>
    /// Marks a task as completed
    /// </summary>
    public async Task MarkTaskCompletedAsync(string taskId, Dictionary<string, object?>? results = null)
    {
        var task = await GetTaskAsync<PersistentTask>(taskId);
        if (task is null) return;

        var now = SystemClock.Instance.GetCurrentInstant();
        task.Status = TaskStatus.Completed;
        task.Progress = 100;
        task.CompletedAt = now;
        task.LastActivity = now;
        task.UpdatedAt = now;

        if (results is not null)
        {
            foreach (var (key, value) in results)
            {
                task.Results[key] = value;
            }
        }

        await db.SaveChangesAsync();
        await RemoveCacheAsync(taskId);

        await SendTaskCompletedNotificationAsync(task);
    }

    /// <summary>
    /// Marks a task as failed
    /// </summary>
    public async Task MarkTaskFailedAsync(string taskId, string? errorMessage = null)
    {
        var task = await GetTaskAsync<PersistentTask>(taskId);
        if (task is null) return;

        task.Status = TaskStatus.Failed;
        task.ErrorMessage = errorMessage ?? "Task failed due to an unknown error";
        task.LastActivity = SystemClock.Instance.GetCurrentInstant();
        task.UpdatedAt = task.LastActivity;

        await db.SaveChangesAsync();
        await RemoveCacheAsync(taskId);

        await SendTaskFailedNotificationAsync(task);
    }

    /// <summary>
    /// Pauses a task
    /// </summary>
    public async Task PauseTaskAsync(string taskId)
    {
        var task = await GetTaskAsync<PersistentTask>(taskId);
        if (task is null || task.Status != TaskStatus.InProgress) return;

        task.Status = TaskStatus.Paused;
        task.LastActivity = SystemClock.Instance.GetCurrentInstant();
        task.UpdatedAt = task.LastActivity;

        await db.SaveChangesAsync();
        await SetCacheAsync(task);
    }

    /// <summary>
    /// Resumes a paused task
    /// </summary>
    public async Task ResumeTaskAsync(string taskId)
    {
        var task = await GetTaskAsync<PersistentTask>(taskId);
        if (task is null || task.Status != TaskStatus.Paused) return;

        task.Status = TaskStatus.InProgress;
        task.LastActivity = SystemClock.Instance.GetCurrentInstant();
        task.UpdatedAt = task.LastActivity;

        await db.SaveChangesAsync();
        await SetCacheAsync(task);
    }

    /// <summary>
    /// Cancels a task
    /// </summary>
    public async Task CancelTaskAsync(string taskId)
    {
        var task = await GetTaskAsync<PersistentTask>(taskId);
        if (task is null) return;

        task.Status = TaskStatus.Cancelled;
        task.LastActivity = SystemClock.Instance.GetCurrentInstant();
        task.UpdatedAt = task.LastActivity;

        await db.SaveChangesAsync();
        await RemoveCacheAsync(taskId);
    }

    /// <summary>
    /// Gets tasks for a user with filtering and pagination
    /// </summary>
    public async Task<(List<PersistentTask> Items, int TotalCount)> GetUserTasksAsync(
        Guid accountId,
        TaskType? type = null,
        TaskStatus? status = null,
        string? sortBy = "lastActivity",
        bool sortDescending = true,
        int offset = 0,
        int limit = 50
    )
    {
        var query = db.Tasks.Where(t => t.AccountId == accountId);

        // Apply filters
        if (type.HasValue)
        {
            query = query.Where(t => t.Type == type.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(t => t.Status == status.Value);
        }

        // Get total count
        var totalCount = await query.CountAsync();

        // Apply sorting
        IOrderedQueryable<PersistentTask> orderedQuery;
        switch (sortBy?.ToLower())
        {
            case "name":
                orderedQuery = sortDescending
                    ? query.OrderByDescending(t => t.Name)
                    : query.OrderBy(t => t.Name);
                break;
            case "type":
                orderedQuery = sortDescending
                    ? query.OrderByDescending(t => t.Type)
                    : query.OrderBy(t => t.Type);
                break;
            case "progress":
                orderedQuery = sortDescending
                    ? query.OrderByDescending(t => t.Progress)
                    : query.OrderBy(t => t.Progress);
                break;
            case "createdat":
                orderedQuery = sortDescending
                    ? query.OrderByDescending(t => t.CreatedAt)
                    : query.OrderBy(t => t.CreatedAt);
                break;
            case "updatedat":
                orderedQuery = sortDescending
                    ? query.OrderByDescending(t => t.UpdatedAt)
                    : query.OrderBy(t => t.UpdatedAt);
                break;
            case "lastactivity":
            default:
                orderedQuery = sortDescending
                    ? query.OrderByDescending(t => t.LastActivity)
                    : query.OrderBy(t => t.LastActivity);
                break;
        }

        // Apply pagination
        var items = await orderedQuery
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        return (items, totalCount);
    }

    /// <summary>
    /// Gets task statistics for a user
    /// </summary>
    public async Task<TaskStatistics> GetUserTaskStatsAsync(Guid accountId)
    {
        var tasks = await db.Tasks
            .Where(t => t.AccountId == accountId)
            .ToListAsync();

        var stats = new TaskStatistics
        {
            TotalTasks = tasks.Count,
            PendingTasks = tasks.Count(t => t.Status == TaskStatus.Pending),
            InProgressTasks = tasks.Count(t => t.Status == TaskStatus.InProgress),
            PausedTasks = tasks.Count(t => t.Status == TaskStatus.Paused),
            CompletedTasks = tasks.Count(t => t.Status == TaskStatus.Completed),
            FailedTasks = tasks.Count(t => t.Status == TaskStatus.Failed),
            CancelledTasks = tasks.Count(t => t.Status == TaskStatus.Cancelled),
            ExpiredTasks = tasks.Count(t => t.Status == TaskStatus.Expired),
            AverageProgress = tasks.Any(t => t.Status == TaskStatus.InProgress || t.Status == TaskStatus.Paused)
                ? tasks.Where(t => t.Status == TaskStatus.InProgress || t.Status == TaskStatus.Paused)
                       .Average(t => t.Progress)
                : 0,
            RecentActivity = tasks.OrderByDescending(t => t.LastActivity)
                                 .Take(10)
                                 .Select(t => new TaskActivity
                                 {
                                     TaskId = t.TaskId,
                                     Name = t.Name,
                                     Type = t.Type,
                                     Status = t.Status,
                                     Progress = t.Progress,
                                     LastActivity = t.LastActivity
                                 })
                                 .ToList()
        };

        return stats;
    }

    /// <summary>
    /// Cleans up old completed/failed tasks
    /// </summary>
    public async Task<int> CleanupOldTasksAsync(Guid accountId, Duration maxAge = default)
    {
        if (maxAge == default)
        {
            maxAge = Duration.FromDays(30); // Default 30 days
        }

        var cutoff = SystemClock.Instance.GetCurrentInstant() - maxAge;

        var oldTasks = await db.Tasks
            .Where(t => t.AccountId == accountId &&
                       (t.Status == TaskStatus.Completed ||
                        t.Status == TaskStatus.Failed ||
                        t.Status == TaskStatus.Cancelled ||
                        t.Status == TaskStatus.Expired) &&
                       t.UpdatedAt < cutoff)
            .ToListAsync();

        db.Tasks.RemoveRange(oldTasks);
        await db.SaveChangesAsync();

        // Clean up cache
        foreach (var task in oldTasks)
        {
            await RemoveCacheAsync(task.TaskId);
        }

        return oldTasks.Count;
    }

    #region Notification Methods

    private async Task SendTaskCreatedNotificationAsync(PersistentTask task)
    {
        try
        {
            var data = new TaskCreatedData
            {
                TaskId = task.TaskId,
                Name = task.Name,
                Type = task.Type.ToString(),
                CreatedAt = task.CreatedAt.ToString("O", null)
            };

            var packet = new WebSocketPacket
            {
                Type = "task.created",
                Data = Google.Protobuf.ByteString.CopyFromUtf8(System.Text.Json.JsonSerializer.Serialize(data))
            };

            await ringService.PushWebSocketPacketAsync(new PushWebSocketPacketRequest
            {
                UserId = task.AccountId.ToString(),
                Packet = packet
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send task created notification for task {TaskId}", task.TaskId);
        }
    }

    private async Task SendTaskProgressUpdateAsync(PersistentTask task, double newProgress, double previousProgress)
    {
        try
        {
            // Only send significant progress updates (every 5% or major milestones)
            if (Math.Abs(newProgress - previousProgress) < 5 && newProgress < 100 && newProgress > 0)
                return;

            var data = new TaskProgressData
            {
                TaskId = task.TaskId,
                Name = task.Name,
                Type = task.Type.ToString(),
                Progress = newProgress,
                Status = task.Status.ToString(),
                LastActivity = task.LastActivity.ToString("O", null)
            };

            var packet = new WebSocketPacket
            {
                Type = "task.progress",
                Data = Google.Protobuf.ByteString.CopyFromUtf8(System.Text.Json.JsonSerializer.Serialize(data))
            };

            await ringService.PushWebSocketPacketAsync(new PushWebSocketPacketRequest
            {
                UserId = task.AccountId.ToString(),
                Packet = packet
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send task progress update for task {TaskId}", task.TaskId);
        }
    }

    private async Task SendTaskCompletedNotificationAsync(PersistentTask task)
    {
        try
        {
            var data = new TaskCompletionData
            {
                TaskId = task.TaskId,
                Name = task.Name,
                Type = task.Type.ToString(),
                CompletedAt = task.CompletedAt?.ToString("O", null) ?? task.UpdatedAt.ToString("O", null),
                Results = task.Results
            };

            // WebSocket notification
            var wsPacket = new WebSocketPacket
            {
                Type = "task.completed",
                Data = Google.Protobuf.ByteString.CopyFromUtf8(System.Text.Json.JsonSerializer.Serialize(data))
            };

            await ringService.PushWebSocketPacketAsync(new PushWebSocketPacketRequest
            {
                UserId = task.AccountId.ToString(),
                Packet = wsPacket
            });

            // Push notification
            var pushNotification = new PushNotification
            {
                Topic = "task",
                Title = "Task Completed",
                Subtitle = task.Name,
                Body = $"Your {task.Type.ToString().ToLower()} task has completed successfully.",
                IsSavable = true
            };

            await ringService.SendPushNotificationToUserAsync(new SendPushNotificationToUserRequest
            {
                UserId = task.AccountId.ToString(),
                Notification = pushNotification
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send task completion notification for task {TaskId}", task.TaskId);
        }
    }

    private async Task SendTaskFailedNotificationAsync(PersistentTask task)
    {
        try
        {
            var data = new TaskFailureData
            {
                TaskId = task.TaskId,
                Name = task.Name,
                Type = task.Type.ToString(),
                FailedAt = task.UpdatedAt.ToString("O", null),
                ErrorMessage = task.ErrorMessage ?? "Task failed due to an unknown error"
            };

            // WebSocket notification
            var wsPacket = new WebSocketPacket
            {
                Type = "task.failed",
                Data = Google.Protobuf.ByteString.CopyFromUtf8(System.Text.Json.JsonSerializer.Serialize(data))
            };

            await ringService.PushWebSocketPacketAsync(new PushWebSocketPacketRequest
            {
                UserId = task.AccountId.ToString(),
                Packet = wsPacket
            });

            // Push notification
            var pushNotification = new PushNotification
            {
                Topic = "task",
                Title = "Task Failed",
                Subtitle = task.Name,
                Body = $"Your {task.Type.ToString().ToLower()} task has failed.",
                IsSavable = true
            };

            await ringService.SendPushNotificationToUserAsync(new SendPushNotificationToUserRequest
            {
                UserId = task.AccountId.ToString(),
                Notification = pushNotification
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send task failure notification for task {TaskId}", task.TaskId);
        }
    }

    #endregion

    #region Cache Methods

    private async Task SetCacheAsync(PersistentTask task)
    {
        var cacheKey = $"{CacheKeyPrefix}{task.TaskId}";
        await cache.SetAsync(cacheKey, task, CacheDuration);
    }

    private async Task RemoveCacheAsync(string taskId)
    {
        var cacheKey = $"{CacheKeyPrefix}{taskId}";
        await cache.RemoveAsync(cacheKey);
    }

    #endregion
}

#region Data Transfer Objects

public class TaskCreatedData
{
    public string TaskId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Type { get; set; } = null!;
    public string CreatedAt { get; set; } = null!;
}

public class TaskProgressData
{
    public string TaskId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Type { get; set; } = null!;
    public double Progress { get; set; }
    public string Status { get; set; } = null!;
    public string LastActivity { get; set; } = null!;
}

public class TaskCompletionData
{
    public string TaskId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Type { get; set; } = null!;
    public string CompletedAt { get; set; } = null!;
    public Dictionary<string, object?> Results { get; set; } = new();
}

public class TaskFailureData
{
    public string TaskId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Type { get; set; } = null!;
    public string FailedAt { get; set; } = null!;
    public string ErrorMessage { get; set; } = null!;
}

public class TaskStatistics
{
    public int TotalTasks { get; set; }
    public int PendingTasks { get; set; }
    public int InProgressTasks { get; set; }
    public int PausedTasks { get; set; }
    public int CompletedTasks { get; set; }
    public int FailedTasks { get; set; }
    public int CancelledTasks { get; set; }
    public int ExpiredTasks { get; set; }
    public double AverageProgress { get; set; }
    public List<TaskActivity> RecentActivity { get; set; } = new();
}

public class TaskActivity
{
    public string TaskId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public TaskType Type { get; set; }
    public TaskStatus Status { get; set; }
    public double Progress { get; set; }
    public Instant LastActivity { get; set; }
}

#endregion
