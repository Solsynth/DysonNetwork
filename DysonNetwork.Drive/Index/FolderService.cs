using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Drive.Index;

public class FolderService(AppDatabase db)
{
    /// <summary>
    /// Creates a new folder
    /// </summary>
    /// <param name="name">The folder name</param>
    /// <param name="accountId">The account ID</param>
    /// <param name="parentFolderId">Optional parent folder ID</param>
    /// <returns>The created folder</returns>
    public async Task<SnCloudFolder> CreateAsync(string name, Guid accountId, Guid? parentFolderId = null)
    {
        // Validate folder name
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Folder name cannot be empty", nameof(name));

        // Check if parent folder exists and belongs to the same account
        SnCloudFolder? parentFolder = null;
        if (parentFolderId.HasValue)
        {
            parentFolder = await db.Folders
                .FirstOrDefaultAsync(f => f.Id == parentFolderId && f.AccountId == accountId);

            if (parentFolder == null)
                throw new InvalidOperationException($"Parent folder with ID '{parentFolderId}' not found or access denied");
        }

        // Check if folder with same name already exists in the same location
        var existingFolder = await db.Folders
            .FirstOrDefaultAsync(f => f.AccountId == accountId && 
                                     f.ParentFolderId == parentFolderId && 
                                     f.Name == name);

        if (existingFolder != null)
        {
            throw new InvalidOperationException(
                $"A folder with name '{name}' already exists in the specified location");
        }

        var folder = SnCloudFolder.Create(name, accountId, parentFolder);
        db.Folders.Add(folder);
        await db.SaveChangesAsync();

        return folder;
    }

    /// <summary>
    /// Creates the root folder for an account (if it doesn't exist)
    /// </summary>
    /// <param name="accountId">The account ID</param>
    /// <returns>The root folder</returns>
    public async Task<SnCloudFolder> EnsureRootFolderAsync(Guid accountId)
    {
        var rootFolder = await db.Folders
            .FirstOrDefaultAsync(f => f.AccountId == accountId && f.ParentFolderId == null);

        if (rootFolder == null)
        {
            rootFolder = SnCloudFolder.CreateRoot(accountId);
            db.Folders.Add(rootFolder);
            await db.SaveChangesAsync();
        }

        return rootFolder;
    }

    /// <summary>
    /// Gets a folder by ID with its contents
    /// </summary>
    /// <param name="folderId">The folder ID</param>
    /// <param name="accountId">The account ID (for authorization)</param>
    /// <returns>The folder with child folders and files</returns>
    public async Task<SnCloudFolder?> GetByIdAsync(Guid folderId, Guid accountId)
    {
        return await db.Folders
            .Include(f => f.ChildFolders.OrderBy(cf => cf.Name))
            .Include(f => f.Files.OrderBy(fi => fi.File.Name))
                .ThenInclude(fi => fi.File)
            .FirstOrDefaultAsync(f => f.Id == folderId && f.AccountId == accountId);
    }

    /// <summary>
    /// Gets all folders for an account
    /// </summary>
    /// <param name="accountId">The account ID</param>
    /// <returns>List of folders</returns>
    public async Task<List<SnCloudFolder>> GetByAccountIdAsync(Guid accountId)
    {
        return await db.Folders
            .Where(f => f.AccountId == accountId)
            .Include(f => f.ParentFolder)
            .OrderBy(f => f.Path)
            .ToListAsync();
    }

    /// <summary>
    /// Gets child folders of a parent folder
    /// </summary>
    /// <param name="parentFolderId">The parent folder ID</param>
    /// <param name="accountId">The account ID</param>
    /// <returns>List of child folders</returns>
    public async Task<List<SnCloudFolder>> GetChildFoldersAsync(Guid parentFolderId, Guid accountId)
    {
        return await db.Folders
            .Where(f => f.ParentFolderId == parentFolderId && f.AccountId == accountId)
            .OrderBy(f => f.Name)
            .ToListAsync();
    }

    /// <summary>
    /// Updates a folder's name and path
    /// </summary>
    /// <param name="folderId">The folder ID</param>
    /// <param name="newName">The new folder name</param>
    /// <param name="accountId">The account ID</param>
    /// <returns>The updated folder</returns>
    public async Task<SnCloudFolder?> UpdateAsync(Guid folderId, string newName, Guid accountId)
    {
        var folder = await db.Folders
            .Include(f => f.ParentFolder)
            .FirstOrDefaultAsync(f => f.Id == folderId && f.AccountId == accountId);

        if (folder == null)
            return null;

        // Check if folder with same name already exists in the same location
        var existingFolder = await db.Folders
            .FirstOrDefaultAsync(f => f.AccountId == accountId && 
                                     f.ParentFolderId == folder.ParentFolderId && 
                                     f.Name == newName && f.Id != folderId);

        if (existingFolder != null)
        {
            throw new InvalidOperationException(
                $"A folder with name '{newName}' already exists in the specified location");
        }

        // Update folder name and path
        var oldPath = folder.Path;
        folder = SnCloudFolder.Create(newName, accountId, folder.ParentFolder);
        folder.Id = folderId; // Keep the same ID

        // Update all child folders' paths recursively
        await UpdateChildFolderPathsAsync(folderId, oldPath, folder.Path);

        db.Folders.Update(folder);
        await db.SaveChangesAsync();

        return folder;
    }

