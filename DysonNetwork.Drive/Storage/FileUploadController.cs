using System.Text.Json;
using DysonNetwork.Drive.Billing;
using DysonNetwork.Drive.Storage.Model;
using DysonNetwork.Shared.Auth;
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
        Path.Combine(configuration.GetValue<string>("Storage:Uploads") ?? Path.GetTempPath(), "multipart-uploads");

    private const long DefaultChunkSize = 1024 * 1024 * 5; // 5MB

    [HttpPost("create")]
    public async Task<IActionResult> CreateUploadTask([FromBody] CreateUploadTaskRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        if (!currentUser.IsSuperuser)
        {
            var allowed = await permission.HasPermissionAsync(new HasPermissionRequest
                { Actor = $"user:{currentUser.Id}", Area = "global", Key = "files.create" });
            if (!allowed.HasPermission)
            {
                return Forbid();
            }
        }

        if (!Guid.TryParse(request.PoolId, out var poolGuid))
        {
            return BadRequest("Invalid file pool id");
        }

        var pool = await fileService.GetPoolAsync(poolGuid);
        if (pool is null)
        {
            return BadRequest("Pool not found");
        }

        if (pool.PolicyConfig.RequirePrivilege > 0)
        {
            if (currentUser.PerkSubscription is null)
            {
                return new ObjectResult("You need to have join the Stellar Program to use this pool")
                    { StatusCode = 403 };
            }

            var privilege =
                PerkSubscriptionPrivilege.GetPrivilegeFromIdentifier(currentUser.PerkSubscription.Identifier);
            if (privilege < pool.PolicyConfig.RequirePrivilege)
            {
                return new ObjectResult(
                    $"You need Stellar Program tier {pool.PolicyConfig.RequirePrivilege} to use this pool, you are tier {privilege}")
                {
                    StatusCode = 403
                };
            }
        }

        if (!string.IsNullOrEmpty(request.BundleId) && !Guid.TryParse(request.BundleId, out _))
        {
            return BadRequest("Invalid file bundle id");
        }

        var policy = pool.PolicyConfig;
        if (!policy.AllowEncryption && !string.IsNullOrEmpty(request.EncryptPassword))
        {
            return new ObjectResult("File encryption is not allowed in this pool") { StatusCode = 403 };
        }

        if (policy.AcceptTypes is { Count: > 0 })
        {
            if (string.IsNullOrEmpty(request.ContentType))
            {
                return BadRequest("Content type is required by the pool's policy");
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
                return new ObjectResult($"Content type {request.ContentType} is not allowed by the pool's policy")
                    { StatusCode = 403 };
            }
        }

        if (policy.MaxFileSize is not null && request.FileSize > policy.MaxFileSize)
        {
            return new ObjectResult(
                $"File size {request.FileSize} is larger than the pool's maximum file size {policy.MaxFileSize}")
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
            return new ObjectResult($"File size {billableUnit} MiB is exceeded the user's quota {quota} MiB")
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
            PoolId = request.PoolId,
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

    [HttpPost("chunk/{taskId}/{chunkIndex}")]
    [RequestSizeLimit(DefaultChunkSize + 1024 * 1024)] // 6MB to be safe
    [RequestFormLimits(MultipartBodyLengthLimit = DefaultChunkSize + 1024 * 1024)]
    public async Task<IActionResult> UploadChunk(string taskId, int chunkIndex, [FromForm] IFormFile chunk)
    {
        var taskPath = Path.Combine(_tempPath, taskId);
        if (!Directory.Exists(taskPath))
        {
            return NotFound("Upload task not found.");
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
            return NotFound("Upload task not found.");
        }

        var taskJsonPath = Path.Combine(taskPath, "task.json");
        if (!System.IO.File.Exists(taskJsonPath))
        {
            return NotFound("Upload task metadata not found.");
        }

        var task = JsonSerializer.Deserialize<UploadTask>(await System.IO.File.ReadAllTextAsync(taskJsonPath));
        if (task == null)
        {
            return BadRequest("Invalid task metadata.");
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
                    return BadRequest($"Chunk {i} is missing.");
                }

                await using var chunkStream = new FileStream(chunkPath, FileMode.Open);
                await chunkStream.CopyToAsync(mergedStream);
            }
        }

        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var fileId = await Nanoid.GenerateAsync();

        await using (var fileStream =
                     new FileStream(mergedFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var cloudFile = await fileService.ProcessNewFileAsync(
                currentUser,
                fileId,
                task.PoolId,
                task.BundleId,
                fileStream,
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
}