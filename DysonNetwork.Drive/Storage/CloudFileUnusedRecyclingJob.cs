using Microsoft.EntityFrameworkCore;
using NodaTime;
using DysonNetwork.Shared.Models;
using Quartz;

namespace DysonNetwork.Drive.Storage;

public class CloudFileUnusedRecyclingJob(
    AppDatabase db,
    ILogger<CloudFileUnusedRecyclingJob> logger,
    IConfiguration configuration
)
    : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("Cleaning tus cloud files...");
        var storePath = configuration["Storage:Uploads"];
        if (Directory.Exists(storePath))
        {
            var oneHourAgo = SystemClock.Instance.GetCurrentInstant() - Duration.FromHours(1);
            var files = Directory.GetFiles(storePath);
            foreach (var file in files)
            {
                var creationTime = File.GetCreationTime(file).ToUniversalTime();
                if (creationTime < oneHourAgo.ToDateTimeUtc())
                    File.Delete(file);
            }
        }

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
            .Where(f => f.FileIndexes.Count == 0)
            .Where(f => f.Object!.FileReplicas.Any(r => r.PoolId.HasValue && recyclablePools.Contains(r.PoolId.Value)))
            .Where(f => !f.IsMarkedRecycle)
            .Include(f => f.Object)
            .ThenInclude(o => o.FileReplicas)
            .CountAsync();

        logger.LogInformation("Found {TotalFiles} files to check for unused status", totalFiles);

        // Define a timestamp to limit the age of files we're processing in this run
        // This spreads processing across multiple job runs for very large databases
        var ageThreshold = now - Duration.FromDays(30); // Process files up to 90 days old in this run

        // Instead of loading all files at once, use pagination
        var hasMoreFiles = true;
        string? lastProcessedId = null;

        while (hasMoreFiles)
        {
            IQueryable<SnCloudFile> baseQuery = db.Files
                .Where(f => f.Object!.FileReplicas.Any(r => r.PoolId.HasValue && recyclablePools.Contains(r.PoolId.Value)))
                .Where(f => !f.IsMarkedRecycle)
                .Where(f => f.CreatedAt <= ageThreshold)
                .Include(f => f.Object)
                .ThenInclude(o => o.FileReplicas);

            if (lastProcessedId != null)
                baseQuery = baseQuery.Where(f => string.Compare(f.Id, lastProcessedId) > 0);

            var fileBatch = await baseQuery
                .OrderBy(f => f.Id)
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

            // Optimized query: Find files that have no file object or no replicas
            // A file is considered "unused" if its file object has no replicas
            var filesToMark = await db.Files
                .Where(f => fileBatch.Contains(f.Id))
                .Where(f => f.Object == null || f.Object.FileReplicas.Count == 0)
                .Select(f => f.Id)
                .ToListAsync();

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
