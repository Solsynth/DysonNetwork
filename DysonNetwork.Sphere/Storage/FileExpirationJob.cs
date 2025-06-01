using Microsoft.EntityFrameworkCore;
using NodaTime;
using Quartz;

namespace DysonNetwork.Sphere.Storage;

/// <summary>
/// Job responsible for cleaning up expired file references
/// </summary>
public class FileExpirationJob(AppDatabase db, FileService fileService, ILogger<FileExpirationJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {        
        var now = SystemClock.Instance.GetCurrentInstant();
        logger.LogInformation("Running file reference expiration job at {now}", now);

        // Find all expired references
        var expiredReferences = await db.FileReferences
            .Where(r => r.ExpiredAt < now && r.ExpiredAt != null)
            .ToListAsync();

        if (!expiredReferences.Any())
        {
            logger.LogInformation("No expired file references found");
            return;
        }

        logger.LogInformation("Found {count} expired file references", expiredReferences.Count);

        // Get unique file IDs
        var fileIds = expiredReferences.Select(r => r.FileId).Distinct().ToList();
        var filesAndReferenceCount = new Dictionary<string, int>();

        // Delete expired references
        db.FileReferences.RemoveRange(expiredReferences);
        await db.SaveChangesAsync();

        // Check remaining references for each file
        foreach (var fileId in fileIds)
        {            
            var remainingReferences = await db.FileReferences
                .Where(r => r.FileId == fileId)
                .CountAsync();

            filesAndReferenceCount[fileId] = remainingReferences;

            // If no references remain, delete the file
            if (remainingReferences == 0)
            {
                var file = await db.Files.FirstOrDefaultAsync(f => f.Id == fileId);
                if (file != null)
                {
                    logger.LogInformation("Deleting file {fileId} as all references have expired", fileId);
                    await fileService.DeleteFileAsync(file);
                }
            }
            else
            {
                // Just purge the cache
                await fileService._PurgeCacheAsync(fileId);
            }
        }

        logger.LogInformation("Completed file reference expiration job");
    }
}
