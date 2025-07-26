using DysonNetwork.Shared.Cache;
using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Drive.Storage;

public class FileReferenceService(AppDatabase db, FileService fileService, ICacheService cache)
{
    private const string CacheKeyPrefix = "file:ref:";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Creates a new reference to a file for a specific resource
    /// </summary>
    /// <param name="fileId">The ID of the file to reference</param>
    /// <param name="usage">The usage context (e.g., "avatar", "post-attachment")</param>
    /// <param name="resourceId">The ID of the resource using the file</param>
    /// <param name="expiredAt">Optional expiration time for the file</param>
    /// <param name="duration">Optional duration after which the file expires (alternative to expiredAt)</param>
    /// <returns>The created file reference</returns>
    public async Task<CloudFileReference> CreateReferenceAsync(
        string fileId,
        string usage,
        string resourceId,
        Instant? expiredAt = null,
        Duration? duration = null
    )
    {
        // Calculate expiration time if needed
        var finalExpiration = expiredAt;
        if (duration.HasValue)
            finalExpiration = SystemClock.Instance.GetCurrentInstant() + duration.Value;

        var reference = new CloudFileReference
        {
            FileId = fileId,
            Usage = usage,
            ResourceId = resourceId,
            ExpiredAt = finalExpiration
        };

        db.FileReferences.Add(reference);

        await db.SaveChangesAsync();
        await fileService._PurgeCacheAsync(fileId);

        return reference;
    }

    public async Task<List<CloudFileReference>> CreateReferencesAsync(
        List<string> fileId,
        string usage,
        string resourceId,
        Instant? expiredAt = null,
        Duration? duration = null
    )
    {
        var data = fileId.Select(id => new CloudFileReference
        {
            FileId = id,
            Usage = usage,
            ResourceId = resourceId,
            ExpiredAt = expiredAt ?? SystemClock.Instance.GetCurrentInstant() + duration
        }).ToList();
        await db.BulkInsertAsync(data);
        return data;
    }

    /// <summary>
    /// Gets all references to a file
    /// </summary>
    /// <param name="fileId">The ID of the file</param>
    /// <returns>A list of all references to the file</returns>
    public async Task<List<CloudFileReference>> GetReferencesAsync(string fileId)
    {
        var cacheKey = $"{CacheKeyPrefix}list:{fileId}";

        var cachedReferences = await cache.GetAsync<List<CloudFileReference>>(cacheKey);
        if (cachedReferences is not null)
            return cachedReferences;

        var references = await db.FileReferences
            .Where(r => r.FileId == fileId)
            .ToListAsync();

        await cache.SetAsync(cacheKey, references, CacheDuration);

        return references;
    }

    public async Task<Dictionary<string, List<CloudFileReference>>> GetReferencesAsync(IEnumerable<string> fileId)
    {
        var references = await db.FileReferences
            .Where(r => fileId.Contains(r.FileId))
            .GroupBy(r => r.FileId)
            .ToDictionaryAsync(r => r.Key, r => r.ToList());
        return references;
    }

    /// <summary>
    /// Gets the number of references to a file
    /// </summary>
    /// <param name="fileId">The ID of the file</param>
    /// <returns>The number of references to the file</returns>
    public async Task<int> GetReferenceCountAsync(string fileId)
    {
        var cacheKey = $"{CacheKeyPrefix}count:{fileId}";

        var cachedCount = await cache.GetAsync<int?>(cacheKey);
        if (cachedCount.HasValue)
            return cachedCount.Value;

        var count = await db.FileReferences
            .Where(r => r.FileId == fileId)
            .CountAsync();

        await cache.SetAsync(cacheKey, count, CacheDuration);

        return count;
    }

    /// <summary>
    /// Gets all references for a specific resource
    /// </summary>
    /// <param name="resourceId">The ID of the resource</param>
    /// <returns>A list of file references associated with the resource</returns>
    public async Task<List<CloudFileReference>> GetResourceReferencesAsync(string resourceId)
    {
        var cacheKey = $"{CacheKeyPrefix}resource:{resourceId}";

        var cachedReferences = await cache.GetAsync<List<CloudFileReference>>(cacheKey);
        if (cachedReferences is not null)
            return cachedReferences;

        var references = await db.FileReferences
            .Where(r => r.ResourceId == resourceId)
            .ToListAsync();

        await cache.SetAsync(cacheKey, references, CacheDuration);

        return references;
    }

    /// <summary>
    /// Gets all file references for a specific usage context
    /// </summary>
    /// <param name="usage">The usage context</param>
    /// <returns>A list of file references with the specified usage</returns>
    public async Task<List<CloudFileReference>> GetUsageReferencesAsync(string usage)
    {
        return await db.FileReferences
            .Where(r => r.Usage == usage)
            .ToListAsync();
    }

