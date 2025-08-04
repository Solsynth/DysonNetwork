using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Data;
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
        // Support the file extension for client side data recognize
        string? fileExtension = null;
        if (id.Contains('.'))
        {
            var splitId = id.Split('.');
            id = splitId.First();
            fileExtension = splitId.Last();
        }

        var file = await fs.GetFileAsync(id);
        if (file is null) return NotFound();
        if (file.IsMarkedRecycle) return StatusCode(StatusCodes.Status410Gone, "The file has been recycled.");

        if (file.Bundle is not null && !file.Bundle.VerifyPasscode(passcode))
            return StatusCode(StatusCodes.Status403Forbidden, "The passcode is incorrect.");

        if (!string.IsNullOrWhiteSpace(file.StorageUrl)) return Redirect(file.StorageUrl);

        if (!file.PoolId.HasValue)
        {
            var tusStorePath = configuration.GetValue<string>("Tus:StorePath")!;
            var filePath = Path.Combine(env.ContentRootPath, tusStorePath, file.Id);
            if (!System.IO.File.Exists(filePath)) return new NotFoundResult();
            return PhysicalFile(filePath, file.MimeType ?? "application/octet-stream", file.Name);
        }

        var pool = await fs.GetPoolAsync(file.PoolId.Value);
        if (pool is null)
            return StatusCode(StatusCodes.Status410Gone, "The pool of the file no longer exists or not accessible.");
        var dest = pool.StorageConfig;

        if (!pool.PolicyConfig.AllowAnonymous)
            if (HttpContext.Items["CurrentUser"] is not Account currentUser)
                return Unauthorized();
        // TODO: Provide ability to add access log

        var fileName = string.IsNullOrWhiteSpace(file.StorageId) ? file.Id : file.StorageId;

        if (thumbnail && file.HasThumbnail)
            fileName += ".thumbnail";
        else if (!original && file.HasCompression)
            fileName += ".compressed";

        if (dest.ImageProxy is not null && (file.MimeType?.StartsWith("image/") ?? false))
        {
            var proxyUrl = dest.ImageProxy;
            var baseUri = new Uri(proxyUrl.EndsWith('/') ? proxyUrl : $"{proxyUrl}/");
            var fullUri = new Uri(baseUri, fileName);
            return Redirect(fullUri.ToString());
        }

        if (dest.AccessProxy is not null)
        {
            var proxyUrl = dest.AccessProxy;
            var baseUri = new Uri(proxyUrl.EndsWith('/') ? proxyUrl : $"{proxyUrl}/");
            var fullUri = new Uri(baseUri, fileName);
            return Redirect(fullUri.ToString());
        }

        if (dest.EnableSigned)
        {
            var client = fs.CreateMinioClient(dest);
            if (client is null)
                return BadRequest(
                    "Failed to configure client for remote destination, file got an invalid storage remote."
                );

            var headers = new Dictionary<string, string>();
            if (fileExtension is not null)
            {
                if (MimeTypes.TryGetMimeType(fileExtension, out var mimeType))
                    headers.Add("Response-Content-Type", mimeType);
            }
            else if (overrideMimeType is not null)
            {
                headers.Add("Response-Content-Type", overrideMimeType);
            }
            else if (file.MimeType is not null && !file.MimeType!.EndsWith("unknown"))
            {
                headers.Add("Response-Content-Type", file.MimeType);
            }

            if (download)
            {
                headers.Add("Response-Content-Disposition", $"attachment; filename=\"{file.Name}\"");
            }

            var bucket = dest.Bucket;
            var openUrl = await client.PresignedGetObjectAsync(
                new PresignedGetObjectArgs()
                    .WithBucket(bucket)
                    .WithObject(fileName)
                    .WithExpiry(3600)
                    .WithHeaders(headers)
            );

            return Redirect(openUrl);
        }

        // Fallback redirect to the S3 endpoint (public read)
        var protocol = dest.EnableSsl ? "https" : "http";
        // Use the path bucket lookup mode
        return Redirect($"{protocol}://{dest.Endpoint}/{dest.Bucket}/{fileName}");
    }

    [HttpGet("{id}/info")]
    public async Task<ActionResult<CloudFile>> GetFileInfo(string id)
    {
        var file = await fs.GetFileAsync(id);
        if (file is null) return NotFound();

        return file;
    }

    [Authorize]
    [HttpPatch("{id}/name")]
    public async Task<ActionResult<CloudFile>> UpdateFileName(string id, [FromBody] string name)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == id && f.AccountId == accountId);
        if (file is null) return NotFound();
        file.Name = name;
        await db.SaveChangesAsync();
        await fs._PurgeCacheAsync(file.Id);
        return file;
    }

    public class MarkFileRequest
    {
        public List<ContentSensitiveMark>? SensitiveMarks { get; set; }
    }

    [Authorize]
    [HttpPut("{id}/marks")]
    public async Task<ActionResult<CloudFile>> MarkFile(string id, [FromBody] MarkFileRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == id && f.AccountId == accountId);
        if (file is null) return NotFound();
        file.SensitiveMarks = request.SensitiveMarks;
        await db.SaveChangesAsync();
        await fs._PurgeCacheAsync(file.Id);
        return file;
    }

    [Authorize]
    [HttpPut("{id}/meta")]
    public async Task<ActionResult<CloudFile>> UpdateFileMeta(string id, [FromBody] Dictionary<string, object?> meta)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == id && f.AccountId == accountId);
        if (file is null) return NotFound();
        file.UserMeta = meta;
        await db.SaveChangesAsync();
        await fs._PurgeCacheAsync(file.Id);
        return file;
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<List<CloudFile>>> GetMyFiles(
        [FromQuery] Guid? pool,
        [FromQuery] bool recycled = false,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var query = db.Files
            .Where(e => e.IsMarkedRecycle == recycled)
            .Where(e => e.AccountId == accountId)
            .Include(e => e.Pool)
            .OrderByDescending(e => e.CreatedAt)
            .AsQueryable();

        if (pool.HasValue) query = query.Where(e => e.PoolId == pool);

        var total = await query.CountAsync();
        Response.Headers.Append("X-Total", total.ToString());

        var files = await query
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        return Ok(files);
    }

    [Authorize]
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteFile(string id)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var userId = Guid.Parse(currentUser.Id);

        var file = await db.Files
            .Where(e => e.Id == id)
            .Where(e => e.AccountId == userId)
            .FirstOrDefaultAsync();
        if (file is null) return NotFound();

        await fs.DeleteFileDataAsync(file, force: true);
        await fs.DeleteFileAsync(file);

        return NoContent();
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