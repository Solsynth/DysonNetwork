using System.Security.Cryptography;
using System.Text;
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
    IWebHostEnvironment env
) : ControllerBase
{
    private string AccessTokenSecret => configuration["AccessToken:Secret"]
                                        ?? "dyson-network-default-access-token-secret-change-in-production";

    private static readonly TimeSpan LocalSignedUrlExpiry = TimeSpan.FromMinutes(10);

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

        var currentUser = HttpContext.Items["CurrentUser"] as Account;
        var accessResult = await ValidateFileAccess(file, passcode, currentUser);
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

    private static (string fileId, string? extension) ParseFileId(string id)
    {
        if (!id.Contains('.')) return (id, null);

        var parts = id.Split('.');
        return (parts.First(), parts.Last());
    }

    private async Task<ActionResult?> ValidateFileAccess(SnCloudFile file, string? passcode,
        Account? currentUser = null)
    {
        if (file.Bundle is not null && !file.Bundle.VerifyPasscode(passcode))
            return StatusCode(StatusCodes.Status403Forbidden, "The passcode is incorrect.");

        var hasAccess = await CheckFilePermissionAsync(file, currentUser, SnFilePermissionLevel.Read);
        return !hasAccess
            ? StatusCode(StatusCodes.Status403Forbidden, "You don't have permission to access this file.")
            : null;
    }

    private async Task<bool> CheckFilePermissionAsync(SnCloudFile file, Account? currentUser,
        SnFilePermissionLevel requiredLevel)
    {
        if (currentUser?.IsSuperuser == true)
            return true;

        var permission = await db.FilePermissions
            .FirstOrDefaultAsync(p => p.FileId == file.Id);

        if (permission is null)
        {
            var isOwner = currentUser is not null && file.AccountId == Guid.Parse(currentUser.Id);
            if (isOwner)
                return true;
            return false;
        }

        if (permission.SubjectType == SnFilePermissionType.Anyone)
        {
            var hasAccess = requiredLevel == SnFilePermissionLevel.Read
                            || (requiredLevel == SnFilePermissionLevel.Write &&
                                permission.Permission == SnFilePermissionLevel.Write);
            return hasAccess;
        }

        if (permission.SubjectType == SnFilePermissionType.Someone && currentUser is not null)
        {
            if (permission.SubjectId == currentUser.Id)
            {
                var hasAccess = requiredLevel == SnFilePermissionLevel.Read
                                || (requiredLevel == SnFilePermissionLevel.Write &&
                                    permission.Permission == SnFilePermissionLevel.Write);
                return hasAccess;
            }
        }

        return false;
    }

    private async Task<bool> HasWritePermissionAsync(SnCloudFile file, Account? currentUser)
    {
        if (currentUser?.IsSuperuser == true)
            return true;

        if (currentUser is not null && file.AccountId == Guid.Parse(currentUser.Id))
            return true;

        var permission = await db.FilePermissions
            .FirstOrDefaultAsync(p => p.FileId == file.Id);

        if (permission is null)
            return false;

        return permission.Permission == SnFilePermissionLevel.Write;
    }

    private Task<ActionResult> ServeLocalFile(SnCloudFile file)
    {
        var currentUser = HttpContext.Items["CurrentUser"] as Account;
        var hasWritePermission = Task.Run(() => HasWritePermissionAsync(file, currentUser)).GetAwaiter().GetResult();
        var accessToken = GenerateLocalSignedToken(file.Id, currentUser?.Id, hasWritePermission);

        var accessUrl = $"/api/files/{file.Id}/access?token={accessToken}";
        return Task.FromResult<ActionResult>(Redirect(accessUrl));
    }

    [HttpGet("{id}/access")]
    public async Task<ActionResult> AccessFile(string id, [FromQuery] string token)
    {
        var validation = ValidateLocalSignedToken(token);
        if (!validation.IsValid)
            return StatusCode(StatusCodes.Status403Forbidden, "Invalid or expired access token.");

        if (validation.FileId != id)
            return StatusCode(StatusCodes.Status400BadRequest, "Token mismatch.");

        var file = await fs.GetFileAsync(id);
        if (file is null) return NotFound("File not found.");

        var tempFilePath = Path.Combine(Path.GetTempPath(), file.Id);
        if (System.IO.File.Exists(tempFilePath))
        {
            return PhysicalFile(tempFilePath, file.MimeType ?? "application/octet-stream",
                file.Name, enableRangeProcessing: true);
        }

        var tusStorePath = configuration.GetValue<string>("Storage:Uploads");
        if (string.IsNullOrEmpty(tusStorePath))
            return StatusCode(StatusCodes.Status400BadRequest,
                "File is being processed. Please try again later.");
        var tusFilePath = Path.Combine(env.ContentRootPath, tusStorePath, file.Id);
        if (System.IO.File.Exists(tusFilePath))
        {
            return PhysicalFile(tusFilePath, file.MimeType ?? "application/octet-stream",
                file.Name, enableRangeProcessing: true);
        }

        return StatusCode(StatusCodes.Status400BadRequest,
            "File is being processed. Please try again later.");
    }

    private string GenerateLocalSignedToken(string fileId, string? userId, bool hasWritePermission)
    {
        var expiry = DateTimeOffset.UtcNow.Add(LocalSignedUrlExpiry).ToUnixTimeSeconds();
        var payload = $"{fileId}|{userId ?? ""}|{expiry}|{hasWritePermission}";

        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var payloadBase64 = Convert.ToBase64String(payloadBytes);

        var signature = ComputeHmacSignature(payloadBase64);
        var token = $"{payloadBase64}.{signature}";

        return Uri.EscapeDataString(token);
    }

    private (bool IsValid, string FileId, string? UserId, bool HasWritePermission) ValidateLocalSignedToken(
        string token)
    {
        try
        {
            var tokenDecoded = Uri.UnescapeDataString(token);
            var parts = tokenDecoded.Split('.');
            if (parts.Length != 2)
                return (false, string.Empty, null, false);

            var payloadBase64 = parts[0];
            var providedSignature = parts[1];

            var expectedSignature = ComputeHmacSignature(payloadBase64);
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(expectedSignature),
                    Encoding.UTF8.GetBytes(providedSignature)))
                return (false, string.Empty, null, false);

            var payloadBytes = Convert.FromBase64String(payloadBase64);
            var payload = Encoding.UTF8.GetString(payloadBytes);
            var payloadParts = payload.Split('|');

            if (payloadParts.Length < 4)
                return (false, string.Empty, null, false);

            var fileId = payloadParts[0];
            var userId = string.IsNullOrEmpty(payloadParts[1]) ? null : payloadParts[1];
            var expiry = long.Parse(payloadParts[2]);
            var hasWritePermission = bool.Parse(payloadParts[3]);

            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiry)
                return (false, string.Empty, null, false);

            return (true, fileId, userId, hasWritePermission);
        }
        catch
        {
            return (false, string.Empty, null, false);
        }
    }

    private string ComputeHmacSignature(string data)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(AccessTokenSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
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
            return Redirect(BuildProxyUrl(dest.ImageProxy, fileName));

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

        if (dest.AccessEndpoint is not null)
            openUrl = openUrl.Replace($"{dest.Endpoint}/{dest.Bucket}", dest.AccessEndpoint);

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
    public async Task<ActionResult<List<SnCloudFile>>> GetFileReferences(string id)
    {
        var file = await fs.GetFileAsync(id);
        if (file is null) return NotFound("File not found.");

        var currentUser = HttpContext.Items["CurrentUser"] as Account;
        var accessResult = await ValidateFileAccess(file, null, currentUser);
        if (accessResult is not null) return accessResult;

        var references = await db.Files
            .Where(f => f.ObjectId == file.ObjectId && f.Id != file.Id)
            .ToListAsync();
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
            .Include(e => e.Object)
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

    public class FileBatchDeletionRequest
    {
        public List<string> FileIds { get; set; } = [];
    }

    [Authorize]
    [HttpPost("batches/delete")]
    public async Task<ActionResult> DeleteFileBatch([FromBody] FileBatchDeletionRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var userId = Guid.Parse(currentUser.Id);

        var count = await fs.DeleteAccountFileBatchAsync(userId, request.FileIds);
        return Ok(new { Count = count });
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
    [AskPermission("files.delete.recycle")]
    public async Task<ActionResult> DeleteAllRecycledFiles()
    {
        var count = await fs.DeleteAllRecycledFilesAsync();
        return Ok(new { Count = count });
    }
}