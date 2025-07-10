using DysonNetwork.Sphere.Permission;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Minio.DataModel.Args;

namespace DysonNetwork.Sphere.Storage;

[ApiController]
[Route("/api/files")]
public class FileController(
    AppDatabase db,
    FileService fs,
    IConfiguration configuration,
    IWebHostEnvironment env,
    FileReferenceMigrationService rms
) : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<ActionResult> OpenFile(
        string id,
        [FromQuery] bool download = false,
        [FromQuery] bool original = false,
        [FromQuery] string? overrideMimeType = null
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

        if (!string.IsNullOrWhiteSpace(file.StorageUrl)) return Redirect(file.StorageUrl);

        if (file.UploadedTo is null)
        {
            var tusStorePath = configuration.GetValue<string>("Tus:StorePath")!;
            var filePath = Path.Combine(env.ContentRootPath, tusStorePath, file.Id);
            if (!System.IO.File.Exists(filePath)) return new NotFoundResult();
            return PhysicalFile(filePath, file.MimeType ?? "application/octet-stream", file.Name);
        }

        var dest = fs.GetRemoteStorageConfig(file.UploadedTo);
        var fileName = string.IsNullOrWhiteSpace(file.StorageId) ? file.Id : file.StorageId;

        if (!original && file.HasCompression)
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
                    "Failed to configure client for remote destination, file got an invalid storage remote.");

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
        var file = await db.Files.FindAsync(id);
        if (file is null) return NotFound();

        return file;
    }

    [Authorize]
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteFile(string id)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var file = await db.Files
            .Where(e => e.Id == id)
            .Where(e => e.Account.Id == userId)
            .FirstOrDefaultAsync();
        if (file is null) return NotFound();

        await fs.DeleteFileAsync(file);

        db.Files.Remove(file);
        await db.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("/maintenance/migrateReferences")]
    [Authorize]
    [RequiredPermission("maintenance", "files.references")]
    public async Task<ActionResult> MigrateFileReferences()
    {
        await rms.ScanAndMigrateReferences();
        return Ok();
    }
}