    /// <summary>
    /// Deletes references for a specific resource
    /// </summary>
    /// <param name="resourceId">The ID of the resource</param>
    /// <returns>The number of deleted references</returns>
    public async Task<int> DeleteResourceReferencesAsync(string resourceId)
    {
        var references = await db.FileReferences
            .Where(r => r.ResourceId == resourceId)
            .ToListAsync();

        var fileIds = references.Select(r => r.FileId).Distinct().ToList();

        db.FileReferences.RemoveRange(references);
        var deletedCount = await db.SaveChangesAsync();

        // Purge caches
        var tasks = fileIds.Select(fileService._PurgeCacheAsync).ToList();
        tasks.Add(PurgeCacheForResourceAsync(resourceId));
        await Task.WhenAll(tasks);

        return deletedCount;
    }

    /// <summary>
    /// Deletes references for a specific resource and usage
    /// </summary>
    /// <param name="resourceId">The ID of the resource</param>
    /// <param name="usage">The usage context</param>
    /// <returns>The number of deleted references</returns>
    public async Task<int> DeleteResourceReferencesAsync(string resourceId, string usage)
    {
        var references = await db.FileReferences
            .Where(r => r.ResourceId == resourceId && r.Usage == usage)
            .ToListAsync();

        if (!references.Any())
        {
            return 0;
        }

        var fileIds = references.Select(r => r.FileId).Distinct().ToList();

        db.FileReferences.RemoveRange(references);
        var deletedCount = await db.SaveChangesAsync();

        // Purge caches
        var tasks = fileIds.Select(fileService._PurgeCacheAsync).ToList();
        tasks.Add(PurgeCacheForResourceAsync(resourceId));
        await Task.WhenAll(tasks);

        return deletedCount;
    }

    /// <summary>
    /// Deletes a specific file reference
    /// </summary>
    /// <param name="referenceId">The ID of the reference to delete</param>
    /// <returns>True if the reference was deleted, false otherwise</returns>
    public async Task<bool> DeleteReferenceAsync(Guid referenceId)
    {
        var reference = await db.FileReferences
            .FirstOrDefaultAsync(r => r.Id == referenceId);

        if (reference == null)
            return false;

        db.FileReferences.Remove(reference);
        await db.SaveChangesAsync();

        // Purge caches
        await fileService._PurgeCacheAsync(reference.FileId);
        await PurgeCacheForResourceAsync(reference.ResourceId);
        await PurgeCacheForFileAsync(reference.FileId);

        return true;
    }

    /// <summary>
    /// Updates the files referenced by a resource
    /// </summary>
    /// <param name="resourceId">The ID of the resource</param>
    /// <param name="newFileIds">The new list of file IDs</param>
    /// <param name="usage">The usage context</param>
    /// <param name="expiredAt">Optional expiration time for newly added files</param>
    /// <param name="duration">Optional duration after which newly added files expire</param>
    /// <returns>A list of the updated file references</returns>
    public async Task<List<CloudFileReference>> UpdateResourceFilesAsync(
        string resourceId,
        IEnumerable<string>? newFileIds,
        string usage,
        Instant? expiredAt = null,
        Duration? duration = null)
    {
        if (newFileIds == null)
            return new List<CloudFileReference>();

        var existingReferences = await db.FileReferences
            .Where(r => r.ResourceId == resourceId && r.Usage == usage)
            .ToListAsync();

        var existingFileIds = existingReferences.Select(r => r.FileId).ToHashSet();
        var newFileIdsList = newFileIds.ToList();
        var newFileIdsSet = newFileIdsList.ToHashSet();

        // Files to remove
        var toRemove = existingReferences
            .Where(r => !newFileIdsSet.Contains(r.FileId))
            .ToList();

        // Files to add
        var toAdd = newFileIdsList
            .Where(id => !existingFileIds.Contains(id))
            .Select(id => new CloudFileReference
            {
                FileId = id,
                Usage = usage,
                ResourceId = resourceId
            })
            .ToList();

        // Apply changes
        if (toRemove.Any())
            db.FileReferences.RemoveRange(toRemove);

        if (toAdd.Any())
            db.FileReferences.AddRange(toAdd);

        await db.SaveChangesAsync();

        // Update expiration for newly added references if specified
        if ((expiredAt.HasValue || duration.HasValue) && toAdd.Any())
        {
            var finalExpiration = expiredAt;
            if (duration.HasValue)
            {
                finalExpiration = SystemClock.Instance.GetCurrentInstant() + duration.Value;
            }

            // Update newly added references with the expiration time
            var referenceIds = await db.FileReferences
                .Where(r => toAdd.Select(a => a.FileId).Contains(r.FileId) &&
                            r.ResourceId == resourceId &&
                            r.Usage == usage)
                .Select(r => r.Id)
                .ToListAsync();

            await db.FileReferences
                .Where(r => referenceIds.Contains(r.Id))
                .ExecuteUpdateAsync(setter => setter.SetProperty(
                    r => r.ExpiredAt,
                    _ => finalExpiration
                ));
        }

        // Purge caches
        var allFileIds = existingFileIds.Union(newFileIdsSet).ToList();
        var tasks = allFileIds.Select(fileService._PurgeCacheAsync).ToList();
        tasks.Add(PurgeCacheForResourceAsync(resourceId));
        await Task.WhenAll(tasks);

        // Return updated references
        return await db.FileReferences
            .Where(r => r.ResourceId == resourceId && r.Usage == usage)
            .ToListAsync();
    }

