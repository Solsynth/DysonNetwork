using DysonNetwork.Drive.Storage.Model;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Data;
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
        task.TaskId = await Nanoid.GenerateAsync();
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
    private async Task<T?> GetTaskAsync<T>(string taskId) where T : PersistentTask
    {
        var cacheKey = $"{CacheKeyPrefix}{taskId}";
        var cachedTask = await cache.GetAsync<T>(cacheKey);
        if (cachedTask is not null)
            return cachedTask;

        var task = await db.Tasks
            .FirstOrDefaultAsync(t => t.TaskId == taskId);

        if (task is not T typedTask) return null;
        await SetCacheAsync(typedTask);
        return typedTask;
    }

    /// <summary>
    /// Updates task progress
    /// </summary>
    public async Task UpdateTaskProgressAsync(string taskId, double progress, string? statusMessage = null)
    {
        var task = await GetTaskAsync<PersistentTask>(taskId);
        if (task is null) return;

        var previousProgress = task.Progress;
        var delta = progress - previousProgress;
        var clampedProgress = Math.Clamp(progress, 0, 1.0);
        var now = SystemClock.Instance.GetCurrentInstant();

        // Update the cached task
        task.Progress = clampedProgress;
        task.LastActivity = now;
        task.UpdatedAt = now;
        if (statusMessage is not null)
            task.Description = statusMessage;

        await SetCacheAsync(task);

        // Send progress update notification
        await SendTaskProgressUpdateAsync(task, task.Progress, previousProgress);

        // Only updates when update in period
        // Use ExecuteUpdateAsync for better performance - update only the fields we need
        if (Math.Abs(progress - 1) < 0.1 || delta * 100 > 5)
        {
            await db.Tasks
                .Where(t => t.TaskId == taskId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(t => t.Progress, clampedProgress)
                    .SetProperty(t => t.LastActivity, now)
                    .SetProperty(t => t.UpdatedAt, now)
                    .SetProperty(t => t.Description, t => statusMessage ?? t.Description)
                );
        }
    }

    /// <summary>
    /// Marks a task as completed
    /// </summary>
    public async Task MarkTaskCompletedAsync(string taskId, Dictionary<string, object?>? results = null)
    {
        var task = await GetTaskAsync<PersistentTask>(taskId);
        if (task is null) return;

        var now = SystemClock.Instance.GetCurrentInstant();

        // Use ExecuteUpdateAsync for better performance - update only the fields we need
        var updatedRows = await db.Tasks
            .Where(t => t.TaskId == taskId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(t => t.Status, TaskStatus.Completed)
                .SetProperty(t => t.Progress, 1.0)
                .SetProperty(t => t.CompletedAt, now)
                .SetProperty(t => t.LastActivity, now)
                .SetProperty(t => t.UpdatedAt, now)
            );

        if (updatedRows > 0)
        {
            // Update the cached task with results if provided
            task.Status = TaskStatus.Completed;
            task.Progress = 1.0;
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

            await RemoveCacheAsync(taskId);
            await SendTaskCompletedNotificationAsync(task);
        }
    }

    /// <summary>
    /// Marks a task as failed
    /// </summary>
    public async Task MarkTaskFailedAsync(string taskId, string? errorMessage = null)
    {
        var task = await GetTaskAsync<PersistentTask>(taskId);
        if (task is null) return;

        var now = SystemClock.Instance.GetCurrentInstant();
        var errorMsg = errorMessage ?? "Task failed due to an unknown error";

        // Use ExecuteUpdateAsync for better performance - update only the fields we need
        var updatedRows = await db.Tasks
            .Where(t => t.TaskId == taskId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(t => t.Status, TaskStatus.Failed)
                .SetProperty(t => t.ErrorMessage, errorMsg)
                .SetProperty(t => t.LastActivity, now)
                .SetProperty(t => t.UpdatedAt, now)
            );

        if (updatedRows > 0)
        {
            // Update the cached task
            task.Status = TaskStatus.Failed;
            task.ErrorMessage = errorMsg;
            task.LastActivity = now;
            task.UpdatedAt = now;

            await RemoveCacheAsync(taskId);
            await SendTaskFailedNotificationAsync(task);
        }
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
            case "created":
                orderedQuery = sortDescending
                    ? query.OrderByDescending(t => t.CreatedAt)
                    : query.OrderBy(t => t.CreatedAt);
                break;
            case "updated":
                orderedQuery = sortDescending
                    ? query.OrderByDescending(t => t.UpdatedAt)
                    : query.OrderBy(t => t.UpdatedAt);
                break;
            case "activity":
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
                Parameters = task.Parameters,
                CreatedAt = task.CreatedAt.ToString()
            };

            var packet = new WebSocketPacket
            {
                Type = "task.created",
                Data = InfraObjectCoder.ConvertObjectToByteString(data)
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
            var data = new TaskProgressData
            {
                TaskId = task.TaskId,
                Name = task.Name,
                Type = task.Type.ToString(),
                Progress = newProgress,
                Status = task.Status.ToString(),
                LastActivity = task.LastActivity.ToString()
            };

            var packet = new WebSocketPacket
            {
                Type = "task.progress",
                Data = InfraObjectCoder.ConvertObjectToByteString(data)
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
                CompletedAt = task.CompletedAt?.ToString() ?? task.UpdatedAt.ToString(),
                Results = task.Results
            };

            // WebSocket notification
            var wsPacket = new WebSocketPacket
            {
                Type = "task.completed",
                Data = InfraObjectCoder.ConvertObjectToByteString(data)
            };

            await ringService.PushWebSocketPacketAsync(new PushWebSocketPacketRequest
            {
                UserId = task.AccountId.ToString(),
                Packet = wsPacket
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
                FailedAt = task.UpdatedAt.ToString(),
                ErrorMessage = task.ErrorMessage ?? "Task failed due to an unknown error"
            };

            // WebSocket notification
            var wsPacket = new WebSocketPacket
            {
                Type = "task.failed",
                Data = InfraObjectCoder.ConvertObjectToByteString(data)
            };

            await ringService.PushWebSocketPacketAsync(new PushWebSocketPacketRequest
            {
                UserId = task.AccountId.ToString(),
                Packet = wsPacket
            });

            // Push notification
            var pushNotification = new PushNotification
            {
                Topic = "drive.tasks",
                Title = "Drive Task Failed",
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

        // Cache the entire task object directly - this includes all properties including Parameters dictionary
        await cache.SetAsync(cacheKey, task, CacheDuration);
    }

    private async Task RemoveCacheAsync(string taskId)
    {
        var cacheKey = $"{CacheKeyPrefix}{taskId}";
        await cache.RemoveAsync(cacheKey);
    }

    #endregion

    #region Upload-Specific Methods

    /// <summary>
    /// Gets the first available pool ID, or creates a default one if none exist
    /// </summary>
    private async Task<Guid> GetFirstAvailablePoolIdAsync()
    {
        // Try to get the first available pool
        var firstPool = await db.Pools
            .Where(p => p.PolicyConfig.PublicUsable)
            .OrderBy(p => p.CreatedAt)
            .FirstOrDefaultAsync();

        if (firstPool != null)
        {
            return firstPool.Id;
        }

        // If no pools exist, create a default one
        logger.LogWarning("No pools found in database. Creating default pool...");

        var defaultPoolId = Guid.NewGuid();
        var defaultPool = new DysonNetwork.Shared.Models.FilePool
        {
            Id = defaultPoolId,
            Name = "Default Storage Pool",
            Description = "Automatically created default storage pool",
            StorageConfig = new DysonNetwork.Shared.Models.RemoteStorageConfig
            {
                Region = "auto",
                Bucket = "solar-network-development",
                Endpoint = "localhost:9000",
                SecretId = "littlesheep",
                SecretKey = "password",
                EnableSigned = true,
                EnableSsl = false
            },
            BillingConfig = new DysonNetwork.Shared.Models.BillingConfig
            {
                CostMultiplier = 1.0
            },
            PolicyConfig = new DysonNetwork.Shared.Models.PolicyConfig
            {
                EnableFastUpload = true,
                EnableRecycle = true,
                PublicUsable = true,
                AllowEncryption = true,
                AllowAnonymous = true,
                AcceptTypes = new List<string> { "*/*" },
                MaxFileSize = 1024L * 1024 * 1024 * 10, // 10GB
                RequirePrivilege = 0
            },
            IsHidden = false,
            AccountId = null,
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            UpdatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        db.Pools.Add(defaultPool);
        await db.SaveChangesAsync();

        logger.LogInformation("Created default pool with ID: {PoolId}", defaultPoolId);
        return defaultPoolId;
    }

    /// <summary>
    /// Creates a new persistent upload task
    /// </summary>
    public async Task<PersistentUploadTask> CreateUploadTaskAsync(
        string taskId,
        CreateUploadTaskRequest request,
        Guid accountId
    )
    {
        var chunkSize = request.ChunkSize ?? 1024 * 1024 * 5; // 5MB default
        var chunksCount = (int)Math.Ceiling((double)request.FileSize / chunkSize);

        // If the second chunk is too small (less than 1MB), merge it with the first chunk
        if (chunksCount == 2 && (request.FileSize - chunkSize) < 1024 * 1024)
        {
            chunksCount = 1;
            chunkSize = request.FileSize;
        }

        // Use the default pool if no pool is specified, or find the first available pool
        var poolId = request.PoolId ?? await GetFirstAvailablePoolIdAsync();

        var uploadTask = new PersistentUploadTask
        {
            TaskId = taskId,
            FileName = request.FileName,
            FileSize = request.FileSize,
            ContentType = request.ContentType,
            ChunkSize = chunkSize,
            ChunksCount = chunksCount,
            ChunksUploaded = 0,
            PoolId = poolId,
            BundleId = request.BundleId,
            EncryptPassword = request.EncryptPassword,
            ExpiredAt = request.ExpiredAt,
            Hash = request.Hash,
            Path = request.Path,
            AccountId = accountId,
            Status = TaskStatus.InProgress,
            UploadedChunks = [],
            LastActivity = SystemClock.Instance.GetCurrentInstant()
        };

        db.Tasks.Add(uploadTask);
        await db.SaveChangesAsync();

        await SetCacheAsync(uploadTask);
        await SendTaskCreatedNotificationAsync(uploadTask);
        return uploadTask;
    }

    /// <summary>
    /// Gets an existing upload task by ID
    /// </summary>
    public async Task<PersistentUploadTask?> GetUploadTaskAsync(string taskId)
    {
        var cacheKey = $"{CacheKeyPrefix}{taskId}";
        var cachedTask = await cache.GetAsync<PersistentUploadTask>(cacheKey);
        if (cachedTask is not null)
            return cachedTask;

        var baseTask = await db.Tasks
            .FirstOrDefaultAsync(t => t.TaskId == taskId && t.Type == TaskType.FileUpload && t.Status == TaskStatus.InProgress);

        if (baseTask is null)
            return null;

        var task = ConvertToUploadTask(baseTask);
        await SetCacheAsync(task);
        return task;
    }

    /// <summary>
    /// Converts a base PersistentTask to PersistentUploadTask
    /// </summary>
    private PersistentUploadTask ConvertToUploadTask(PersistentTask baseTask)
    {
        return new PersistentUploadTask
        {
            Id = baseTask.Id,
            TaskId = baseTask.TaskId,
            Name = baseTask.Name,
            Description = baseTask.Description,
            Type = baseTask.Type,
            Status = baseTask.Status,
            AccountId = baseTask.AccountId,
            Progress = baseTask.Progress,
            Parameters = baseTask.Parameters,
            Results = baseTask.Results,
            ErrorMessage = baseTask.ErrorMessage,
            StartedAt = baseTask.StartedAt,
            CompletedAt = baseTask.CompletedAt,
            ExpiredAt = baseTask.ExpiredAt,
            LastActivity = baseTask.LastActivity,
            Priority = baseTask.Priority,
            EstimatedDurationSeconds = baseTask.EstimatedDurationSeconds,
            CreatedAt = baseTask.CreatedAt,
            UpdatedAt = baseTask.UpdatedAt
        };
    }

    /// <summary>
    /// Converts a list of base PersistentTasks to PersistentUploadTasks
    /// </summary>
    private List<PersistentUploadTask> ConvertToUploadTasks(List<PersistentTask> baseTasks)
    {
        return baseTasks.Select(ConvertToUploadTask).ToList();
    }

    /// <summary>
    /// Updates chunk upload progress
    /// </summary>
    public async Task UpdateChunkProgressAsync(string taskId, int chunkIndex)
    {
        var task = await GetUploadTaskAsync(taskId);
        if (task is null) return;

        if (!task.UploadedChunks.Contains(chunkIndex))
        {
            var previousProgress = task.ChunksCount > 0 ? (double)task.ChunksUploaded / task.ChunksCount * 100 : 0;

            // Get current parameters and update them directly
            var parameters = task.TypedParameters;
            if (!parameters.UploadedChunks.Contains(chunkIndex))
            {
                parameters.UploadedChunks.Add(chunkIndex);
                parameters.ChunksUploaded = parameters.UploadedChunks.Count;

                var now = SystemClock.Instance.GetCurrentInstant();

                // Use ExecuteUpdateAsync to update the Parameters dictionary directly
                var updatedRows = await db.Tasks
                    .Where(t => t.TaskId == taskId && t.Type == TaskType.FileUpload)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(t => t.Parameters, ParameterHelper.Untyped(parameters))
                        .SetProperty(t => t.LastActivity, now)
                        .SetProperty(t => t.UpdatedAt, now)
                    );

                if (updatedRows > 0)
                {
                    // Update the cached task
                    task.UploadedChunks.Add(chunkIndex);
                    task.ChunksUploaded = task.UploadedChunks.Count;
                    task.LastActivity = now;
                    task.UpdatedAt = now;
                    await SetCacheAsync(task);

                    // Send real-time progress update
                    var newProgress = task.ChunksCount > 0 ? (double)task.ChunksUploaded / task.ChunksCount * 100 : 0;
                    await SendUploadProgressUpdateAsync(task, newProgress, previousProgress);
                }
            }
        }
    }

    /// <summary>
    /// Checks if a chunk has already been uploaded
    /// </summary>
    public async Task<bool> IsChunkUploadedAsync(string taskId, int chunkIndex)
    {
        var task = await GetUploadTaskAsync(taskId);
        return task?.UploadedChunks.Contains(chunkIndex) ?? false;
    }

    /// <summary>
    /// Gets upload progress as percentage
    /// </summary>
    public async Task<double> GetUploadProgressAsync(string taskId)
    {
        var task = await GetUploadTaskAsync(taskId);
        if (task is null || task.ChunksCount == 0) return 0;

        return (double)task.ChunksUploaded / task.ChunksCount * 100;
    }

    /// <summary>
    /// Gets user upload tasks with filtering and pagination
    /// </summary>
    public async Task<(List<PersistentUploadTask> Items, int TotalCount)> GetUserUploadTasksAsync(
        Guid accountId,
        UploadTaskStatus? status = null,
        string? sortBy = "lastActivity",
        bool sortDescending = true,
        int offset = 0,
        int limit = 50
    )
    {
        var query = db.Tasks.Where(t => t.Type == TaskType.FileUpload && t.AccountId == accountId);

        // Apply status filter
        if (status.HasValue)
        {
            query = query.Where(t => t.Status == (TaskStatus)status.Value);
        }

        // Get total count
        var totalCount = await query.CountAsync();

        // Apply sorting
        IOrderedQueryable<PersistentTask> orderedQuery;
        switch (sortBy?.ToLower())
        {
            case "created":
                orderedQuery = sortDescending
                    ? query.OrderByDescending(t => t.CreatedAt)
                    : query.OrderBy(t => t.CreatedAt);
                break;
            case "updated":
                orderedQuery = sortDescending
                    ? query.OrderByDescending(t => t.UpdatedAt)
                    : query.OrderBy(t => t.UpdatedAt);
                break;
            case "activity":
            default:
                orderedQuery = sortDescending
                    ? query.OrderByDescending(t => t.LastActivity)
                    : query.OrderBy(t => t.LastActivity);
                break;
        }

        // Apply pagination
        var baseTasks = await orderedQuery
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        var items = ConvertToUploadTasks(baseTasks);

        // Sort by derived properties if needed (filename, filesize)
        if (sortBy?.ToLower() == "filename")
        {
            items = sortDescending
                ? items.OrderByDescending(t => t.FileName).ToList()
                : items.OrderBy(t => t.FileName).ToList();
        }
        else if (sortBy?.ToLower() == "filesize")
        {
            items = sortDescending
                ? items.OrderByDescending(t => t.FileSize).ToList()
                : items.OrderBy(t => t.FileSize).ToList();
        }

        return (items, totalCount);
    }

    /// <summary>
    /// Gets upload statistics for a user
    /// </summary>
    public async Task<UserUploadStats> GetUserUploadStatsAsync(Guid accountId)
    {
        var baseTasks = await db.Tasks
            .Where(t => t.Type == TaskType.FileUpload && t.AccountId == accountId)
            .ToListAsync();

        var tasks = ConvertToUploadTasks(baseTasks);

        var stats = new UserUploadStats
        {
            TotalTasks = tasks.Count,
            InProgressTasks = tasks.Count(t => t.Status == TaskStatus.InProgress),
            CompletedTasks = tasks.Count(t => t.Status == TaskStatus.Completed),
            FailedTasks = tasks.Count(t => t.Status == TaskStatus.Failed),
            ExpiredTasks = tasks.Count(t => t.Status == TaskStatus.Expired),
            TotalUploadedBytes = tasks.Sum(t => t.ChunksUploaded * t.ChunkSize),
            AverageProgress = tasks.Any(t => t.Status == TaskStatus.InProgress)
                ? tasks.Where(t => t.Status == TaskStatus.InProgress)
                    .Average(t => t.ChunksCount > 0 ? (double)t.ChunksUploaded / t.ChunksCount * 100 : 0)
                : 0,
            RecentActivity = tasks.OrderByDescending(t => t.LastActivity)
                .Take(5)
                .Select(t => new RecentActivity
                {
                    TaskId = t.TaskId,
                    FileName = t.FileName,
                    Status = (UploadTaskStatus)t.Status,
                    LastActivity = t.LastActivity,
                    Progress = t.ChunksCount > 0 ? (double)t.ChunksUploaded / t.ChunksCount * 100 : 0
                })
                .ToList()
        };

        return stats;
    }

    /// <summary>
    /// Cleans up failed tasks for a user
    /// </summary>
    public async Task<int> CleanupUserFailedTasksAsync(Guid accountId)
    {
        var failedTasks = await db.Tasks
            .Where(t => t.Type == TaskType.FileUpload && t.AccountId == accountId &&
                        (t.Status == TaskStatus.Failed || t.Status == TaskStatus.Expired))
            .ToListAsync();

        foreach (var task in failedTasks)
        {
            await RemoveCacheAsync(task.TaskId);

            // Clean up temp files
            var taskPath = Path.Combine(Path.GetTempPath(), "multipart-uploads", task.TaskId);
            if (!Directory.Exists(taskPath)) continue;
            try
            {
                Directory.Delete(taskPath, true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to cleanup temp files for task {TaskId}", task.TaskId);
            }
        }

        db.Tasks.RemoveRange(failedTasks);
        await db.SaveChangesAsync();

        return failedTasks.Count;
    }

    /// <summary>
    /// Gets recent tasks for a user
    /// </summary>
    public async Task<List<PersistentUploadTask>> GetRecentUserTasksAsync(Guid accountId, int limit = 10)
    {
        var baseTasks = await db.Tasks
            .Where(t => t.Type == TaskType.FileUpload && t.AccountId == accountId)
            .OrderByDescending(t => t.LastActivity)
            .Take(limit)
            .ToListAsync();

        return ConvertToUploadTasks(baseTasks);
    }

    /// <summary>
    /// Sends upload completion notification
    /// </summary>
    public async Task SendUploadCompletedNotificationAsync(PersistentUploadTask task, string fileId)
    {
        try
        {
            var completionData = new UploadCompletionData
            {
                TaskId = task.TaskId,
                FileId = fileId,
                FileName = task.FileName,
                FileSize = task.FileSize,
                CompletedAt = SystemClock.Instance.GetCurrentInstant().ToString()
            };

            // Send WebSocket notification
            var wsPacket = new WebSocketPacket
            {
                Type = "upload.completed",
                Data = InfraObjectCoder.ConvertObjectToByteString(completionData)
            };

            await ringService.PushWebSocketPacketAsync(new PushWebSocketPacketRequest
            {
                UserId = task.AccountId.ToString(),
                Packet = wsPacket
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send upload completion notification for task {TaskId}", task.TaskId);
        }
    }

    /// <summary>
    /// Sends upload failure notification
    /// </summary>
    public async Task SendUploadFailedNotificationAsync(PersistentUploadTask task, string? errorMessage = null)
    {
        try
        {
            var failureData = new UploadFailureData
            {
                TaskId = task.TaskId,
                FileName = task.FileName,
                FileSize = task.FileSize,
                FailedAt = SystemClock.Instance.GetCurrentInstant().ToString(),
                ErrorMessage = errorMessage ?? "Upload failed due to an unknown error"
            };

            // Send WebSocket notification
            var wsPacket = new WebSocketPacket
            {
                Type = "upload.failed",
                Data = InfraObjectCoder.ConvertObjectToByteString(failureData)
            };

            await ringService.PushWebSocketPacketAsync(new PushWebSocketPacketRequest
            {
                UserId = task.AccountId.ToString(),
                Packet = wsPacket
            });

            // Send push notification
            var pushNotification = new PushNotification
            {
                Topic = "drive.tasks.upload",
                Title = "Upload Failed",
                Subtitle = task.FileName,
                Body = $"Your file '{task.FileName}' upload has failed. You can try again.",
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
            logger.LogWarning(ex, "Failed to send upload failure notification for task {TaskId}", task.TaskId);
        }
    }

    /// <summary>
    /// Sends real-time upload progress update via WebSocket
    /// </summary>
    private async Task SendUploadProgressUpdateAsync(PersistentUploadTask task, double newProgress,
        double previousProgress)
    {
        try
        {
            // Only send significant progress updates (every 5% or major milestones)
            if (Math.Abs(newProgress - previousProgress) < 5 && newProgress < 100)
                return;

            var progressData = new UploadProgressData
            {
                TaskId = task.TaskId,
                FileName = task.FileName,
                FileSize = task.FileSize,
                ChunksUploaded = task.ChunksUploaded,
                ChunksTotal = task.ChunksCount,
                Progress = newProgress,
                Status = task.Status.ToString(),
                LastActivity = task.LastActivity.ToString()
            };

            var packet = new WebSocketPacket
            {
                Type = "upload.progress",
                Data = InfraObjectCoder.ConvertObjectToByteString(progressData)
            };

            await ringService.PushWebSocketPacketAsync(new PushWebSocketPacketRequest
            {
                UserId = task.AccountId.ToString(),
                Packet = packet
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send upload progress update for task {TaskId}", task.TaskId);
        }
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
    public Dictionary<string, object?>? Parameters { get; set; }
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

#region Upload-Specific Data Transfer Objects

public class UploadProgressData
{
    public string TaskId { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public long FileSize { get; set; }
    public int ChunksUploaded { get; set; }
    public int ChunksTotal { get; set; }
    public double Progress { get; set; }
    public string Status { get; set; } = null!;
    public string LastActivity { get; set; } = null!;
}

public class UploadCompletionData
{
    public string TaskId { get; set; } = null!;
    public string FileId { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public long FileSize { get; set; }
    public string CompletedAt { get; set; } = null!;
}

public class UploadFailureData
{
    public string TaskId { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public long FileSize { get; set; }
    public string FailedAt { get; set; } = null!;
    public string ErrorMessage { get; set; } = null!;
}

public class UserUploadStats
{
    public int TotalTasks { get; set; }
    public int InProgressTasks { get; set; }
    public int CompletedTasks { get; set; }
    public int FailedTasks { get; set; }
    public int ExpiredTasks { get; set; }
    public long TotalUploadedBytes { get; set; }
    public double AverageProgress { get; set; }
    public List<RecentActivity> RecentActivity { get; set; } = new();
}

public class RecentActivity
{
    public string TaskId { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public UploadTaskStatus Status { get; set; }
    public Instant LastActivity { get; set; }
    public double Progress { get; set; }
}

#endregion
