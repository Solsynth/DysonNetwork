using Microsoft.EntityFrameworkCore;
using NodaTime;
using Quartz;

namespace DysonNetwork.Drive.Storage;

public class CloudFileUnusedRecyclingJob(
    AppDatabase db,
    FileReferenceService fileRefService,
    ILogger<CloudFileUnusedRecyclingJob> logger
)
    : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("Marking unused cloud files...");

        var recyclablePools = await db.Pools
            .Where(p => p.PolicyConfig.EnableRecycle)
            .Select(p => p.Id)
            .ToListAsync();

        var now = SystemClock.Instance.GetCurrentInstant();
        const int batchSize = 1000; // Process larger batches for efficiency
        var processedCount = 0;
        var markedCount = 0;
        var totalFiles = await db.Files
            .Where(f => f.PoolId.HasValue && recyclablePools.Contains(f.PoolId.Value))
            .Where(f => !f.IsMarkedRecycle)
            .CountAsync();

        logger.LogInformation("Found {TotalFiles} files to check for unused status", totalFiles);

        // Define a timestamp to limit the age of files we're processing in this run
        // This spreads the processing across multiple job runs for very large databases
        var ageThreshold = now - Duration.FromDays(30); // Process files up to 90 days old in this run

        // Instead of loading all files at once, use pagination
        var hasMoreFiles = true;
        string? lastProcessedId = null;

        while (hasMoreFiles)
        {
            // Query for the next batch of files using keyset pagination
            var filesQuery = db.Files
                .Where(f => f.PoolId.HasValue && recyclablePools.Contains(f.PoolId.Value))
                .Where(f => !f.IsMarkedRecycle)
                .Where(f => f.CreatedAt <= ageThreshold); // Only process older files first

            if (lastProcessedId != null)
                filesQuery = filesQuery.Where(f => string.Compare(f.Id, lastProcessedId) > 0);

            var fileBatch = await filesQuery
                .OrderBy(f => f.Id) // Ensure consistent ordering for pagination
                .Take(batchSize)
                .Select(f => f.Id)
                .ToListAsync();

            if (fileBatch.Count == 0)
            {
                hasMoreFiles = false;
                continue;
            }

            processedCount += fileBatch.Count;
            lastProcessedId = fileBatch.Last();

            // Get all relevant file references for this batch
            var fileReferences = await fileRefService.GetReferencesAsync(fileBatch);

            // Filter to find files that have no references or all expired references
            var filesToMark = fileBatch.Where(fileId =>
                !fileReferences.TryGetValue(fileId, out var references) ||
                references.Count == 0 ||
                references.All(r => r.ExpiredAt.HasValue && r.ExpiredAt.Value <= now)
            ).ToList();

            if (filesToMark.Count > 0)
            {
                // Use a bulk update for better performance - mark all qualifying files at once
                var updateCount = await db.Files
                    .Where(f => filesToMark.Contains(f.Id))
                    .ExecuteUpdateAsync(setter => setter
                        .SetProperty(f => f.IsMarkedRecycle, true));

                markedCount += updateCount;
            }

            // Log progress periodically
            if (processedCount % 10000 == 0 || !hasMoreFiles)
            {
                logger.LogInformation(
                    "Progress: processed {ProcessedCount}/{TotalFiles} files, marked {MarkedCount} for recycling",
                    processedCount,
                    totalFiles,
                    markedCount
                );
            }
        }
        
        var expiredCount = await db.Files
            .Where(f => f.ExpiredAt.HasValue && f.ExpiredAt.Value <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.IsMarkedRecycle, true));
        markedCount += expiredCount;

        logger.LogInformation("Completed marking {MarkedCount} files for recycling", markedCount);
    }
}