    /// <summary>
    /// Gets all files referenced by a resource
    /// </summary>
    /// <param name="resourceId">The ID of the resource</param>
    /// <param name="usage">Optional filter by usage context</param>
    /// <returns>A list of files referenced by the resource</returns>
    public async Task<List<CloudFile>> GetResourceFilesAsync(string resourceId, string? usage = null)
    {
        var query = db.FileReferences.Where(r => r.ResourceId == resourceId);

        if (usage != null)
            query = query.Where(r => r.Usage == usage);

        var references = await query.ToListAsync();
        var fileIds = references.Select(r => r.FileId).ToList();

        return await db.Files
            .Where(f => fileIds.Contains(f.Id))
            .ToListAsync();
    }

    /// <summary>
    /// Purges all caches related to a resource
    /// </summary>
    private async Task PurgeCacheForResourceAsync(string resourceId)
    {
        var cacheKey = $"{CacheKeyPrefix}resource:{resourceId}";
        await cache.RemoveAsync(cacheKey);
    }

    /// <summary>
    /// Purges all caches related to a file
    /// </summary>
    private async Task PurgeCacheForFileAsync(string fileId)
    {
        var cacheKeys = new[]
        {
            $"{CacheKeyPrefix}list:{fileId}",
            $"{CacheKeyPrefix}count:{fileId}"
        };

        var tasks = cacheKeys.Select(cache.RemoveAsync);
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Updates the expiration time for a file reference
    /// </summary>
    /// <param name="referenceId">The ID of the reference</param>
    /// <param name="expiredAt">The new expiration time, or null to remove expiration</param>
    /// <returns>True if the reference was found and updated, false otherwise</returns>
    public async Task<bool> SetReferenceExpirationAsync(Guid referenceId, Instant? expiredAt)
    {
        var reference = await db.FileReferences
            .FirstOrDefaultAsync(r => r.Id == referenceId);

        if (reference == null)
            return false;

        reference.ExpiredAt = expiredAt;
        await db.SaveChangesAsync();

        await PurgeCacheForFileAsync(reference.FileId);
        await PurgeCacheForResourceAsync(reference.ResourceId);

        return true;
    }

    /// <summary>
    /// Updates the expiration time for all references to a file
    /// </summary>
    /// <param name="fileId">The ID of the file</param>
    /// <param name="expiredAt">The new expiration time, or null to remove expiration</param>
    /// <returns>The number of references updated</returns>
    public async Task<int> SetFileReferencesExpirationAsync(string fileId, Instant? expiredAt)
    {
        var rowsAffected = await db.FileReferences
            .Where(r => r.FileId == fileId)
            .ExecuteUpdateAsync(setter => setter.SetProperty(
                r => r.ExpiredAt,
                _ => expiredAt
            ));

        if (rowsAffected > 0)
        {
            await fileService._PurgeCacheAsync(fileId);
            await PurgeCacheForFileAsync(fileId);
        }

        return rowsAffected;
    }

    /// <summary>
    /// Get all file references for a specific resource and usage type
    /// </summary>
    /// <param name="resourceId">The resource ID</param>
    /// <param name="usageType">The usage type</param>
    /// <returns>List of file references</returns>
    public async Task<List<CloudFileReference>> GetResourceReferencesAsync(string resourceId, string usageType)
    {
        return await db.FileReferences
            .Where(r => r.ResourceId == resourceId && r.Usage == usageType)
            .ToListAsync();
    }

    /// <summary>
    /// Check if a file has any references
    /// </summary>
    /// <param name="fileId">The file ID to check</param>
    /// <returns>True if the file has references, false otherwise</returns>
    public async Task<bool> HasFileReferencesAsync(string fileId)
    {
        return await db.FileReferences.AnyAsync(r => r.FileId == fileId);
    }

    /// <summary>
    /// Updates the expiration time for a file reference using a duration from now
    /// </summary>
    /// <param name="referenceId">The ID of the reference</param>
    /// <param name="duration">The duration after which the reference expires, or null to remove expiration</param>
    /// <returns>True if the reference was found and updated, false otherwise</returns>
    public async Task<bool> SetReferenceExpirationDurationAsync(Guid referenceId, Duration? duration)
    {
        Instant? expiredAt = null;
        if (duration.HasValue)
        {
            expiredAt = SystemClock.Instance.GetCurrentInstant() + duration.Value;
        }

        return await SetReferenceExpirationAsync(referenceId, expiredAt);
    }
}