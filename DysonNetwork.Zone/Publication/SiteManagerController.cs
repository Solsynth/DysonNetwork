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
        var isMember = await remotePublisherService.IsMemberWithRole(site.PublisherId, accountId, PublisherMemberRole.Editor);
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

        const long maxFileSize = 1048576; // 1MB
        const long maxTotalSize = 26214400; // 25MB

        if (file.Length > maxFileSize)
            return BadRequest("File size exceeds 1MB limit");

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

        var fullPath = fileManager.GetFullPathForDownload(siteId, relativePath);
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

        var fullPath = fileManager.GetFullPathForDownload(siteId, relativePath);
        long oldSize = 0;
        if (System.IO.File.Exists(fullPath))
            oldSize = new FileInfo(fullPath).Length;

        if (request.NewContent.Length > maxFileSize)
            return BadRequest("New content exceeds 1MB limit");

        var currentTotal = await fileManager.GetTotalSiteSize(siteId);
        if (currentTotal - oldSize + request.NewContent.Length > maxTotalSize)
            return BadRequest("Site total size would exceed 25MB limit");

        try
        {
            await fileManager.UpdateFile(siteId, relativePath, request.NewContent);
            return Ok();
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