    /// <summary>
    /// Recursively updates child folder paths when a parent folder is renamed
    /// </summary>
    private async Task UpdateChildFolderPathsAsync(Guid parentFolderId, string oldParentPath, string newParentPath)
    {
        var childFolders = await db.Folders
            .Where(f => f.ParentFolderId == parentFolderId)
            .ToListAsync();

        foreach (var childFolder in childFolders)
        {
            var newPath = childFolder.Path.Replace(oldParentPath, newParentPath);
            childFolder.Path = newPath;

            // Recursively update grandchildren
            await UpdateChildFolderPathsAsync(childFolder.Id, oldParentPath, newParentPath);
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Deletes a folder and all its contents
    /// </summary>
    /// <param name="folderId">The folder ID</param>
    /// <param name="accountId">The account ID</param>
    /// <returns>True if the folder was deleted, false otherwise</returns>
    public async Task<bool> DeleteAsync(Guid folderId, Guid accountId)
    {
        var folder = await db.Folders
            .Include(f => f.ChildFolders)
            .Include(f => f.Files)
            .FirstOrDefaultAsync(f => f.Id == folderId && f.AccountId == accountId);

        if (folder == null)
            return false;

        // Recursively delete child folders
        foreach (var childFolder in folder.ChildFolders.ToList())
        {
            await DeleteAsync(childFolder.Id, accountId);
        }

        // Remove file indexes
        db.FileIndexes.RemoveRange(folder.Files);

        // Remove the folder itself
        db.Folders.Remove(folder);
        await db.SaveChangesAsync();

        return true;
    }

    /// <summary>
    /// Moves a folder to a new parent folder
    /// </summary>
    /// <param name="folderId">The folder ID</param>
    /// <param name="newParentFolderId">The new parent folder ID</param>
    /// <param name="accountId">The account ID</param>
    /// <returns>The moved folder</returns>
    public async Task<SnCloudFolder?> MoveAsync(Guid folderId, Guid? newParentFolderId, Guid accountId)
    {
        var folder = await db.Folders
            .FirstOrDefaultAsync(f => f.Id == folderId && f.AccountId == accountId);

        if (folder == null)
            return null;

        // Check if new parent exists and belongs to the same account
        SnCloudFolder? newParentFolder = null;
        if (newParentFolderId.HasValue)
        {
            newParentFolder = await db.Folders
                .FirstOrDefaultAsync(f => f.Id == newParentFolderId && f.AccountId == accountId);

            if (newParentFolder == null)
                throw new InvalidOperationException($"Target folder with ID '{newParentFolderId}' not found or access denied");
        }

        // Check for circular reference
        if (newParentFolderId.HasValue && await IsCircularReferenceAsync(folderId, newParentFolderId.Value))
        {
            throw new InvalidOperationException("Cannot move folder to its own descendant");
        }

        // Check if folder with same name already exists in the target location
        var existingFolder = await db.Folders
            .FirstOrDefaultAsync(f => f.AccountId == accountId && 
                                     f.ParentFolderId == newParentFolderId && 
                                     f.Name == folder.Name);

        if (existingFolder != null)
        {
            throw new InvalidOperationException(
                $"A folder with name '{folder.Name}' already exists in the target location");
        }

        var oldPath = folder.Path;
        var newPath = newParentFolder != null 
            ? $"{newParentFolder.Path.TrimEnd('/')}/{folder.Name}/"
            : $"/{folder.Name}/";

        // Update folder parent and path
        folder.ParentFolderId = newParentFolderId;
        folder.Path = newPath;

        // Update all child folders' paths recursively
        await UpdateChildFolderPathsAsync(folderId, oldPath, newPath);

        db.Folders.Update(folder);
        await db.SaveChangesAsync();

        return folder;
    }

    /// <summary>
    /// Checks if moving a folder would create a circular reference
    /// </summary>
    private async Task<bool> IsCircularReferenceAsync(Guid folderId, Guid potentialParentId)
    {
        if (folderId == potentialParentId)
            return true;

        var currentFolderId = potentialParentId;
        while (currentFolderId != Guid.Empty)
        {
            var currentFolder = await db.Folders
                .Where(f => f.Id == currentFolderId)
                .Select(f => new { f.Id, f.ParentFolderId })
                .FirstOrDefaultAsync();

            if (currentFolder == null)
                break;

            if (currentFolder.Id == folderId)
                return true;

            currentFolderId = currentFolder.ParentFolderId ?? Guid.Empty;
        }

        return false;
    }

    /// <summary>
    /// Searches for folders by name
    /// </summary>
    /// <param name="accountId">The account ID</param>
    /// <param name="searchTerm">The search term</param>
    /// <returns>List of matching folders</returns>
    public async Task<List<SnCloudFolder>> SearchAsync(Guid accountId, string searchTerm)
    {
        return await db.Folders
            .Where(f => f.AccountId == accountId && f.Name.Contains(searchTerm))
            .Include(f => f.ParentFolder)
            .OrderBy(f => f.Name)
            .ToListAsync();
    }
}
