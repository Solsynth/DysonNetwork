using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using DysonNetwork.Drive.Billing;
using DysonNetwork.Drive.Storage.Model;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Http;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NanoidDotNet;
using NodaTime;
using TaskStatus = DysonNetwork.Drive.Storage.Model.TaskStatus;

namespace DysonNetwork.Drive.Storage;

[ApiController]
[Route("/api/files/upload")]
[Authorize]
public class FileUploadController(
    IConfiguration configuration,
    FileService fileService,
    AppDatabase db,
    PermissionService.PermissionServiceClient permission,
    QuotaService quotaService,
    PersistentUploadService persistentUploadService
)
    : ControllerBase
{
    private readonly string _tempPath =
        configuration.GetValue<string>("Storage:Uploads") ?? Path.Combine(Path.GetTempPath(), "multipart-uploads");

    private const long DefaultChunkSize = 1024 * 1024 * 5; // 5MB

    [HttpPost("create")]
    public async Task<IActionResult> CreateUploadTask([FromBody] CreateUploadTaskRequest request)
    {
        var currentUser = HttpContext.Items["CurrentUser"] as Account;
        if (currentUser is null)
            return new ObjectResult(ApiError.Unauthorized()) { StatusCode = 401 };

        var permissionCheck = await ValidateUserPermissions(currentUser);
        if (permissionCheck is not null) return permissionCheck;

        request.PoolId ??= Guid.Parse(configuration["Storage:PreferredRemote"]!);

        var pool = await fileService.GetPoolAsync(request.PoolId.Value);
        if (pool is null)
            return new ObjectResult(ApiError.NotFound("Pool")) { StatusCode = 404 };

        var poolValidation = await ValidatePoolAccess(currentUser, pool, request);
        if (poolValidation is not null) return poolValidation;

        var policyValidation = ValidatePoolPolicy(pool.PolicyConfig, request);
        if (policyValidation is not null) return policyValidation;

        var quotaValidation = await ValidateQuota(currentUser, pool, request.FileSize);
        if (quotaValidation is not null) return quotaValidation;

        EnsureTempDirectoryExists();

        // Check if a file with the same hash already exists
        var existingFile = await db.Files.FirstOrDefaultAsync(f => f.Hash == request.Hash);
        if (existingFile != null)
        {
            return Ok(new CreateUploadTaskResponse
            {
                FileExists = true,
                File = existingFile
            });
        }

        var accountId = Guid.Parse(currentUser.Id);
        var taskId = await Nanoid.GenerateAsync();

        // Create persistent upload task
        var persistentTask = await persistentUploadService.CreateUploadTaskAsync(taskId, request, accountId);

        return Ok(new CreateUploadTaskResponse
        {
            FileExists = false,
            TaskId = taskId,
            ChunkSize = persistentTask.ChunkSize,
            ChunksCount = persistentTask.ChunksCount
        });
    }

    private async Task<IActionResult?> ValidateUserPermissions(Account currentUser)
    {
        if (currentUser.IsSuperuser) return null;

        var allowed = await permission.HasPermissionAsync(new HasPermissionRequest
        { Actor = $"user:{currentUser.Id}", Area = "global", Key = "files.create" });

        return allowed.HasPermission ? null :
            new ObjectResult(ApiError.Unauthorized(forbidden: true)) { StatusCode = 403 };
    }

    private async Task<IActionResult?> ValidatePoolAccess(Account currentUser, FilePool pool, CreateUploadTaskRequest request)
    {
        if (pool.PolicyConfig.RequirePrivilege <= 0) return null;

        var privilege = currentUser.PerkSubscription is null ? 0 :
            PerkSubscriptionPrivilege.GetPrivilegeFromIdentifier(currentUser.PerkSubscription.Identifier);

        if (privilege < pool.PolicyConfig.RequirePrivilege)
        {
            return new ObjectResult(ApiError.Unauthorized(
                $"You need Stellar Program tier {pool.PolicyConfig.RequirePrivilege} to use pool {pool.Name}, you are tier {privilege}",
                forbidden: true))
            { StatusCode = 403 };
        }

        return null;
    }

    private IActionResult? ValidatePoolPolicy(PolicyConfig policy, CreateUploadTaskRequest request)
    {
        if (!policy.AllowEncryption && !string.IsNullOrEmpty(request.EncryptPassword))
        {
            return new ObjectResult(ApiError.Unauthorized("File encryption is not allowed in this pool", true))
            { StatusCode = 403 };
        }

        if (policy.AcceptTypes is { Count: > 0 })
        {
            if (string.IsNullOrEmpty(request.ContentType))
            {
                return new ObjectResult(ApiError.Validation(new Dictionary<string, string[]>
                {
                    { "contentType", new[] { "Content type is required by the pool's policy" } }
                }))
                { StatusCode = 400 };
            }

            var foundMatch = policy.AcceptTypes.Any(acceptType =>
            {
                if (acceptType.EndsWith("/*", StringComparison.OrdinalIgnoreCase))
                {
                    var type = acceptType[..^2];
                    return request.ContentType.StartsWith($"{type}/", StringComparison.OrdinalIgnoreCase);
                }

                return acceptType.Equals(request.ContentType, StringComparison.OrdinalIgnoreCase);
            });

            if (!foundMatch)
            {
                return new ObjectResult(
                    ApiError.Unauthorized($"Content type {request.ContentType} is not allowed by the pool's policy", true))
                { StatusCode = 403 };
            }
        }

        if (policy.MaxFileSize is not null && request.FileSize > policy.MaxFileSize)
        {
            return new ObjectResult(ApiError.Unauthorized(
                $"File size {request.FileSize} is larger than the pool's maximum file size {policy.MaxFileSize}", true))
            { StatusCode = 403 };
        }

        return null;
    }

    private async Task<IActionResult?> ValidateQuota(Account currentUser, FilePool pool, long fileSize)
    {
        var (ok, billableUnit, quota) = await quotaService.IsFileAcceptable(
            Guid.Parse(currentUser.Id),
            pool.BillingConfig.CostMultiplier ?? 1.0,
            fileSize
        );

        if (!ok)
        {
            return new ObjectResult(
                ApiError.Unauthorized($"File size {billableUnit} MiB is exceeded the user's quota {quota} MiB", true))
            { StatusCode = 403 };
        }

        return null;
    }

    private void EnsureTempDirectoryExists()
    {
        if (!Directory.Exists(_tempPath))
        {
            Directory.CreateDirectory(_tempPath);
        }
    }

    private async Task<(string taskId, UploadTask task)> CreateUploadTaskInternal(CreateUploadTaskRequest request)
    {
        var taskId = await Nanoid.GenerateAsync();
        var taskPath = Path.Combine(_tempPath, taskId);
        Directory.CreateDirectory(taskPath);

        var chunkSize = request.ChunkSize ?? DefaultChunkSize;
        var chunksCount = (int)Math.Ceiling((double)request.FileSize / chunkSize);

        var task = new UploadTask
        {
            TaskId = taskId,
            FileName = request.FileName,
            FileSize = request.FileSize,
            ContentType = request.ContentType,
            ChunkSize = chunkSize,
            ChunksCount = chunksCount,
            PoolId = request.PoolId.Value,
            BundleId = request.BundleId,
            EncryptPassword = request.EncryptPassword,
            ExpiredAt = request.ExpiredAt,
            Hash = request.Hash,
        };

        await System.IO.File.WriteAllTextAsync(Path.Combine(taskPath, "task.json"), JsonSerializer.Serialize(task));
        return (taskId, task);
    }

    public class UploadChunkRequest
    {
        [Required]
        public IFormFile Chunk { get; set; } = null!;
    }

    [HttpPost("chunk/{taskId}/{chunkIndex}")]
    [RequestSizeLimit(DefaultChunkSize + 1024 * 1024)] // 6MB to be safe
    [RequestFormLimits(MultipartBodyLengthLimit = DefaultChunkSize + 1024 * 1024)]
    public async Task<IActionResult> UploadChunk(string taskId, int chunkIndex, [FromForm] UploadChunkRequest request)
    {
        var chunk = request.Chunk;

        // Check if chunk is already uploaded (resumable upload)
        if (await persistentUploadService.IsChunkUploadedAsync(taskId, chunkIndex))
        {
            return Ok(new { message = "Chunk already uploaded" });
        }

        var taskPath = Path.Combine(_tempPath, taskId);
        if (!Directory.Exists(taskPath))
        {
            Directory.CreateDirectory(taskPath);
        }

        var chunkPath = Path.Combine(taskPath, $"{chunkIndex}.chunk");
        await using var stream = new FileStream(chunkPath, FileMode.Create);
        await chunk.CopyToAsync(stream);

        // Update persistent task progress
        await persistentUploadService.UpdateChunkProgressAsync(taskId, chunkIndex);

        return Ok();
    }

    [HttpPost("complete/{taskId}")]
    public async Task<IActionResult> CompleteUpload(string taskId)
    {
        // Get persistent task
        var persistentTask = await persistentUploadService.GetUploadTaskAsync(taskId);
        if (persistentTask is null)
            return new ObjectResult(ApiError.NotFound("Upload task")) { StatusCode = 404 };

        var currentUser = HttpContext.Items["CurrentUser"] as Account;
        if (currentUser is null)
            return new ObjectResult(ApiError.Unauthorized()) { StatusCode = 401 };

        // Verify ownership
        if (persistentTask.AccountId != Guid.Parse(currentUser.Id))
            return new ObjectResult(ApiError.Unauthorized(forbidden: true)) { StatusCode = 403 };

        var taskPath = Path.Combine(_tempPath, taskId);
        if (!Directory.Exists(taskPath))
            return new ObjectResult(ApiError.NotFound("Upload task directory")) { StatusCode = 404 };

        var mergedFilePath = Path.Combine(_tempPath, taskId + ".tmp");

        try
        {
            await MergeChunks(taskPath, mergedFilePath, persistentTask.ChunksCount);

            var fileId = await Nanoid.GenerateAsync();
            var cloudFile = await fileService.ProcessNewFileAsync(
                currentUser,
                fileId,
                persistentTask.PoolId.ToString(),
                persistentTask.BundleId?.ToString(),
                mergedFilePath,
                persistentTask.FileName,
                persistentTask.ContentType,
                persistentTask.EncryptPassword,
                persistentTask.ExpiredAt
            );

            // Mark task as completed
            await persistentUploadService.MarkTaskCompletedAsync(taskId);

            // Send completion notification
            await persistentUploadService.SendUploadCompletedNotificationAsync(persistentTask, fileId);

            return Ok(cloudFile);
        }
        catch (Exception ex)
        {
            // Mark task as failed
            await persistentUploadService.MarkTaskFailedAsync(taskId);

            // Send failure notification
            await persistentUploadService.SendUploadFailedNotificationAsync(persistentTask, ex.Message);

            await CleanupTempFiles(taskPath, mergedFilePath);

            return new ObjectResult(new ApiError
            {
                Code = "UPLOAD_FAILED",
                Message = "Failed to complete file upload.",
                Status = 500
            }) { StatusCode = 500 };
        }
        finally
        {
            // Always clean up temp files
            await CleanupTempFiles(taskPath, mergedFilePath);
        }
    }

    private async Task MergeChunks(string taskPath, string mergedFilePath, int chunksCount)
    {
        await using var mergedStream = new FileStream(mergedFilePath, FileMode.Create);

        for (var i = 0; i < chunksCount; i++)
        {
            var chunkPath = Path.Combine(taskPath, $"{i}.chunk");
            if (!System.IO.File.Exists(chunkPath))
            {
                throw new InvalidOperationException($"Chunk {i} is missing.");
            }

            await using var chunkStream = new FileStream(chunkPath, FileMode.Open);
            await chunkStream.CopyToAsync(mergedStream);
        }
    }

    private async Task CleanupTempFiles(string taskPath, string mergedFilePath)
    {
        try
        {
            if (Directory.Exists(taskPath))
                Directory.Delete(taskPath, true);

            if (System.IO.File.Exists(mergedFilePath))
                System.IO.File.Delete(mergedFilePath);
        }
        catch
        {
            // Ignore cleanup errors to avoid masking the original exception
        }
    }

    // New endpoints for resumable uploads

    [HttpGet("tasks")]
    public async Task<IActionResult> GetMyUploadTasks(
        [FromQuery] UploadTaskStatus? status = null,
        [FromQuery] string? sortBy = "lastActivity",
        [FromQuery] bool sortDescending = true,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 50
    )
    {
        var currentUser = HttpContext.Items["CurrentUser"] as Account;
        if (currentUser is null)
            return new ObjectResult(ApiError.Unauthorized()) { StatusCode = 401 };

        var accountId = Guid.Parse(currentUser.Id);
        var tasks = await persistentUploadService.GetUserTasksAsync(accountId, status, sortBy, sortDescending, offset, limit);

        Response.Headers.Append("X-Total", tasks.TotalCount.ToString());

        return Ok(tasks.Items.Select(t => new
        {
            t.TaskId,
            t.FileName,
            t.FileSize,
            t.ContentType,
            t.ChunkSize,
            t.ChunksCount,
            t.ChunksUploaded,
            Progress = t.ChunksCount > 0 ? (double)t.ChunksUploaded / t.ChunksCount * 100 : 0,
            t.Status,
            t.LastActivity,
            t.CreatedAt,
            t.UpdatedAt,
            UploadedChunks = t.UploadedChunks,
            Pool = new { t.PoolId, Name = "Pool Name" }, // Could be expanded to include pool details
            Bundle = t.BundleId.HasValue ? new { t.BundleId } : null
        }));
    }

    [HttpGet("progress/{taskId}")]
    public async Task<IActionResult> GetUploadProgress(string taskId)
    {
        var currentUser = HttpContext.Items["CurrentUser"] as Account;
        if (currentUser is null)
            return new ObjectResult(ApiError.Unauthorized()) { StatusCode = 401 };

        var task = await persistentUploadService.GetUploadTaskAsync(taskId);
        if (task is null)
            return new ObjectResult(ApiError.NotFound("Upload task")) { StatusCode = 404 };

        // Verify ownership
        if (task.AccountId != Guid.Parse(currentUser.Id))
            return new ObjectResult(ApiError.Unauthorized(forbidden: true)) { StatusCode = 403 };

        var progress = await persistentUploadService.GetUploadProgressAsync(taskId);

        return Ok(new
        {
            task.TaskId,
            task.FileName,
            task.FileSize,
            task.ChunksCount,
            task.ChunksUploaded,
            Progress = progress,
            task.Status,
            task.LastActivity,
            task.UploadedChunks
        });
    }

    [HttpGet("resume/{taskId}")]
    public async Task<IActionResult> ResumeUploadTask(string taskId)
    {
        var currentUser = HttpContext.Items["CurrentUser"] as Account;
        if (currentUser is null)
            return new ObjectResult(ApiError.Unauthorized()) { StatusCode = 401 };

        var task = await persistentUploadService.GetUploadTaskAsync(taskId);
        if (task is null)
            return new ObjectResult(ApiError.NotFound("Upload task")) { StatusCode = 404 };

        // Verify ownership
        if (task.AccountId != Guid.Parse(currentUser.Id))
            return new ObjectResult(ApiError.Unauthorized(forbidden: true)) { StatusCode = 403 };

        // Ensure temp directory exists
        var taskPath = Path.Combine(_tempPath, taskId);
        if (!Directory.Exists(taskPath))
        {
            Directory.CreateDirectory(taskPath);
        }

        return Ok(new
        {
            task.TaskId,
            task.FileName,
            task.FileSize,
            task.ContentType,
            task.ChunkSize,
            task.ChunksCount,
            task.ChunksUploaded,
            UploadedChunks = task.UploadedChunks,
            Progress = task.ChunksCount > 0 ? (double)task.ChunksUploaded / task.ChunksCount * 100 : 0
        });
    }

    [HttpDelete("task/{taskId}")]
    public async Task<IActionResult> CancelUploadTask(string taskId)
    {
        var currentUser = HttpContext.Items["CurrentUser"] as Account;
        if (currentUser is null)
            return new ObjectResult(ApiError.Unauthorized()) { StatusCode = 401 };

        var task = await persistentUploadService.GetUploadTaskAsync(taskId);
        if (task is null)
            return new ObjectResult(ApiError.NotFound("Upload task")) { StatusCode = 404 };

        // Verify ownership
        if (task.AccountId != Guid.Parse(currentUser.Id))
            return new ObjectResult(ApiError.Unauthorized(forbidden: true)) { StatusCode = 403 };

        // Mark as failed (cancelled)
        await persistentUploadService.MarkTaskFailedAsync(taskId);

        // Clean up temp files
        var taskPath = Path.Combine(_tempPath, taskId);
        await CleanupTempFiles(taskPath, string.Empty);

        return Ok(new { message = "Upload task cancelled" });
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetUploadStats()
    {
        var currentUser = HttpContext.Items["CurrentUser"] as Account;
        if (currentUser is null)
            return new ObjectResult(ApiError.Unauthorized()) { StatusCode = 401 };

        var accountId = Guid.Parse(currentUser.Id);
        var stats = await persistentUploadService.GetUserUploadStatsAsync(accountId);

        return Ok(new
        {
            TotalTasks = stats.TotalTasks,
            InProgressTasks = stats.InProgressTasks,
            CompletedTasks = stats.CompletedTasks,
            FailedTasks = stats.FailedTasks,
            ExpiredTasks = stats.ExpiredTasks,
            TotalUploadedBytes = stats.TotalUploadedBytes,
            AverageProgress = stats.AverageProgress,
            RecentActivity = stats.RecentActivity
        });
    }

    [HttpDelete("tasks/cleanup")]
    public async Task<IActionResult> CleanupFailedTasks()
    {
        var currentUser = HttpContext.Items["CurrentUser"] as Account;
        if (currentUser is null)
            return new ObjectResult(ApiError.Unauthorized()) { StatusCode = 401 };

        var accountId = Guid.Parse(currentUser.Id);
        var cleanedCount = await persistentUploadService.CleanupUserFailedTasksAsync(accountId);

        return Ok(new { message = $"Cleaned up {cleanedCount} failed tasks" });
    }

    [HttpGet("tasks/recent")]
    public async Task<IActionResult> GetRecentTasks([FromQuery] int limit = 10)
    {
        var currentUser = HttpContext.Items["CurrentUser"] as Account;
        if (currentUser is null)
            return new ObjectResult(ApiError.Unauthorized()) { StatusCode = 401 };

        var accountId = Guid.Parse(currentUser.Id);
        var tasks = await persistentUploadService.GetRecentUserTasksAsync(accountId, limit);

        return Ok(tasks.Select(t => new
        {
            t.TaskId,
            t.FileName,
            t.FileSize,
            t.ContentType,
            Progress = t.ChunksCount > 0 ? (double)t.ChunksUploaded / t.ChunksCount * 100 : 0,
            t.Status,
            t.LastActivity,
            t.CreatedAt
        }));
    }

    [HttpGet("tasks/{taskId}/details")]
    public async Task<IActionResult> GetTaskDetails(string taskId)
    {
        var currentUser = HttpContext.Items["CurrentUser"] as Account;
        if (currentUser is null)
            return new ObjectResult(ApiError.Unauthorized()) { StatusCode = 401 };

        var task = await persistentUploadService.GetUploadTaskAsync(taskId);
        if (task is null)
            return new ObjectResult(ApiError.NotFound("Upload task")) { StatusCode = 404 };

        // Verify ownership
        if (task.AccountId != Guid.Parse(currentUser.Id))
            return new ObjectResult(ApiError.Unauthorized(forbidden: true)) { StatusCode = 403 };

        // Get pool information
        var pool = await fileService.GetPoolAsync(task.PoolId);
        var bundle = task.BundleId.HasValue
            ? await db.Bundles.FirstOrDefaultAsync(b => b.Id == task.BundleId.Value)
            : null;

        return Ok(new
        {
            Task = new
            {
                task.TaskId,
                task.FileName,
                task.FileSize,
                task.ContentType,
                task.ChunkSize,
                task.ChunksCount,
                task.ChunksUploaded,
                Progress = task.ChunksCount > 0 ? (double)task.ChunksUploaded / task.ChunksCount * 100 : 0,
                task.Status,
                task.LastActivity,
                task.CreatedAt,
                task.UpdatedAt,
                task.ExpiredAt,
                task.Hash,
                UploadedChunks = task.UploadedChunks
            },
            Pool = pool != null ? new
            {
                pool.Id,
                pool.Name,
                pool.Description
            } : null,
            Bundle = bundle != null ? new
            {
                bundle.Id,
                bundle.Name,
                bundle.Description
            } : null,
            EstimatedTimeRemaining = CalculateEstimatedTime(task),
            UploadSpeed = CalculateUploadSpeed(task)
        });
    }

    private string? CalculateEstimatedTime(PersistentUploadTask task)
    {
        if (task.Status != Model.TaskStatus.InProgress || task.ChunksUploaded == 0)
            return null;

        var elapsed = NodaTime.SystemClock.Instance.GetCurrentInstant() - task.CreatedAt;
        var elapsedSeconds = elapsed.TotalSeconds;
        var chunksPerSecond = task.ChunksUploaded / elapsedSeconds;
        var remainingChunks = task.ChunksCount - task.ChunksUploaded;

        if (chunksPerSecond <= 0)
            return null;

        var remainingSeconds = remainingChunks / chunksPerSecond;

        if (remainingSeconds < 60)
            return $"{remainingSeconds:F0} seconds";
        if (remainingSeconds < 3600)
            return $"{remainingSeconds / 60:F0} minutes";
        return $"{remainingSeconds / 3600:F1} hours";
    }

    private string? CalculateUploadSpeed(PersistentUploadTask task)
    {
        if (task.ChunksUploaded == 0)
            return null;

        var elapsed = NodaTime.SystemClock.Instance.GetCurrentInstant() - task.CreatedAt;
        var elapsedSeconds = elapsed.TotalSeconds;
        var bytesUploaded = (long)task.ChunksUploaded * task.ChunkSize;
        var bytesPerSecond = bytesUploaded / elapsedSeconds;

        if (bytesPerSecond < 1024)
            return $"{bytesPerSecond:F0} B/s";
        if (bytesPerSecond < 1024 * 1024)
            return $"{bytesPerSecond / 1024:F0} KB/s";
        return $"{bytesPerSecond / (1024 * 1024):F1} MB/s";
    }
}
