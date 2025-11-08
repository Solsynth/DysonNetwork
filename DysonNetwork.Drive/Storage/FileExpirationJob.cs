using Microsoft.EntityFrameworkCore;
using NodaTime;
using Quartz;

namespace DysonNetwork.Drive.Storage;

/// <summary>
/// Job responsible for cleaning up expired file references
/// </summary>
public class FileExpirationJob(AppDatabase db, FileService fileService, ILogger<FileExpirationJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        logger.LogInformation("Running file reference expiration job at {now}", now);

        // Delete expired references in bulk and get affected file IDs
        var affectedFileIds = await db.FileReferences
            .Where(r => r.ExpiredAt < now && r.ExpiredAt != null)
            .Select(r => r.FileId)
            .Distinct()
            .ToListAsync();

        if (!affectedFileIds.Any())
        {
            logger.LogInformation("No expired file references found");
            return;
        }

        logger.LogInformation("Found expired references for {count} files", affectedFileIds.Count);

        // Delete expired references in bulk
        var deletedReferencesCount = await db.FileReferences
            .Where(r => r.ExpiredAt < now && r.ExpiredAt != null)
            .ExecuteDeleteAsync();

        logger.LogInformation("Deleted {count} expired file references", deletedReferencesCount);

        // Find files that now have no remaining references (bulk operation)
        var filesToDelete = await db.Files
            .Where(f => affectedFileIds.Contains(f.Id))
            .Where(f => !db.FileReferences.Any(r => r.FileId == f.Id))
            .Select(f => f.Id)
            .ToListAsync();

        if (filesToDelete.Any())
        {
            logger.LogInformation("Deleting {count} files that have no remaining references", filesToDelete.Count);

            // Get files for deletion
            var files = await db.Files
                .Where(f => filesToDelete.Contains(f.Id))
                .ToListAsync();

            // Delete files and their data in parallel
            var deleteTasks = files.Select(f => fileService.DeleteFileAsync(f));
            await Task.WhenAll(deleteTasks);
        }

        // Purge cache for files that still have references
        var filesWithRemainingRefs = affectedFileIds.Except(filesToDelete).ToList();
        if (filesWithRemainingRefs.Any())
        {
            var cachePurgeTasks = filesWithRemainingRefs.Select(fileService._PurgeCacheAsync);
            await Task.WhenAll(cachePurgeTasks);
        }

        logger.LogInformation("Completed file reference expiration job");
    }
}
