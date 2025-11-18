using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Minio.DataModel.Args;

namespace DysonNetwork.Drive.Storage;

[ApiController]
[Route("/api/files")]
public class FileController(
    AppDatabase db,
    FileService fs,
    IConfiguration configuration,
    IWebHostEnvironment env,
    FileReferenceService fileReferenceService
) : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<ActionResult> OpenFile(
        string id,
        [FromQuery] bool download = false,
        [FromQuery] bool original = false,
        [FromQuery] bool thumbnail = false,
        [FromQuery] string? overrideMimeType = null,
        [FromQuery] string? passcode = null
    )
    {
        var (fileId, fileExtension) = ParseFileId(id);
        var file = await fs.GetFileAsync(fileId);
        if (file is null) return NotFound("File not found.");

        var accessResult = await ValidateFileAccess(file, passcode);
        if (accessResult is not null) return accessResult;

        // Handle direct storage URL redirect
        if (!string.IsNullOrWhiteSpace(file.StorageUrl))
            return Redirect(file.StorageUrl);

        // Handle files not yet uploaded to remote storage
        if (file.UploadedAt is null)
            return await ServeLocalFile(file);

        // Handle uploaded files
        return await ServeRemoteFile(file, fileExtension, download, original, thumbnail, overrideMimeType);
    }

    private (string fileId, string? extension) ParseFileId(string id)
    {
        if (!id.Contains('.')) return (id, null);

        var parts = id.Split('.');
        return (parts.First(), parts.Last());
    }

    private async Task<ActionResult?> ValidateFileAccess(SnCloudFile file, string? passcode)
    {
        if (file.Bundle is not null && !file.Bundle.VerifyPasscode(passcode))
            return StatusCode(StatusCodes.Status403Forbidden, "The passcode is incorrect.");
        return null;
    }

    private Task<ActionResult> ServeLocalFile(SnCloudFile file)
    {
        // Try temp storage first
        var tempFilePath = Path.Combine(Path.GetTempPath(), file.Id);
        if (System.IO.File.Exists(tempFilePath))
        {
            if (file.IsEncrypted)
                return Task.FromResult<ActionResult>(StatusCode(StatusCodes.Status403Forbidden,
                    "Encrypted files cannot be accessed before they are processed and stored."));

            return Task.FromResult<ActionResult>(PhysicalFile(tempFilePath, file.MimeType ?? "application/octet-stream",
                file.Name, enableRangeProcessing: true));
        }

        // Fallback for tus uploads
        var tusStorePath = configuration.GetValue<string>("Storage:Uploads");
        if (string.IsNullOrEmpty(tusStorePath))
            return Task.FromResult<ActionResult>(StatusCode(StatusCodes.Status400BadRequest,
                "File is being processed. Please try again later."));
        var tusFilePath = Path.Combine(env.ContentRootPath, tusStorePath, file.Id);
        return System.IO.File.Exists(tusFilePath)
            ? Task.FromResult<ActionResult>(PhysicalFile(tusFilePath, file.MimeType ?? "application/octet-stream",
                file.Name, enableRangeProcessing: true))
            : Task.FromResult<ActionResult>(StatusCode(StatusCodes.Status400BadRequest,
                "File is being processed. Please try again later."));
    }

    private async Task<ActionResult> ServeRemoteFile(
        SnCloudFile file,
        string? fileExtension,
        bool download,
        bool original,
        bool thumbnail,
        string? overrideMimeType
    )
    {
        if (!file.PoolId.HasValue)
            return StatusCode(StatusCodes.Status500InternalServerError,
                "File is in an inconsistent state: uploaded but no pool ID.");

        var pool = await fs.GetPoolAsync(file.PoolId.Value);
        if (pool is null)
            return StatusCode(StatusCodes.Status410Gone, "The pool of the file no longer exists or not accessible.");

        if (!pool.PolicyConfig.AllowAnonymous && HttpContext.Items["CurrentUser"] is not Account)
            return Unauthorized();

        var dest = pool.StorageConfig;
        var fileName = BuildRemoteFileName(file, original, thumbnail);

        // Try proxy redirects first
        var proxyResult = TryProxyRedirect(file, dest, fileName);
        if (proxyResult is not null) return proxyResult;

        // Handle signed URLs
        if (dest.EnableSigned)
            return await CreateSignedUrl(file, dest, fileName, fileExtension, download, overrideMimeType);

        // Fallback to direct S3 endpoint
        var protocol = dest.EnableSsl ? "https" : "http";
        return Redirect($"{protocol}://{dest.Endpoint}/{dest.Bucket}/{fileName}");
    }

    private string BuildRemoteFileName(SnCloudFile file, bool original, bool thumbnail)
    {
        var fileName = string.IsNullOrWhiteSpace(file.StorageId) ? file.Id : file.StorageId;

        if (thumbnail)
        {
            if (!file.HasThumbnail) throw new InvalidOperationException("Thumbnail not available");
            fileName += ".thumbnail";
        }
        else if (!original && file.HasCompression)
        {
            fileName += ".compressed";
        }

        return fileName;
    }

    private ActionResult? TryProxyRedirect(SnCloudFile file, RemoteStorageConfig dest, string fileName)
    {
        if (dest.ImageProxy is not null && (file.MimeType?.StartsWith("image/") ?? false))
        {
            return Redirect(BuildProxyUrl(dest.ImageProxy, fileName));
        }

        return dest.AccessProxy is not null ? Redirect(BuildProxyUrl(dest.AccessProxy, fileName)) : null;
    }

    private static string BuildProxyUrl(string proxyUrl, string fileName)
    {
        var baseUri = new Uri(proxyUrl.EndsWith('/') ? proxyUrl : $"{proxyUrl}/");
        var fullUri = new Uri(baseUri, fileName);
        return fullUri.ToString();
    }

    private async Task<ActionResult> CreateSignedUrl(
        SnCloudFile file,
        RemoteStorageConfig dest,
        string fileName,
        string? fileExtension,
        bool download,
        string? overrideMimeType
    )
    {
        var client = fs.CreateMinioClient(dest);
        if (client is null)
            return BadRequest("Failed to configure client for remote destination, file got an invalid storage remote.");

        var headers = BuildSignedUrlHeaders(file, fileExtension, overrideMimeType, download);

        var openUrl = await client.PresignedGetObjectAsync(
            new PresignedGetObjectArgs()
                .WithBucket(dest.Bucket)
                .WithObject(fileName)
                .WithExpiry(3600)
                .WithHeaders(headers)
        );

        return Redirect(openUrl);
    }

    private static Dictionary<string, string> BuildSignedUrlHeaders(
        SnCloudFile file,
        string? fileExtension,
        string? overrideMimeType,
        bool download
    )
    {
        var headers = new Dictionary<string, string>();

        string? contentType = null;
        if (fileExtension is not null && MimeTypes.TryGetMimeType(fileExtension, out var mimeType))
        {
            contentType = mimeType;
        }
        else if (overrideMimeType is not null)
        {
            contentType = overrideMimeType;
        }
        else if (file.MimeType is not null && !file.MimeType.EndsWith("unknown"))
        {
            contentType = file.MimeType;
        }

        if (contentType is not null)
        {
            headers.Add("Response-Content-Type", contentType);
        }

        if (download)
        {
            headers.Add("Response-Content-Disposition", $"attachment; filename=\"{file.Name}\"");
        }

        return headers;
    }

    [HttpGet("{id}/info")]
    public async Task<ActionResult<SnCloudFile>> GetFileInfo(string id)
    {
        var file = await fs.GetFileAsync(id);
        if (file is null) return NotFound("File not found.");

        return file;
    }

    [HttpGet("{id}/references")]
    public async Task<ActionResult<List<Shared.Models.CloudFileReference>>> GetFileReferences(string id)
    {
        var file = await fs.GetFileAsync(id);
        if (file is null) return NotFound("File not found.");

        // Check if user has access to the file
        var accessResult = await ValidateFileAccess(file, null);
        if (accessResult is not null) return accessResult;

        // Get references using the injected FileReferenceService
        var references = await fileReferenceService.GetReferencesAsync(id);
        return Ok(references);
    }

    [Authorize]
    [HttpPatch("{id}/name")]
    public async Task<ActionResult<SnCloudFile>> UpdateFileName(string id, [FromBody] string name)
    {
        return await UpdateFileProperty(id, file => file.Name = name);
    }

    public class MarkFileRequest
    {
        public List<Shared.Models.ContentSensitiveMark>? SensitiveMarks { get; set; }
    }

    [Authorize]
    [HttpPut("{id}/marks")]
    public async Task<ActionResult<SnCloudFile>> MarkFile(string id, [FromBody] MarkFileRequest request)
    {
        return await UpdateFileProperty(id, file => file.SensitiveMarks = request.SensitiveMarks);
    }

    [Authorize]
    [HttpPut("{id}/meta")]
    public async Task<ActionResult<SnCloudFile>> UpdateFileMeta(string id, [FromBody] Dictionary<string, object?> meta)
    {
        return await UpdateFileProperty(id, file => file.UserMeta = meta);
    }

    private async Task<ActionResult<SnCloudFile>> UpdateFileProperty(string fileId, Action<SnCloudFile> updateAction)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == fileId && f.AccountId == accountId);
        if (file is null) return NotFound();

        updateAction(file);
        await db.SaveChangesAsync();
        await fs._PurgeCacheAsync(file.Id);

        return file;
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<List<SnCloudFile>>> GetMyFiles(
        [FromQuery] Guid? pool,
        [FromQuery] bool recycled = false,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20,
        [FromQuery] string? query = null,
        [FromQuery] string order = "date",
        [FromQuery] bool orderDesc = true
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var filesQuery = db.Files
            .Where(e => e.IsMarkedRecycle == recycled)
            .Where(e => e.AccountId == accountId)
            .Include(e => e.Pool)
            .AsQueryable();

        if (pool.HasValue) filesQuery = filesQuery.Where(e => e.PoolId == pool);

        if (!string.IsNullOrWhiteSpace(query))
        {
            filesQuery = filesQuery.Where(e => e.Name.Contains(query));
        }

        filesQuery = order.ToLower() switch
        {
            "date" => orderDesc ? filesQuery.OrderByDescending(e => e.CreatedAt) : filesQuery.OrderBy(e => e.CreatedAt),
            "size" => orderDesc ? filesQuery.OrderByDescending(e => e.Size) : filesQuery.OrderBy(e => e.Size),
            "name" => orderDesc ? filesQuery.OrderByDescending(e => e.Name) : filesQuery.OrderBy(e => e.Name),
            _ => filesQuery.OrderByDescending(e => e.CreatedAt)
        };

        var total = await filesQuery.CountAsync();
        Response.Headers.Append("X-Total", total.ToString());

        var files = await filesQuery
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        return Ok(files);
    }

    [Authorize]
    [HttpDelete("{id}")]
    public async Task<ActionResult<SnCloudFile>> DeleteFile(string id)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var userId = Guid.Parse(currentUser.Id);

        var file = await db.Files
            .Where(e => e.Id == id)
            .Where(e => e.AccountId == userId)
            .FirstOrDefaultAsync();
        if (file is null) return NotFound();

        await fs.DeleteFileDataAsync(file, force: true);
        await fs.DeleteFileAsync(file, skipData: true);

        return Ok(file);
    }

    [Authorize]
    [HttpDelete("me/recycle")]
    public async Task<ActionResult> DeleteMyRecycledFiles()
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var count = await fs.DeleteAccountRecycledFilesAsync(accountId);
        return Ok(new { Count = count });
    }

    [Authorize]
    [HttpDelete("recycle")]
    [RequiredPermission("maintenance", "files.delete.recycle")]
    public async Task<ActionResult> DeleteAllRecycledFiles()
    {
        var count = await fs.DeleteAllRecycledFilesAsync();
        return Ok(new { Count = count });
    }
}
