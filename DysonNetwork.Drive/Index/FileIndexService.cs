using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Drive.Index;

public class FileIndexService(AppDatabase db)
{
    /// <summary>
    /// Creates a new file index entry
    /// </summary>
    /// <param name="path">The parent folder path with a trailing slash</param>
    /// <param name="fileId">The file ID</param>
    /// <param name="accountId">The account ID</param>
    /// <returns>The created file index</returns>
    public async Task<SnCloudFileIndex> CreateAsync(string path, string fileId, Guid accountId)
    {
        // Ensure a path has a trailing slash and is query-safe
        var normalizedPath = NormalizePath(path);

        // Check if a file with the same name already exists in the same path for this account
        var existingFileIndex = await db.FileIndexes
            .FirstOrDefaultAsync(fi => fi.AccountId == accountId && fi.Path == normalizedPath && fi.FileId == fileId);

        if (existingFileIndex != null)
        {
            throw new InvalidOperationException(
                $"A file with ID '{fileId}' already exists in path '{normalizedPath}' for account '{accountId}'");
        }

        var fileIndex = new SnCloudFileIndex
        {
            Path = normalizedPath,
            FileId = fileId,
            AccountId = accountId
        };

        db.FileIndexes.Add(fileIndex);
        await db.SaveChangesAsync();

        return fileIndex;
    }

    /// <summary>
    /// Updates an existing file index entry by removing the old one and creating a new one
    /// </summary>
    /// <param name="id">The file index ID</param>
    /// <param name="newPath">The new parent folder path with trailing slash</param>
    /// <returns>The updated file index</returns>
    public async Task<SnCloudFileIndex?> UpdateAsync(Guid id, string newPath)
    {
        var fileIndex = await db.FileIndexes.FindAsync(id);
        if (fileIndex == null)
            return null;

        // Since properties are init-only, we need to remove the old index and create a new one
        db.FileIndexes.Remove(fileIndex);

        var newFileIndex = new SnCloudFileIndex
        {
            Path = NormalizePath(newPath),
            FileId = fileIndex.FileId,
            AccountId = fileIndex.AccountId
        };

        db.FileIndexes.Add(newFileIndex);
        await db.SaveChangesAsync();

        return newFileIndex;
    }

    /// <summary>
    /// Removes a file index entry by ID
    /// </summary>
    /// <param name="id">The file index ID</param>
    /// <returns>True if the index was found and removed, false otherwise</returns>
    public async Task<bool> RemoveAsync(Guid id)
    {
        var fileIndex = await db.FileIndexes.FindAsync(id);
        if (fileIndex == null)
            return false;

        db.FileIndexes.Remove(fileIndex);
        await db.SaveChangesAsync();

        return true;
    }

    /// <summary>
    /// Removes file index entries by file ID
    /// </summary>
    /// <param name="fileId">The file ID</param>
    /// <returns>The number of indexes removed</returns>
    public async Task<int> RemoveByFileIdAsync(string fileId)
    {
        var indexes = await db.FileIndexes
            .Where(fi => fi.FileId == fileId)
            .ToListAsync();

        if (indexes.Count == 0)
            return 0;

        db.FileIndexes.RemoveRange(indexes);
        await db.SaveChangesAsync();

        return indexes.Count;
    }

    /// <summary>
    /// Removes file index entries by account ID and path
    /// </summary>
    /// <param name="accountId">The account ID</param>
    /// <param name="path">The parent folder path</param>
    /// <returns>The number of indexes removed</returns>
    public async Task<int> RemoveByPathAsync(Guid accountId, string path)
    {
        var normalizedPath = NormalizePath(path);

        var indexes = await db.FileIndexes
            .Where(fi => fi.AccountId == accountId && fi.Path == normalizedPath)
            .ToListAsync();

        if (!indexes.Any())
            return 0;

        db.FileIndexes.RemoveRange(indexes);
        await db.SaveChangesAsync();

        return indexes.Count;
    }

    /// <summary>
    /// Gets file indexes by account ID and path
    /// </summary>
    /// <param name="accountId">The account ID</param>
    /// <param name="path">The parent folder path</param>
    /// <returns>List of file indexes</returns>
    public async Task<List<SnCloudFileIndex>> GetByPathAsync(Guid accountId, string path)
    {
        var normalizedPath = NormalizePath(path);

        return await db.FileIndexes
            .Where(fi => fi.AccountId == accountId && fi.Path == normalizedPath)
            .Include(fi => fi.File)
            .ToListAsync();
    }

    /// <summary>
    /// Gets file indexes by file ID
    /// </summary>
    /// <param name="fileId">The file ID</param>
    /// <returns>List of file indexes</returns>
    public async Task<List<SnCloudFileIndex>> GetByFileIdAsync(string fileId)
    {
        return await db.FileIndexes
            .Where(fi => fi.FileId == fileId)
            .Include(fi => fi.File)
            .ToListAsync();
    }

    /// <summary>
    /// Gets all file indexes for an account
    /// </summary>
    /// <param name="accountId">The account ID</param>
    /// <returns>List of file indexes</returns>
    public async Task<List<SnCloudFileIndex>> GetByAccountIdAsync(Guid accountId)
    {
        return await db.FileIndexes
            .Where(fi => fi.AccountId == accountId)
            .Include(fi => fi.File)
            .ThenInclude(f => f.Object)
            .ToListAsync();
    }

    /// <summary>
    /// Normalizes the path to ensure it has a trailing slash and is query-safe
    /// </summary>
    /// <param name="path">The original path</param>
    /// <returns>The normalized path</returns>
    public static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "/";

        // Ensure the path starts with a slash
        if (!path.StartsWith('/'))
            path = "/" + path;

        // Ensure the path ends with a slash (unless it's just the root)
        if (path != "/" && !path.EndsWith('/'))
            path += "/";

        // Make path query-safe by removing problematic characters
        // This is a basic implementation - you might want to add more robust validation
        path = path.Replace("%", "").Replace("'", "").Replace("\"", "");

        return path;
    }
}