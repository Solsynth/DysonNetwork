using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Drive.Index;

public class FileIndexService(AppDatabase db, FolderService folderService)
{
    /// <summary>
    /// Normalizes a path to ensure consistent formatting
    /// </summary>
    /// <param name="path">The path to normalize</param>
    /// <returns>The normalized path</returns>
    public static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "/";

        // Ensure path starts with /
        if (!path.StartsWith('/'))
            path = "/" + path;

        // Remove trailing slash unless it's the root
        if (path.Length > 1 && path.EndsWith('/'))
            path = path.TrimEnd('/');

        // Normalize double slashes
        while (path.Contains("//"))
            path = path.Replace("//", "/");

        return path;
    }

    /// <summary>
    /// Gets or creates a folder hierarchy based on a file path
    /// </summary>
    /// <param name="filePath">The file path (e.g., "/folder/sub/file.txt")</param>
    /// <param name="accountId">The account ID</param>
    /// <returns>The folder where the file should be placed</returns>
    private async Task<SnCloudFolder> GetOrCreateFolderByPathAsync(string filePath, Guid accountId)
    {
        // Extract folder path from file path (remove filename)
        var lastSlashIndex = filePath.LastIndexOf('/');
        var folderPath = lastSlashIndex == 0 ? "/" : filePath[..(lastSlashIndex + 1)];

        // Ensure root folder exists
        var rootFolder = await folderService.EnsureRootFolderAsync(accountId);

        // If it's the root folder, return it
        if (folderPath == "/")
            return rootFolder;

        // Split the folder path into segments
        var pathSegments = folderPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        var currentParent = rootFolder;
        var currentPath = "/";

        // Create folder hierarchy
        foreach (var segment in pathSegments)
        {
            currentPath += segment + "/";

            // Check if folder already exists
            var existingFolder = await db.Folders
                .FirstOrDefaultAsync(f => f.AccountId == accountId && f.Path == currentPath);

            if (existingFolder != null)
            {
                currentParent = existingFolder;
                continue;
            }

            // Create new folder
            var newFolder = await folderService.CreateAsync(segment, accountId, currentParent.Id);
            currentParent = newFolder;
        }

        return currentParent;
    }
    /// <summary>
    /// Creates a new file index entry at a specific path (creates folder hierarchy if needed)
    /// </summary>
    /// <param name="path">The path where the file should be indexed</param>
    /// <param name="fileId">The file ID</param>
    /// <param name="accountId">The account ID</param>
    /// <returns>The created file index</returns>
    public async Task<SnCloudFileIndex> CreateAsync(string path, string fileId, Guid accountId)
    {
        var normalizedPath = NormalizePath(path);

        // Get the file to extract the file name
        var file = await db.Files
            .FirstOrDefaultAsync(f => f.Id == fileId) ?? throw new InvalidOperationException($"File with ID '{fileId}' not found");

        // Get or create the folder hierarchy based on the path
        var folder = await GetOrCreateFolderByPathAsync(normalizedPath, accountId);

        // Check if a file with the same name already exists in the same folder for this account
        var existingFileIndex = await db.FileIndexes
            .FirstOrDefaultAsync(fi => fi.AccountId == accountId &&
                                     fi.FolderId == folder.Id &&
                                     fi.File.Name == file.Name);

        if (existingFileIndex != null)
        {
            throw new InvalidOperationException(
                $"A file with name '{file.Name}' already exists in folder '{folder.Name}' for account '{accountId}'");
        }

        var fileIndex = SnCloudFileIndex.Create(folder, file, accountId);
        db.FileIndexes.Add(fileIndex);
        await db.SaveChangesAsync();

        return fileIndex;
    }

    /// <summary>
    /// Creates a new file index entry in a specific folder
    /// </summary>
    /// <param name="folderId">The folder ID where the file should be placed</param>
    /// <param name="fileId">The file ID</param>
    /// <param name="accountId">The account ID</param>
    /// <returns>The created file index</returns>
    public async Task<SnCloudFileIndex> CreateInFolderAsync(Guid folderId, string fileId, Guid accountId)
    {
        // Verify the folder exists and belongs to the account
        var folder = await db.Folders
            .FirstOrDefaultAsync(f => f.Id == folderId && f.AccountId == accountId);

        if (folder == null)
        {
            throw new InvalidOperationException($"Folder with ID '{folderId}' not found or access denied");
        }

        // Get the file to extract the file name
        var file = await db.Files
            .FirstOrDefaultAsync(f => f.Id == fileId);

        if (file == null)
        {
            throw new InvalidOperationException($"File with ID '{fileId}' not found");
        }

        // Check if a file with the same name already exists in the same folder for this account
        var existingFileIndex = await db.FileIndexes
            .FirstOrDefaultAsync(fi => fi.AccountId == accountId &&
                                     fi.FolderId == folderId &&
                                     fi.File.Name == file.Name);

        if (existingFileIndex != null)
        {
            throw new InvalidOperationException(
                $"A file with name '{file.Name}' already exists in folder '{folder.Name}' for account '{accountId}'");
        }

        var fileIndex = SnCloudFileIndex.Create(folder, file, accountId);
        db.FileIndexes.Add(fileIndex);
        await db.SaveChangesAsync();

        return fileIndex;
    }

    /// <summary>
    /// Moves a file to a different folder
    /// </summary>
    /// <param name="fileIndexId">The file index ID</param>
    /// <param name="newFolderId">The new folder ID</param>
    /// <param name="accountId">The account ID</param>
    /// <returns>The updated file index</returns>
    public async Task<SnCloudFileIndex?> MoveAsync(Guid fileIndexId, Guid newFolderId, Guid accountId)
    {
        var fileIndex = await db.FileIndexes
            .Include(fi => fi.File)
            .FirstOrDefaultAsync(fi => fi.Id == fileIndexId && fi.AccountId == accountId);

        if (fileIndex == null)
            return null;

        // Verify the new folder exists and belongs to the account
        var newFolder = await db.Folders
            .FirstOrDefaultAsync(f => f.Id == newFolderId && f.AccountId == accountId);

        if (newFolder == null)
        {
            throw new InvalidOperationException($"Target folder with ID '{newFolderId}' not found or access denied");
        }

        // Check if a file with the same name already exists in the target folder
        var existingFileIndex = await db.FileIndexes
            .FirstOrDefaultAsync(fi => fi.AccountId == accountId &&
                                     fi.FolderId == newFolderId &&
                                     fi.File.Name == fileIndex.File.Name &&
                                     fi.Id != fileIndexId);

        if (existingFileIndex != null)
        {
            throw new InvalidOperationException(
                $"A file with name '{fileIndex.File.Name}' already exists in folder '{newFolder.Name}'");
        }

        // Since properties are init-only, we need to remove the old index and create a new one
        db.FileIndexes.Remove(fileIndex);

        var newFileIndex = SnCloudFileIndex.Create(newFolder, fileIndex.File, accountId);
        newFileIndex.Id = fileIndexId; // Keep the same ID

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
    /// Removes file index entries by account ID and folder
    /// </summary>
    /// <param name="accountId">The account ID</param>
    /// <param name="folderId">The folder ID</param>
    /// <returns>The number of indexes removed</returns>
    public async Task<int> RemoveByFolderAsync(Guid accountId, Guid folderId)
    {
        var indexes = await db.FileIndexes
            .Where(fi => fi.AccountId == accountId && fi.FolderId == folderId)
            .ToListAsync();

        if (!indexes.Any())
            return 0;

        db.FileIndexes.RemoveRange(indexes);
        await db.SaveChangesAsync();

        return indexes.Count;
    }

    /// <summary>
    /// Gets file indexes by account ID and folder
    /// </summary>
    /// <param name="accountId">The account ID</param>
    /// <param name="folderId">The folder ID</param>
    /// <returns>List of file indexes</returns>
    public async Task<List<SnCloudFileIndex>> GetByFolderAsync(Guid accountId, Guid folderId)
    {
        return await db.FileIndexes
            .Where(fi => fi.AccountId == accountId && fi.FolderId == folderId)
            .Include(fi => fi.File)
            .ToListAsync();
    }

    /// <summary>
    /// Gets file indexes by file ID with folder information
    /// </summary>
    /// <param name="fileId">The file ID</param>
    /// <returns>List of file indexes</returns>
    public async Task<List<SnCloudFileIndex>> GetByFileIdAsync(string fileId)
    {
        return await db.FileIndexes
            .Where(fi => fi.FileId == fileId)
            .Include(fi => fi.File)
            .Include(fi => fi.Folder)
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
            .Include(fi => fi.Folder)
            .ToListAsync();
    }

    /// <summary>
    /// Gets file indexes by path for an account (finds folder by path and gets files in that folder)
    /// </summary>
    /// <param name="accountId">The account ID</param>
    /// <param name="path">The path to search for</param>
    /// <returns>List of file indexes at the specified path</returns>
    public async Task<List<SnCloudFileIndex>> GetByPathAsync(Guid accountId, string path)
    {
        var normalizedPath = NormalizePath(path);

        // Find the folder that corresponds to this path
        var folder = await db.Folders
            .FirstOrDefaultAsync(f => f.AccountId == accountId && f.Path == normalizedPath + (normalizedPath == "/" ? "" : "/"));

        if (folder == null)
            return new List<SnCloudFileIndex>();

        return await db.FileIndexes
            .Where(fi => fi.AccountId == accountId && fi.FolderId == folder.Id)
            .Include(fi => fi.File)
            .Include(fi => fi.Folder)
            .ToListAsync();
    }

    /// <summary>
    /// Updates the path of a file index
    /// </summary>
    /// <param name="fileIndexId">The file index ID</param>
    /// <param name="newPath">The new path</param>
    /// <returns>The updated file index, or null if not found</returns>
    public async Task<SnCloudFileIndex?> UpdateAsync(Guid fileIndexId, string newPath)
    {
        var fileIndex = await db.FileIndexes
            .Include(fi => fi.File)
            .Include(fi => fi.Folder)
            .FirstOrDefaultAsync(fi => fi.Id == fileIndexId);

        if (fileIndex == null)
            return null;

        var normalizedPath = NormalizePath(newPath);

        // Get or create the folder hierarchy based on the new path
        var newFolder = await GetOrCreateFolderByPathAsync(normalizedPath, fileIndex.AccountId);

        // Check if a file with the same name already exists in the new folder
        var existingFileIndex = await db.FileIndexes
            .FirstOrDefaultAsync(fi => fi.AccountId == fileIndex.AccountId &&
                                     fi.FolderId == newFolder.Id &&
                                     fi.File.Name == fileIndex.File.Name &&
                                     fi.Id != fileIndexId);

        if (existingFileIndex != null)
        {
            throw new InvalidOperationException(
                $"A file with name '{fileIndex.File.Name}' already exists in folder '{newFolder.Name}'");
        }

        // Since properties are init-only, we need to remove the old index and create a new one
        db.FileIndexes.Remove(fileIndex);

        var updatedFileIndex = SnCloudFileIndex.Create(newFolder, fileIndex.File, fileIndex.AccountId);
        updatedFileIndex.Id = fileIndexId; // Keep the same ID

        db.FileIndexes.Add(updatedFileIndex);
        await db.SaveChangesAsync();

        return updatedFileIndex;
    }

    /// <summary>
    /// Removes all file index entries at a specific path for an account (finds folder by path and removes files from that folder)
    /// </summary>
    /// <param name="accountId">The account ID</param>
    /// <param name="path">The path to clear</param>
    /// <returns>The number of indexes removed</returns>
    public async Task<int> RemoveByPathAsync(Guid accountId, string path)
    {
        var normalizedPath = NormalizePath(path);

        // Find the folder that corresponds to this path
        var folder = await db.Folders
            .FirstOrDefaultAsync(f => f.AccountId == accountId && f.Path == normalizedPath + (normalizedPath == "/" ? "" : "/"));

        if (folder == null)
            return 0;

        var indexes = await db.FileIndexes
            .Where(fi => fi.AccountId == accountId && fi.FolderId == folder.Id)
            .ToListAsync();

        if (!indexes.Any())
            return 0;

        db.FileIndexes.RemoveRange(indexes);
        await db.SaveChangesAsync();

        return indexes.Count;
    }
}
