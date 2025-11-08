using DysonNetwork.Drive.Storage.Model;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Proto;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using System.Text.Json;
using TaskStatus = DysonNetwork.Drive.Storage.Model.TaskStatus;

namespace DysonNetwork.Drive.Storage;

public class PersistentUploadService(
    AppDatabase db,
    ICacheService cache,
    ILogger<PersistentUploadService> logger,
    RingService.RingServiceClient ringService
)
{
    private const string CacheKeyPrefix = "upload:task:";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

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

        var uploadTask = new PersistentUploadTask
        {
            TaskId = taskId,
            FileName = request.FileName,
            FileSize = request.FileSize,
            ContentType = request.ContentType,
            ChunkSize = chunkSize,
            ChunksCount = chunksCount,
            ChunksUploaded = 0,
            PoolId = request.PoolId.Value,
            BundleId = request.BundleId,
            EncryptPassword = request.EncryptPassword,
            ExpiredAt = request.ExpiredAt,
            Hash = request.Hash,
            AccountId = accountId,
            Status = Model.TaskStatus.InProgress,
            UploadedChunks = new List<int>(),
            LastActivity = SystemClock.Instance.GetCurrentInstant()
        };

        db.UploadTasks.Add(uploadTask);
        await db.SaveChangesAsync();

        await SetCacheAsync(uploadTask);
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

        var task = await db.Tasks
            .OfType<PersistentUploadTask>()
            .FirstOrDefaultAsync(t => t.TaskId == taskId && t.Status == TaskStatus.InProgress);

        if (task is not null)
            await SetCacheAsync(task);

        return task;
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

            task.UploadedChunks.Add(chunkIndex);
            task.ChunksUploaded = task.UploadedChunks.Count;
            task.LastActivity = SystemClock.Instance.GetCurrentInstant();

            await db.SaveChangesAsync();
            await SetCacheAsync(task);

            // Send real-time progress update
            var newProgress = task.ChunksCount > 0 ? (double)task.ChunksUploaded / task.ChunksCount * 100 : 0;
            await SendUploadProgressUpdateAsync(task, newProgress, previousProgress);
        }
    }

    /// <summary>
    /// Marks an upload task as completed
    /// </summary>
    public async Task MarkTaskCompletedAsync(string taskId)
    {
        var task = await GetUploadTaskAsync(taskId);
        if (task is null) return;

        task.Status = Model.TaskStatus.Completed;
        task.LastActivity = SystemClock.Instance.GetCurrentInstant();

        await db.SaveChangesAsync();
        await RemoveCacheAsync(taskId);
    }

    /// <summary>
    /// Marks an upload task as failed
    /// </summary>
    public async Task MarkTaskFailedAsync(string taskId)
    {
        var task = await GetUploadTaskAsync(taskId);
        if (task is null) return;

        task.Status = Model.TaskStatus.Failed;
        task.LastActivity = SystemClock.Instance.GetCurrentInstant();

        await db.SaveChangesAsync();
        await RemoveCacheAsync(taskId);
    }

    /// <summary>
    /// Gets all resumable tasks for an account
    /// </summary>
    public async Task<List<PersistentUploadTask>> GetResumableTasksAsync(Guid accountId)
    {
        return await db.Tasks
            .OfType<PersistentUploadTask>()
            .Where(t => t.AccountId == accountId &&
                       t.Status == Model.TaskStatus.InProgress &&
                       t.LastActivity > SystemClock.Instance.GetCurrentInstant() - Duration.FromHours(24))
            .OrderByDescending(t => t.LastActivity)
            .ToListAsync();
    }

    /// <summary>
    /// Gets user tasks with filtering and pagination
    /// </summary>
    public async Task<(List<PersistentUploadTask> Items, int TotalCount)> GetUserTasksAsync(
        Guid accountId,
        UploadTaskStatus? status = null,
        string? sortBy = "lastActivity",
        bool sortDescending = true,
        int offset = 0,
        int limit = 50
    )
    {
        var query = db.Tasks.OfType<PersistentUploadTask>().Where(t => t.AccountId == accountId);

        // Apply status filter
        if (status.HasValue)
        {
            query = query.Where(t => t.Status == (TaskStatus)status.Value);
        }

        // Get total count
        var totalCount = await query.CountAsync();

        // Apply sorting
        IOrderedQueryable<PersistentUploadTask> orderedQuery;
        switch (sortBy?.ToLower())
        {
            case "filename":
                orderedQuery = sortDescending
                    ? query.OrderByDescending(t => t.FileName)
                    : query.OrderBy(t => t.FileName);
                break;
            case "filesize":
                orderedQuery = sortDescending
                    ? query.OrderByDescending(t => t.FileSize)
                    : query.OrderBy(t => t.FileSize);
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
    /// Checks if a chunk has already been uploaded
    /// </summary>
    public async Task<bool> IsChunkUploadedAsync(string taskId, int chunkIndex)
    {
        var task = await GetUploadTaskAsync(taskId);
        return task?.UploadedChunks.Contains(chunkIndex) ?? false;
    }

    /// <summary>
    /// Cleans up expired/stale upload tasks
    /// </summary>
    public async Task CleanupStaleTasksAsync()
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var staleThreshold = now - Duration.FromHours(24); // 24 hours

        var staleTasks = await db.Tasks
            .OfType<PersistentUploadTask>()
            .Where(t => t.Status == Model.TaskStatus.InProgress &&
                       t.LastActivity < staleThreshold)
            .ToListAsync();

        foreach (var task in staleTasks)
        {
            task.Status = Model.TaskStatus.Expired;
            await RemoveCacheAsync(task.TaskId);

            // Clean up temp files
            var taskPath = Path.Combine(Path.GetTempPath(), "multipart-uploads", task.TaskId);
            if (Directory.Exists(taskPath))
            {
                try
                {
                    Directory.Delete(taskPath, true);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to cleanup temp files for task {TaskId}", task.TaskId);
                }
            }
        }

        await db.SaveChangesAsync();

        if (staleTasks.Any())
        {
            logger.LogInformation("Cleaned up {Count} stale upload tasks", staleTasks.Count);
        }
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

    private async Task SetCacheAsync(PersistentUploadTask task)
    {
        var cacheKey = $"{CacheKeyPrefix}{task.TaskId}";
        await cache.SetAsync(cacheKey, task, CacheDuration);
    }

    private async Task RemoveCacheAsync(string taskId)
    {
        var cacheKey = $"{CacheKeyPrefix}{taskId}";
        await cache.RemoveAsync(cacheKey);
    }

    /// <summary>
    /// Gets upload statistics for a user
    /// </summary>
    public async Task<UserUploadStats> GetUserUploadStatsAsync(Guid accountId)
    {
        var tasks = await db.Tasks
            .OfType<PersistentUploadTask>()
            .Where(t => t.AccountId == accountId)
            .ToListAsync();

        var stats = new UserUploadStats
        {
            TotalTasks = tasks.Count,
            InProgressTasks = tasks.Count(t => t.Status == Model.TaskStatus.InProgress),
            CompletedTasks = tasks.Count(t => t.Status == Model.TaskStatus.Completed),
            FailedTasks = tasks.Count(t => t.Status == Model.TaskStatus.Failed),
            ExpiredTasks = tasks.Count(t => t.Status == Model.TaskStatus.Expired),
            TotalUploadedBytes = tasks.Sum(t => (long)t.ChunksUploaded * t.ChunkSize),
            AverageProgress = tasks.Any(t => t.Status == Model.TaskStatus.InProgress)
                ? tasks.Where(t => t.Status == Model.TaskStatus.InProgress)
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
            .OfType<PersistentUploadTask>()
            .Where(t => t.AccountId == accountId &&
                       (t.Status == Model.TaskStatus.Failed || t.Status == Model.TaskStatus.Expired))
            .ToListAsync();

        foreach (var task in failedTasks)
        {
            await RemoveCacheAsync(task.TaskId);

            // Clean up temp files
            var taskPath = Path.Combine(Path.GetTempPath(), "multipart-uploads", task.TaskId);
            if (Directory.Exists(taskPath))
            {
                try
                {
                    Directory.Delete(taskPath, true);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to cleanup temp files for task {TaskId}", task.TaskId);
                }
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
        return await db.Tasks
            .OfType<PersistentUploadTask>()
            .Where(t => t.AccountId == accountId)
            .OrderByDescending(t => t.LastActivity)
            .Take(limit)
            .ToListAsync();
    }

    /// <summary>
    /// Sends real-time upload progress update via WebSocket
    /// </summary>
    private async Task SendUploadProgressUpdateAsync(PersistentUploadTask task, double newProgress, double previousProgress)
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
                LastActivity = task.LastActivity.ToString("O", null)
            };

            var packet = new WebSocketPacket
            {
                Type = "upload.progress",
                Data = Google.Protobuf.ByteString.CopyFromUtf8(System.Text.Json.JsonSerializer.Serialize(progressData))
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
                CompletedAt = SystemClock.Instance.GetCurrentInstant().ToString("O", null)
            };

            // Send WebSocket notification
            var wsPacket = new WebSocketPacket
            {
                Type = "upload.completed",
                Data = Google.Protobuf.ByteString.CopyFromUtf8(System.Text.Json.JsonSerializer.Serialize(completionData))
            };

            await ringService.PushWebSocketPacketAsync(new PushWebSocketPacketRequest
            {
                UserId = task.AccountId.ToString(),
                Packet = wsPacket
            });

            // Send push notification
            var pushNotification = new PushNotification
            {
                Topic = "upload",
                Title = "Upload Completed",
                Subtitle = task.FileName,
                Body = $"Your file '{task.FileName}' has been uploaded successfully.",
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
                FailedAt = SystemClock.Instance.GetCurrentInstant().ToString("O", null),
                ErrorMessage = errorMessage ?? "Upload failed due to an unknown error"
            };

            // Send WebSocket notification
            var wsPacket = new WebSocketPacket
            {
                Type = "upload.failed",
                Data = Google.Protobuf.ByteString.CopyFromUtf8(System.Text.Json.JsonSerializer.Serialize(failureData))
            };

            await ringService.PushWebSocketPacketAsync(new PushWebSocketPacketRequest
            {
                UserId = task.AccountId.ToString(),
                Packet = wsPacket
            });

            // Send push notification
            var pushNotification = new PushNotification
            {
                Topic = "upload",
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
}

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
