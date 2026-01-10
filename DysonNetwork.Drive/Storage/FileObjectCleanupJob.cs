using Microsoft.EntityFrameworkCore;
using Minio.DataModel.Args;
using NodaTime;
using Quartz;

namespace DysonNetwork.Drive.Storage;

/// <summary>
/// Job responsible for cleaning up orphaned file objects
/// When no SnCloudFile references a SnFileObject, the file object is considered orphaned
/// and should be deleted from disk and database
/// </summary>
public class FileObjectCleanupJob(AppDatabase db, FileService fileService, ILogger<FileObjectCleanupJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        logger.LogInformation("Running file object cleanup job at {now}", now);

        // Find orphaned file objects (objects with no cloud files referencing them)
        var referencedObjectIds = await db.Files
            .Where(f => f.ObjectId != null)
            .Select(f => f.ObjectId)
            .Distinct()
            .ToListAsync();

        var orphanedObjects = await db.FileObjects
            .Where(fo => !referencedObjectIds.Contains(fo.Id))
            .ToListAsync();

        if (!orphanedObjects.Any())
        {
            logger.LogInformation("No orphaned file objects found");
            return;
        }

        logger.LogInformation("Found {count} orphaned file objects", orphanedObjects.Count);

        // Delete orphaned objects and their data
        foreach (var fileObject in orphanedObjects)
        {
            try
            {
                var replicas = await db.FileReplicas
                    .Where(r => r.ObjectId == fileObject.Id)
                    .ToListAsync();

                foreach (var replica in replicas)
                {
                    var dest = await fileService.GetRemoteStorageConfig(replica.PoolId);
                    if (dest != null)
                    {
                        var client = fileService.CreateMinioClient(dest);
                        if (client != null)
                        {
                            try
                            {
                                await client.RemoveObjectAsync(
                                    new RemoveObjectArgs()
                                        .WithBucket(dest.Bucket)
                                        .WithObject(replica.StorageId)
                                );
                                if (fileObject.HasCompression)
                                {
                                    await client.RemoveObjectAsync(
                                        new RemoveObjectArgs()
                                            .WithBucket(dest.Bucket)
                                            .WithObject(replica.StorageId + ".compressed")
                                    );
                                }
                                if (fileObject.HasThumbnail)
                                {
                                    await client.RemoveObjectAsync(
                                        new RemoveObjectArgs()
                                            .WithBucket(dest.Bucket)
                                            .WithObject(replica.StorageId + ".thumbnail")
                                    );
                                }
                            }
                            catch (Exception ex)
                            {
                                logger.LogError(ex, "Failed to delete orphaned file object {ObjectId} from remote storage", fileObject.Id);
                            }
                        }
                    }
                }

                db.FileReplicas.RemoveRange(replicas);
                db.FileObjects.Remove(fileObject);
                await db.SaveChangesAsync();
                logger.LogInformation("Deleted orphaned file object {ObjectId}", fileObject.Id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to clean up orphaned file object {ObjectId}", fileObject.Id);
            }
        }

        logger.LogInformation("Completed file object cleanup job");
    }
}
