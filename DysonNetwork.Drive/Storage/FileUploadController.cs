using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using DysonNetwork.Drive.Billing;
using DysonNetwork.Drive.Storage.Model;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Http;
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
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
        {
            return new ObjectResult(ApiError.Unauthorized()) { StatusCode = 401 };
        }

        if (!currentUser.IsSuperuser)
        {
            var allowed = await permission.HasPermissionAsync(new HasPermissionRequest
            { Actor = $"user:{currentUser.Id}", Area = "global", Key = "files.create" });
            if (!allowed.HasPermission)
            {
                return new ObjectResult(ApiError.Unauthorized(forbidden: true)) { StatusCode = 403 };
            }
        }

        request.PoolId ??= Guid.Parse(configuration["Storage:PreferredRemote"]!);

        var pool = await fileService.GetPoolAsync(request.PoolId.Value);
        if (pool is null)
        {
            return new ObjectResult(ApiError.NotFound("Pool")) { StatusCode = 404 };
        }

        if (pool.PolicyConfig.RequirePrivilege is > 0)
        {
            var privilege =
                currentUser.PerkSubscription is null ? 0 :
                PerkSubscriptionPrivilege.GetPrivilegeFromIdentifier(currentUser.PerkSubscription.Identifier);
            if (privilege < pool.PolicyConfig.RequirePrivilege)
            {
                return new ObjectResult(ApiError.Unauthorized(
                    $"You need Stellar Program tier {pool.PolicyConfig.RequirePrivilege} to use pool {pool.Name}, you are tier {privilege}",
                    forbidden: true))
                {
                    StatusCode = 403
                };
            }
        }

        var policy = pool.PolicyConfig;
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
                    ApiError.Unauthorized($"Content type {request.ContentType} is not allowed by the pool's policy",
                        true))
                { StatusCode = 403 };
            }
        }

        if (policy.MaxFileSize is not null && request.FileSize > policy.MaxFileSize)
        {
            return new ObjectResult(ApiError.Unauthorized(
                $"File size {request.FileSize} is larger than the pool's maximum file size {policy.MaxFileSize}",
                true))
            {
                StatusCode = 403
            };
        }

        var (ok, billableUnit, quota) = await quotaService.IsFileAcceptable(
            Guid.Parse(currentUser.Id),
            pool.BillingConfig.CostMultiplier ?? 1.0,
            request.FileSize
        );
        if (!ok)
        {
            return new ObjectResult(
                ApiError.Unauthorized($"File size {billableUnit} MiB is exceeded the user's quota {quota} MiB",
                    true))
            { StatusCode = 403 };
        }

        if (!Directory.Exists(_tempPath))
        {
            Directory.CreateDirectory(_tempPath);
        }

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

        return Ok(new CreateUploadTaskResponse
        {
            FileExists = false,
            TaskId = taskId,
            ChunkSize = chunkSize,
            ChunksCount = chunksCount
        });
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
        {
            return new ObjectResult(ApiError.NotFound("Upload task")) { StatusCode = 404 };
        }

        var taskJsonPath = Path.Combine(taskPath, "task.json");
        if (!System.IO.File.Exists(taskJsonPath))
        {
            return new ObjectResult(ApiError.NotFound("Upload task metadata")) { StatusCode = 404 };
        }

        var task = JsonSerializer.Deserialize<UploadTask>(await System.IO.File.ReadAllTextAsync(taskJsonPath));
        if (task == null)
        {
            return new ObjectResult(new ApiError { Code = "BAD_REQUEST", Message = "Invalid task metadata.", Status = 400 })
            { StatusCode = 400 };
        }

        var mergedFilePath = Path.Combine(_tempPath, taskId + ".tmp");
        await using (var mergedStream = new FileStream(mergedFilePath, FileMode.Create))
        {
            for (var i = 0; i < task.ChunksCount; i++)
            {
                var chunkPath = Path.Combine(taskPath, $"{i}.chunk");
                if (!System.IO.File.Exists(chunkPath))
                {
                    // Clean up partially uploaded file
                    mergedStream.Close();
                    System.IO.File.Delete(mergedFilePath);
                    Directory.Delete(taskPath, true);
                    return new ObjectResult(new ApiError
                    { Code = "CHUNK_MISSING", Message = $"Chunk {i} is missing.", Status = 400 })
                    { StatusCode = 400 };
                }

                await using var chunkStream = new FileStream(chunkPath, FileMode.Open);
                await chunkStream.CopyToAsync(mergedStream);
            }
        }

        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
        {
            return new ObjectResult(ApiError.Unauthorized()) { StatusCode = 401 };
        }

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

        // Clean up
        Directory.Delete(taskPath, true);
        System.IO.File.Delete(mergedFilePath);

        return Ok(cloudFile);
    }
}
