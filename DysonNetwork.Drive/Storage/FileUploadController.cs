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

namespace DysonNetwork.Drive.Storage;

[ApiController]
[Route("/api/files/upload")]
[Authorize]
public class FileUploadController(
    IConfiguration configuration,
    FileService fileService,
    AppDatabase db,
    PermissionService.PermissionServiceClient permission,
    QuotaService quotaService
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

        var (taskId, task) = await CreateUploadTaskInternal(request);
        return Ok(new CreateUploadTaskResponse
        {
            FileExists = false,
            TaskId = taskId,
            ChunkSize = task.ChunkSize,
            ChunksCount = task.ChunksCount
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
        var taskPath = Path.Combine(_tempPath, taskId);
        if (!Directory.Exists(taskPath))
        {
            return new ObjectResult(ApiError.NotFound("Upload task")) { StatusCode = 404 };
        }

        var chunkPath = Path.Combine(taskPath, $"{chunkIndex}.chunk");
        await using var stream = new FileStream(chunkPath, FileMode.Create);
        await chunk.CopyToAsync(stream);

        return Ok();
    }

    [HttpPost("complete/{taskId}")]
    public async Task<IActionResult> CompleteUpload(string taskId)
    {
        var taskPath = Path.Combine(_tempPath, taskId);
        if (!Directory.Exists(taskPath))
            return new ObjectResult(ApiError.NotFound("Upload task")) { StatusCode = 404 };

        var taskJsonPath = Path.Combine(taskPath, "task.json");
        if (!System.IO.File.Exists(taskJsonPath))
            return new ObjectResult(ApiError.NotFound("Upload task metadata")) { StatusCode = 404 };

        var task = JsonSerializer.Deserialize<UploadTask>(await System.IO.File.ReadAllTextAsync(taskJsonPath));
        if (task == null)
            return new ObjectResult(new ApiError { Code = "BAD_REQUEST", Message = "Invalid task metadata.", Status = 400 })
            { StatusCode = 400 };

        var currentUser = HttpContext.Items["CurrentUser"] as Account;
        if (currentUser is null)
            return new ObjectResult(ApiError.Unauthorized()) { StatusCode = 401 };

        var mergedFilePath = Path.Combine(_tempPath, taskId + ".tmp");

        try
        {
            await MergeChunks(taskPath, mergedFilePath, task.ChunksCount);

            var fileId = await Nanoid.GenerateAsync();
            var cloudFile = await fileService.ProcessNewFileAsync(
                currentUser,
                fileId,
                task.PoolId.ToString(),
                task.BundleId?.ToString(),
                mergedFilePath,
                task.FileName,
                task.ContentType,
                task.EncryptPassword,
                task.ExpiredAt
            );

            return Ok(cloudFile);
        }
        catch (Exception)
        {
            // Log the error and clean up
            // (Assuming you have a logger - you might want to inject ILogger)
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
}
