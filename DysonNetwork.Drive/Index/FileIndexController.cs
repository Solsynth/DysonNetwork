using System.ComponentModel.DataAnnotations;
using DysonNetwork.Drive.Storage;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Http;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Drive.Index;

[ApiController]
[Route("/api/index")]
[Authorize]
public class FileIndexController(
    FileIndexService fileIndexService,
    AppDatabase db,
    ILogger<FileIndexController> logger
) : ControllerBase
{
    /// <summary>
    /// Gets files in a specific path for the current user
    /// </summary>
    /// <param name="path">The path to browse (defaults to root "/")</param>
    /// <param name="query">Optional query to filter files by name</param>
    /// <param name="order">The field to order by (date, size, name - defaults to date)</param>
    /// <param name="orderDesc">Whether to order in descending order (defaults to true)</param>
    /// <returns>List of files in the specified path</returns>
    [HttpGet("browse")]
    public async Task<IActionResult> BrowseFiles(
        [FromQuery] string path = "/",
        [FromQuery] string? query = null,
        [FromQuery] string order = "date",
        [FromQuery] bool orderDesc = true
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return new ObjectResult(ApiError.Unauthorized()) { StatusCode = 401 };

        var accountId = Guid.Parse(currentUser.Id);

        try
        {
            var fileIndexes = await fileIndexService.GetByPathAsync(accountId, path);

            if (!string.IsNullOrWhiteSpace(query))
            {
                fileIndexes = fileIndexes
                    .Where(fi => fi.File.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            // Apply sorting
            fileIndexes = order.ToLower() switch
            {
                "name" => orderDesc ? fileIndexes.OrderByDescending(fi => fi.File.Name).ToList()
                                   : fileIndexes.OrderBy(fi => fi.File.Name).ToList(),
                "size" => orderDesc ? fileIndexes.OrderByDescending(fi => fi.File.Size).ToList()
                                   : fileIndexes.OrderBy(fi => fi.File.Size).ToList(),
                _ => orderDesc ? fileIndexes.OrderByDescending(fi => fi.File.CreatedAt).ToList()
                              : fileIndexes.OrderBy(fi => fi.File.CreatedAt).ToList()
            };

            // Get all file indexes for this account to extract child folders
            var allFileIndexes = await fileIndexService.GetByAccountIdAsync(accountId);

            // Extract unique child folder paths
            var childFolders = ExtractChildFolders(allFileIndexes, path);

            return Ok(new
            {
                Path = path,
                Files = fileIndexes,
                Folders = childFolders,
                TotalCount = fileIndexes.Count
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to browse files for account {AccountId} at path {Path}", accountId, path);
            return new ObjectResult(new ApiError
            {
                Code = "BROWSE_FAILED",
                Message = "Failed to browse files",
                Status = 500
            }) { StatusCode = 500 };
        }
    }

    /// <summary>
    /// Extracts unique child folder paths from all file indexes for a given parent path
    /// </summary>
    /// <param name="allFileIndexes">All file indexes for the account</param>
    /// <param name="parentPath">The parent path to find children for</param>
    /// <returns>List of unique child folder names</returns>
    private List<string> ExtractChildFolders(List<SnCloudFileIndex> allFileIndexes, string parentPath)
    {
        var normalizedParentPath = FileIndexService.NormalizePath(parentPath);
        var childFolders = new HashSet<string>();

        foreach (var index in allFileIndexes)
        {
            var normalizedIndexPath = FileIndexService.NormalizePath(index.Path);

            // Check if this path is a direct child of the parent path
            if (normalizedIndexPath.StartsWith(normalizedParentPath) &&
                normalizedIndexPath != normalizedParentPath)
            {
                // Remove the parent path prefix to get the relative path
                var relativePath = normalizedIndexPath.Substring(normalizedParentPath.Length);

                // Extract the first folder name (direct child)
                var firstSlashIndex = relativePath.IndexOf('/');
                if (firstSlashIndex > 0)
                {
                    var folderName = relativePath.Substring(0, firstSlashIndex);
                    childFolders.Add(folderName);
                }
            }
        }

        return childFolders.OrderBy(f => f).ToList();
    }

    /// <summary>
    /// Gets all files for the current user (across all paths)
    /// </summary>
    /// <param name="query">Optional query to filter files by name</param>
    /// <param name="order">The field to order by (date, size, name - defaults to date)</param>
    /// <param name="orderDesc">Whether to order in descending order (defaults to true)</param>
    /// <returns>List of all files for the user</returns>
    [HttpGet("all")]
    public async Task<IActionResult> GetAllFiles(
        [FromQuery] string? query = null,
        [FromQuery] string order = "date",
        [FromQuery] bool orderDesc = true
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return new ObjectResult(ApiError.Unauthorized()) { StatusCode = 401 };

        var accountId = Guid.Parse(currentUser.Id);

        try
        {
            var fileIndexes = await fileIndexService.GetByAccountIdAsync(accountId);

            if (!string.IsNullOrWhiteSpace(query))
            {
                fileIndexes = fileIndexes
                    .Where(fi => fi.File.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            // Apply sorting
            fileIndexes = order.ToLower() switch
            {
                "name" => orderDesc ? fileIndexes.OrderByDescending(fi => fi.File.Name).ToList()
                                   : fileIndexes.OrderBy(fi => fi.File.Name).ToList(),
                "size" => orderDesc ? fileIndexes.OrderByDescending(fi => fi.File.Size).ToList()
                                   : fileIndexes.OrderBy(fi => fi.File.Size).ToList(),
                _ => orderDesc ? fileIndexes.OrderByDescending(fi => fi.File.CreatedAt).ToList()
                              : fileIndexes.OrderBy(fi => fi.File.CreatedAt).ToList()
            };

            return Ok(new
            {
                Files = fileIndexes,
                TotalCount = fileIndexes.Count()
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get all files for account {AccountId}", accountId);
            return new ObjectResult(new ApiError
            {
                Code = "GET_ALL_FAILED",
                Message = "Failed to get files",
                Status = 500
            }) { StatusCode = 500 };
        }
    }

    /// <summary>
    /// Gets files that have not been indexed for the current user.
    /// </summary>
    /// <param name="recycled">Shows recycled files or not</param>
    /// <param name="offset">The number of files to skip</param>
    /// <param name="take">The number of files to return</param>
    /// <param name="pool">The pool ID of those files</param>
    /// <param name="query">Optional query to filter files by name</param>
    /// <param name="order">The field to order by (date, size, name - defaults to date)</param>
    /// <param name="orderDesc">Whether to order in descending order (defaults to true)</param>
    /// <returns>List of unindexed files</returns>
    [HttpGet("unindexed")]
    public async Task<IActionResult> GetUnindexedFiles(
        [FromQuery] Guid? pool,
        [FromQuery] bool recycled = false,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20,
        [FromQuery] string? query = null,
        [FromQuery] string order = "date",
        [FromQuery] bool orderDesc = true
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return new ObjectResult(ApiError.Unauthorized()) { StatusCode = 401 };

        var accountId = Guid.Parse(currentUser.Id);

        try
        {
            var filesQuery = db.Files
                .Where(f => f.AccountId == accountId
                            && f.IsMarkedRecycle == recycled
                            && !db.FileIndexes.Any(fi => fi.FileId == f.Id && fi.AccountId == accountId)
                )
                .Include(f => f.Object)
                .AsQueryable();

            // Apply sorting
            filesQuery = order.ToLower() switch
            {
                "name" => orderDesc ? filesQuery.OrderByDescending(f => f.Name)
                                   : filesQuery.OrderBy(f => f.Name),
                "size" => orderDesc ? filesQuery.OrderByDescending(f => f.Size)
                                   : filesQuery.OrderBy(f => f.Size),
                _ => orderDesc ? filesQuery.OrderByDescending(f => f.CreatedAt)
                              : filesQuery.OrderBy(f => f.CreatedAt)
            };

            if (pool.HasValue) filesQuery = filesQuery.Where(f => f.PoolId == pool);

            if (!string.IsNullOrWhiteSpace(query))
            {
                filesQuery = filesQuery.Where(f => f.Name.Contains(query));
            }

            var totalCount = await filesQuery.CountAsync();

            Response.Headers.Append("X-Total", totalCount.ToString());

            var unindexedFiles = await filesQuery
                .Skip(offset)
                .Take(take)
                .ToListAsync();

            return Ok(unindexedFiles);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get unindexed files for account {AccountId}", accountId);
            return new ObjectResult(new ApiError
            {
                Code = "GET_UNINDEXED_FAILED",
                Message = "Failed to get unindexed files",
                Status = 500
            }) { StatusCode = 500 };
        }
    }

    /// <summary>
    /// Moves a file to a new path
    /// </summary>
    /// <param name="indexId">The file index ID</param>
    /// <param name="newPath">The new path</param>
    /// <returns>The updated file index</returns>
    [HttpPost("move/{indexId}")]
    public async Task<IActionResult> MoveFile(Guid indexId, [FromBody] MoveFileRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return new ObjectResult(ApiError.Unauthorized()) { StatusCode = 401 };

        var accountId = Guid.Parse(currentUser.Id);

        try
        {
            // Verify ownership
            var existingIndex = await db.FileIndexes
                .Include(fi => fi.File)
                .FirstOrDefaultAsync(fi => fi.Id == indexId && fi.AccountId == accountId);

            if (existingIndex == null)
                return new ObjectResult(ApiError.NotFound("File index")) { StatusCode = 404 };

            var updatedIndex = await fileIndexService.UpdateAsync(indexId, request.NewPath);

            if (updatedIndex == null)
                return new ObjectResult(ApiError.NotFound("File index")) { StatusCode = 404 };

            return Ok(new
            {
                updatedIndex.FileId,
                IndexId = updatedIndex.Id,
                OldPath = existingIndex.Path,
                NewPath = updatedIndex.Path,
                Message = "File moved successfully"
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to move file index {IndexId} for account {AccountId}", indexId, accountId);
            return new ObjectResult(new ApiError
            {
                Code = "MOVE_FAILED",
                Message = "Failed to move file",
                Status = 500
            }) { StatusCode = 500 };
        }
    }

    /// <summary>
    /// Removes a file index (does not delete the actual file by default)
    /// </summary>
    /// <param name="indexId">The file index ID</param>
    /// <param name="deleteFile">Whether to also delete the actual file data</param>
    /// <returns>Success message</returns>
    [HttpDelete("remove/{indexId}")]
    public async Task<IActionResult> RemoveFileIndex(Guid indexId, [FromQuery] bool deleteFile = false)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return new ObjectResult(ApiError.Unauthorized()) { StatusCode = 401 };

        var accountId = Guid.Parse(currentUser.Id);

        try
        {
            // Verify ownership
            var existingIndex = await db.FileIndexes
                .Include(fi => fi.File)
                .FirstOrDefaultAsync(fi => fi.Id == indexId && fi.AccountId == accountId);

            if (existingIndex == null)
                return new ObjectResult(ApiError.NotFound("File index")) { StatusCode = 404 };

            var fileId = existingIndex.FileId;
            var fileName = existingIndex.File.Name;
            var filePath = existingIndex.Path;

            // Remove the index
            var removed = await fileIndexService.RemoveAsync(indexId);

            if (!removed)
                return new ObjectResult(ApiError.NotFound("File index")) { StatusCode = 404 };

            // Optionally delete the actual file
            if (!deleteFile)
                return Ok(new
                {
                    Message = deleteFile
                        ? "File index and file data removed successfully"
                        : "File index removed successfully",
                    FileId = fileId,
                    FileName = fileName,
                    Path = filePath,
                    FileDataDeleted = deleteFile
                });
            try
            {
                // Check if there are any other indexes for this file
                var remainingIndexes = await fileIndexService.GetByFileIdAsync(fileId);
                if (remainingIndexes.Count == 0)
                {
                    // No other indexes exist, safe to delete the file
                    var file = await db.Files.FirstOrDefaultAsync(f => f.Id == fileId.ToString());
                    if (file != null)
                    {
                        db.Files.Remove(file);
                        await db.SaveChangesAsync();
                        logger.LogInformation("Deleted file {FileId} ({FileName}) as requested", fileId, fileName);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete file {FileId} while removing index", fileId);
                // Continue even if file deletion fails
            }

            return Ok(new
            {
                Message = deleteFile
                    ? "File index and file data removed successfully"
                    : "File index removed successfully",
                FileId = fileId,
                FileName = fileName,
                Path = filePath,
                FileDataDeleted = deleteFile
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to remove file index {IndexId} for account {AccountId}", indexId, accountId);
            return new ObjectResult(new ApiError
            {
                Code = "REMOVE_FAILED",
                Message = "Failed to remove file",
                Status = 500
            }) { StatusCode = 500 };
        }
    }

    /// <summary>
    /// Removes all file indexes in a specific path
    /// </summary>
    /// <param name="path">The path to clear</param>
    /// <param name="deleteFiles">Whether to also delete the actual file data</param>
    /// <returns>Success message with count of removed items</returns>
    [HttpDelete("clear-path")]
    public async Task<IActionResult> ClearPath([FromQuery] string path = "/", [FromQuery] bool deleteFiles = false)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return new ObjectResult(ApiError.Unauthorized()) { StatusCode = 401 };

        var accountId = Guid.Parse(currentUser.Id);

        try
        {
            var removedCount = await fileIndexService.RemoveByPathAsync(accountId, path);

            if (!deleteFiles || removedCount <= 0)
                return Ok(new
                {
                    Message = deleteFiles
                        ? $"Cleared {removedCount} file indexes from path and deleted orphaned files"
                        : $"Cleared {removedCount} file indexes from path",
                    Path = path,
                    RemovedCount = removedCount,
                    FilesDeleted = deleteFiles
                });
            // Get the files that were in this path and check if they have other indexes
            var filesInPath = await fileIndexService.GetByPathAsync(accountId, path);
            var fileIdsToCheck = filesInPath.Select(fi => fi.FileId).Distinct().ToList();

            foreach (var fileId in fileIdsToCheck)
            {
                var remainingIndexes = await fileIndexService.GetByFileIdAsync(fileId);
                if (remainingIndexes.Count != 0) continue;
                // No other indexes exist, safe to delete the file
                var file = await db.Files.FirstOrDefaultAsync(f => f.Id == fileId.ToString());
                if (file == null) continue;
                db.Files.Remove(file);
                logger.LogInformation("Deleted orphaned file {FileId} after clearing path {Path}", fileId, path);
            }

            await db.SaveChangesAsync();

            return Ok(new
            {
                Message = deleteFiles
                    ? $"Cleared {removedCount} file indexes from path and deleted orphaned files"
                    : $"Cleared {removedCount} file indexes from path",
                Path = path,
                RemovedCount = removedCount,
                FilesDeleted = deleteFiles
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to clear path {Path} for account {AccountId}", path, accountId);
            return new ObjectResult(new ApiError
            {
                Code = "CLEAR_PATH_FAILED",
                Message = "Failed to clear path",
                Status = 500
            }) { StatusCode = 500 };
        }
    }

    /// <summary>
    /// Creates a new file index (useful for adding existing files to a path)
    /// </summary>
    /// <param name="request">The create index request</param>
    /// <returns>The created file index</returns>
    [HttpPost("create")]
    public async Task<IActionResult> CreateFileIndex([FromBody] CreateFileIndexRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return new ObjectResult(ApiError.Unauthorized()) { StatusCode = 401 };

        var accountId = Guid.Parse(currentUser.Id);

        try
        {
            // Verify the file exists and belongs to the user
            var file = await db.Files.FirstOrDefaultAsync(f => f.Id == request.FileId);
            if (file == null)
                return new ObjectResult(ApiError.NotFound("File")) { StatusCode = 404 };

            if (file.AccountId != accountId)
                return new ObjectResult(ApiError.Unauthorized(forbidden: true)) { StatusCode = 403 };

            // Check if index already exists for this file and path
            var existingIndex = await db.FileIndexes
                .FirstOrDefaultAsync(fi =>
                    fi.FileId == request.FileId && fi.Path == request.Path && fi.AccountId == accountId);

            if (existingIndex != null)
                return new ObjectResult(ApiError.Validation(new Dictionary<string, string[]>
                {
                    { "fileId", ["File index already exists for this path"] }
                })) { StatusCode = 400 };

            var fileIndex = await fileIndexService.CreateAsync(request.Path, request.FileId, accountId);

            return Ok(new
            {
                IndexId = fileIndex.Id,
                fileIndex.FileId,
                fileIndex.Path,
                Message = "File index created successfully"
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create file index for file {FileId} at path {Path} for account {AccountId}",
                request.FileId, request.Path, accountId);
            return new ObjectResult(new ApiError
            {
                Code = "CREATE_INDEX_FAILED",
                Message = "Failed to create file index",
                Status = 500
            }) { StatusCode = 500 };
        }
    }

    /// <summary>
    /// Searches for files by name or metadata
    /// </summary>
    /// <param name="query">The search query</param>
    /// <param name="path">Optional path to limit search to</param>
    /// <returns>Matching files</returns>
    [HttpGet("search")]
    public async Task<IActionResult> SearchFiles([FromQuery] string query, [FromQuery] string? path = null)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return new ObjectResult(ApiError.Unauthorized()) { StatusCode = 401 };

        var accountId = Guid.Parse(currentUser.Id);

        try
        {
            // Build the query with all conditions at once
            var searchTerm = query.ToLower();
            var fileIndexes = await db.FileIndexes
                .Where(fi => fi.AccountId == accountId)
                .Include(fi => fi.File)
                .Where(fi =>
                    (string.IsNullOrEmpty(path) || fi.Path == FileIndexService.NormalizePath(path)) &&
                    (fi.File.Name.ToLower().Contains(searchTerm) ||
                     (fi.File.Description != null && fi.File.Description.ToLower().Contains(searchTerm)) ||
                     (fi.File.MimeType != null && fi.File.MimeType.ToLower().Contains(searchTerm))))
                .ToListAsync();

            return Ok(new
            {
                Query = query,
                Path = path,
                Results = fileIndexes,
                TotalCount = fileIndexes.Count()
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to search files for account {AccountId} with query {Query}", accountId, query);
            return new ObjectResult(new ApiError
            {
                Code = "SEARCH_FAILED",
                Message = "Failed to search files",
                Status = 500
            }) { StatusCode = 500 };
        }
    }
}

public class MoveFileRequest
{
    public string NewPath { get; set; } = null!;
}

public class CreateFileIndexRequest
{
    [MaxLength(32)] public string FileId { get; set; } = null!;
    public string Path { get; set; } = null!;
}
