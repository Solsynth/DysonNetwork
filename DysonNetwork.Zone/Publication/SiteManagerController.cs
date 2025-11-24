using System.IO.Compression;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublisherMemberRole = DysonNetwork.Shared.Models.PublisherMemberRole;

namespace DysonNetwork.Zone.Publication;

[ApiController]
[Route("api/sites/{siteId:guid}/files")]
public class SiteManagerController(
    PublicationSiteService publicationSiteService,
    PublicationSiteManager fileManager,
    RemotePublisherService remotePublisherService
) : ControllerBase
{
    private async Task<ActionResult?> CheckAccess(Guid siteId)
    {
        var site = await publicationSiteService.GetSiteById(siteId);
        if (site is not { Mode: PublicationSiteMode.SelfManaged })
            return NotFound("Site not found or not self-managed");

        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var isMember =
            await remotePublisherService.IsMemberWithRole(site.PublisherId, accountId, PublisherMemberRole.Editor);
        return !isMember ? Forbid() : null;
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<FileEntry>>> ListFiles(Guid siteId, [FromQuery] string path = "")
    {
        var check = await CheckAccess(siteId);
        if (check != null) return check;

        try
        {
            var entries = await fileManager.ListFiles(siteId, path);
            return Ok(entries);
        }
        catch (DirectoryNotFoundException)
        {
            return NotFound("Directory not found");
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("upload")]
    [Authorize]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadFile(Guid siteId, [FromForm] string filePath, IFormFile? file)
    {
        var check = await CheckAccess(siteId);
        if (check != null) return check;

        if (file == null || file.Length == 0)
            return BadRequest("No file provided");

        const long maxTotalSize = 26214400; // 25MB

        var currentTotal = await fileManager.GetTotalSiteSize(siteId);
        if (currentTotal + file.Length > maxTotalSize)
            return BadRequest("Site total size would exceed 25MB limit");

        try
        {
            await fileManager.UploadFile(siteId, filePath, file);
            return Ok();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("deploy")]
    [Authorize]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Deploy(
        Guid siteId,
        [FromForm(Name = "file")] IFormFile? zipFile,
        [FromQuery] bool smart = true
    )
    {
        var check = await CheckAccess(siteId);
        if (check != null) return check;

        if (zipFile == null || zipFile.Length == 0)
            return BadRequest("No file provided.");

        if (Path.GetExtension(zipFile.FileName).ToLowerInvariant() != ".zip")
            return BadRequest("Only .zip files are allowed for deployment.");

        // Define size limits
        const long maxZipFileSize = 52428800; // 50MB for the zip file itself
        const long maxTotalSiteSizeAfterExtract = 104857600; // 100MB total size after extraction

        if (zipFile.Length > maxZipFileSize)
            return BadRequest($"Zip file size exceeds {maxZipFileSize / (1024 * 1024)}MB limit.");

        try
        {
            // For now, we'll only check the zip file size.
            // A more robust solution might involve extracting to a temp location
            // and checking the uncompressed size before moving, but that's more complex.

            // Get current site size before deployment
            long currentTotal = await fileManager.GetTotalSiteSize(siteId);

            // This is a rough check. The actual uncompressed size might be much larger.
            // Consider adding a more sophisticated check if this is a concern.
            if (currentTotal + zipFile.Length * 3 > maxTotalSiteSizeAfterExtract) // Heuristic: assume 3x expansion
                return BadRequest(
                    $"Deployment would exceed total site size limit of {maxTotalSiteSizeAfterExtract / (1024 * 1024)}MB.");

            var siteDir = fileManager.GetSiteDirectory(siteId);
            Directory.CreateDirectory(siteDir); // Ensure site directory exists

            if (smart)
            {
                // Smart mode: Extract to temp directory and flatten if single folder wrapper
                var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempDir);
                try
                {
                    await using (var archive = new ZipArchive(zipFile.OpenReadStream(), ZipArchiveMode.Read))
                    {
                        await archive.ExtractToDirectoryAsync(tempDir, true);
                    }

                    // Check if temp directory has exactly one subdirectory and no files at root
                    var rootEntries = Directory.GetFileSystemEntries(tempDir);
                    if (rootEntries.Length == 1 && Directory.Exists(rootEntries[0]))
                    {
                        var innerDir = rootEntries[0];
                        // Flatten: move contents of innerDir to siteDir
                        foreach (var file in Directory.GetFiles(innerDir, "*", SearchOption.AllDirectories))
                        {
                            string relPath = Path.GetRelativePath(innerDir, file);
                            string destFile = Path.Combine(siteDir, relPath);
                            string? destDir = Path.GetDirectoryName(destFile);
                            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                                Directory.CreateDirectory(destDir);
                            System.IO.File.Move(file, destFile, true);
                        }

                        // Also create empty directories
                        foreach (var dir in Directory.GetDirectories(innerDir, "*", SearchOption.AllDirectories))
                        {
                            string relPath = Path.GetRelativePath(innerDir, dir);
                            string destDirPath = Path.Combine(siteDir, relPath);
                            Directory.CreateDirectory(destDirPath);
                        }
                    }
                    else
                    {
                        // No smart flattening needed, extract directly to siteDir
                        using (var archive = new ZipArchive(zipFile.OpenReadStream(), ZipArchiveMode.Read))
                        {
                            archive.ExtractToDirectory(siteDir, true);
                        }
                    }
                }
                finally
                {
                    Directory.Delete(tempDir, true);
                }
            }
            else
            {
                await fileManager.DeployZip(siteId, zipFile);
            }

            return Ok("Deployment successful.");
        }
        catch (Exception ex)
        {
            return BadRequest($"Deployment failed: {ex.Message}");
        }
    }

    [HttpDelete("purge")]
    [Authorize]
    public async Task<IActionResult> Purge(Guid siteId)
    {
        var check = await CheckAccess(siteId);
        if (check != null) return check;

        try
        {
            await fileManager.PurgeSite(siteId);
            return Ok("Site content purged successfully.");
        }
        catch (Exception ex)
        {
            return BadRequest($"Purge failed: {ex.Message}");
        }
    }

    [HttpGet("content/{**relativePath}")]
    [Authorize]
    public async Task<ActionResult<string>> GetFileContent(Guid siteId, string relativePath)
    {
        var check = await CheckAccess(siteId);
        if (check != null) return check;

        try
        {
            var content = await fileManager.ReadFileContent(siteId, relativePath);
            return Ok(content);
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("download/{**relativePath}")]
    [Authorize]
    public async Task<IActionResult> DownloadFile(Guid siteId, string relativePath)
    {
        var check = await CheckAccess(siteId);
        if (check != null) return check;

        string fullPath;
        try
        {
            fullPath = fileManager.GetValidatedFullPath(siteId, relativePath);
        }
        catch (ArgumentException)
        {
            return BadRequest("Invalid path");
        }

        if (!System.IO.File.Exists(fullPath))
            return NotFound();

        // Determine MIME type
        var mimeType = "application/octet-stream"; // default
        var ext = Path.GetExtension(relativePath).ToLowerInvariant();
        if (ext == ".txt") mimeType = "text/plain";
        else if (ext == ".html" || ext == ".htm") mimeType = "text/html";
        else if (ext == ".css") mimeType = "text/css";
        else if (ext == ".js") mimeType = "application/javascript";
        else if (ext == ".json") mimeType = "application/json";

        return PhysicalFile(fullPath, mimeType, Path.GetFileName(relativePath));
    }

    [HttpPut("edit/{**relativePath}")]
    [Authorize]
    public async Task<IActionResult> UpdateFile(Guid siteId, string relativePath, [FromBody] UpdateFileRequest request)
    {
        var check = await CheckAccess(siteId);
        if (check != null) return check;

        const long maxFileSize = 1048576; // 1MB
        const long maxTotalSize = 26214400; // 25MB

        if (request.NewContent.Length > maxFileSize)
            return BadRequest("New content exceeds 1MB limit");

        long oldSize = 0;
        try
        {
            var fullPath = fileManager.GetValidatedFullPath(siteId, relativePath);
            if (System.IO.File.Exists(fullPath))
                oldSize = new FileInfo(fullPath).Length;

            var currentTotal = await fileManager.GetTotalSiteSize(siteId);
            if (currentTotal - oldSize + request.NewContent.Length > maxTotalSize)
                return BadRequest("Site total size would exceed 25MB limit");

            await fileManager.UpdateFile(siteId, relativePath, request.NewContent);
            return Ok();
        }
        catch (ArgumentException)
        {
            return BadRequest("Invalid path");
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("delete/{**relativePath}")]
    [Authorize]
    public async Task<IActionResult> DeleteFile(Guid siteId, string relativePath)
    {
        var check = await CheckAccess(siteId);
        if (check != null) return check;

        try
        {
            await fileManager.DeleteFile(siteId, relativePath);
            return Ok();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }
}

public class UpdateFileRequest
{
    public string NewContent { get; set; } = null!;
}