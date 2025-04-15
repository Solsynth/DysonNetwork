using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Minio.DataModel.Args;

namespace DysonNetwork.Sphere.Storage;

[ApiController]
[Route("/files")]
public class FileController(
    AppDatabase db,
    FileService fs,
    IConfiguration configuration,
    IWebHostEnvironment env
) : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<ActionResult> OpenFile(string id)
    {
        var file = await db.Files.FindAsync(id);
        if (file is null) return NotFound();

        if (file.UploadedTo is null)
        {
            var tusStorePath = configuration.GetValue<string>("Tus:StorePath")!;
            var filePath = Path.Combine(env.ContentRootPath, tusStorePath, file.Id);
            if (!System.IO.File.Exists(filePath)) return new NotFoundResult();
            return PhysicalFile(filePath, file.MimeType ?? "application/octet-stream", file.Name);
        }

        var dest = fs.GetRemoteStorageConfig(file.UploadedTo);

        if (dest.ImageProxy is not null && (file.MimeType?.StartsWith("image/") ?? false))
        {
            var proxyUrl = dest.ImageProxy;
            var baseUri = new Uri(proxyUrl.EndsWith('/') ? proxyUrl : $"{proxyUrl}/");
            var fullUri = new Uri(baseUri, file.Id);
            return Redirect(fullUri.ToString());
        }

        if (dest.AccessProxy is not null)
        {
            var proxyUrl = dest.AccessProxy;
            var baseUri = new Uri(proxyUrl.EndsWith('/') ? proxyUrl : $"{proxyUrl}/");
            var fullUri = new Uri(baseUri, file.Id);
            return Redirect(fullUri.ToString());
        }

        if (dest.EnableSigned)
        {
            var client = fs.CreateMinioClient(dest);
            if (client is null)
                return BadRequest(
                    "Failed to configure client for remote destination, file got an invalid storage remote.");

            var bucket = dest.Bucket;
            var openUrl = await client.PresignedGetObjectAsync(
                new PresignedGetObjectArgs()
                    .WithBucket(bucket)
                    .WithObject(file.Id)
                    .WithExpiry(3600)
            );

            return Redirect(openUrl);
        }

        // Fallback redirect to the S3 endpoint (public read)
        var protocol = dest.EnableSsl ? "https" : "http";
        // Use the path bucket lookup mode
        return Redirect($"{protocol}://{dest.Endpoint}/{dest.Bucket}/{file.Id}");
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
        var userIdClaim = User.FindFirst("user_id")?.Value;
        if (userIdClaim is null) return Unauthorized();
        var userId = long.Parse(userIdClaim);

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
}