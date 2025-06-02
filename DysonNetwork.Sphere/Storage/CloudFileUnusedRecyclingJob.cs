using Microsoft.EntityFrameworkCore;
using NodaTime;
using Quartz;

namespace DysonNetwork.Sphere.Storage;

public class CloudFileUnusedRecyclingJob(
    AppDatabase db,
    FileService fs,
    FileReferenceService fileRefService,
    ILogger<CloudFileUnusedRecyclingJob> logger
)
    : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        return;
        logger.LogInformation("Deleting unused cloud files...");

        var cutoff = SystemClock.Instance.GetCurrentInstant() - Duration.FromHours(1);
        var now = SystemClock.Instance.GetCurrentInstant();

        // Get files that are either expired or created more than an hour ago
        var fileIds = await db.Files
            .Select(f => f.Id)
            .ToListAsync();

        // Filter to only include files that have no references or all references have expired
        var deletionPlan = new List<string>();
        foreach (var batch in fileIds.Chunk(100)) // Process in batches to avoid excessive query size
        {
            var references = await fileRefService.GetReferencesAsync(batch);
            deletionPlan.AddRange(from refer in references
                where refer.Value.Count == 0 || refer.Value.All(r => r.ExpiredAt != null && now >= r.ExpiredAt)
                select refer.Key);
        }

        if (deletionPlan.Count == 0)
        {
            logger.LogInformation("No files to delete");
            return;
        }

        // Get the actual file objects for the files to be deleted
        var files = await db.Files
            .Where(f => deletionPlan.Contains(f.Id))
            .ToListAsync();

        logger.LogInformation($"Found {files.Count} files to delete...");

        // Group files by StorageId and find which ones are safe to delete
        var storageIds = files.Where(f => f.StorageId != null)
            .Select(f => f.StorageId!)
            .Distinct()
            .ToList();

        // Check if any other files with the same storage IDs are referenced
        var usedStorageIds = new List<string>();
        var filesWithSameStorageId = await db.Files
            .Where(f => f.StorageId != null &&
                        storageIds.Contains(f.StorageId) &&
                        !files.Select(ff => ff.Id).Contains(f.Id))
            .ToListAsync();

        foreach (var file in filesWithSameStorageId)
        {
            // Get all references for the file
            var references = await fileRefService.GetReferencesAsync(file.Id);

            // Check if file has active references (non-expired)
            if (references.Any(r => r.ExpiredAt == null || r.ExpiredAt > now) && file.StorageId != null)
            {
                usedStorageIds.Add(file.StorageId);
            }
        }

        // Group files for deletion
        var filesToDelete = files.Where(f => f.StorageId == null || !usedStorageIds.Contains(f.StorageId))
            .GroupBy(f => f.UploadedTo)
            .ToDictionary(grouping => grouping.Key!, grouping => grouping.ToList());

        // Delete files by remote storage
        foreach (var group in filesToDelete.Where(group => !string.IsNullOrEmpty(group.Key)))
        {
            try
            {
                var dest = fs.GetRemoteStorageConfig(group.Key);
                var client = fs.CreateMinioClient(dest);
                if (client == null) continue;

                // Create delete tasks for each file in the group
                // var deleteTasks = group.Value.Select(file =>
                // {
                //     var objectId = file.StorageId ?? file.Id;
                //     var tasks = new List<Task>
                //     {
                //         client.RemoveObjectAsync(new Minio.DataModel.Args.RemoveObjectArgs()
                //             .WithBucket(dest.Bucket)
                //             .WithObject(objectId))
                //     };
                //
                //     if (file.HasCompression)
                //     {
                //         tasks.Add(client.RemoveObjectAsync(new Minio.DataModel.Args.RemoveObjectArgs()
                //             .WithBucket(dest.Bucket)
                //             .WithObject(objectId + ".compressed")));
                //     }
                //
                //     return Task.WhenAll(tasks);
                // });
                //
                // await Task.WhenAll(deleteTasks);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error deleting files from remote storage {remote}", group.Key);
            }
        }

        // Delete all file records from the database
        var fileIdsToDelete = files.Select(f => f.Id).ToList();
        await db.Files
            .Where(f => fileIdsToDelete.Contains(f.Id))
            .ExecuteDeleteAsync();

        logger.LogInformation($"Completed deleting {files.Count} files");
    }
}