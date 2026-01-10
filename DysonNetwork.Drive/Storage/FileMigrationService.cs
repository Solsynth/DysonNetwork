using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Drive.Storage;

public class FileMigrationService(AppDatabase db, ILogger<FileMigrationService> logger)
{
    public async Task MigrateCloudFilesAsync()
    {
        logger.LogInformation("Starting cloud file migration.");
        
        var cloudFiles = await db.Files
            .Where(f =>
                f.ObjectId == null &&
                f.StorageId != null &&
                f.PoolId != null &&
                !db.FileObjects.Any(fo => fo.Id == f.Id)
            )
            .ToListAsync();

        logger.LogDebug("Found {Count} cloud files to migrate.", cloudFiles.Count);

        foreach (var cf in cloudFiles)
        {
            var fileObject = new SnFileObject
            {
                Id = cf.Id,
                Size = cf.Size,
                Meta = cf.FileMeta,
                MimeType = cf.MimeType,
                Hash = cf.Hash,
                HasCompression = cf.HasCompression,
                HasThumbnail = cf.HasThumbnail
            };

            var fileReplica = new SnFileReplica
            {
                Id = Guid.NewGuid(),
                ObjectId = fileObject.Id,
                PoolId = cf.PoolId!.Value,
                StorageId = cf.StorageId ?? cf.Id,
                Status = SnFileReplicaStatus.Available,
                IsPrimary = true
            };

            fileObject.FileReplicas.Add(fileReplica);

            db.FileObjects.Add(fileObject);
            db.FileReplicas.Add(fileReplica);

            cf.ObjectId = fileObject.Id;
            cf.Object = fileObject;
        }

        await db.SaveChangesAsync();
        
        logger.LogInformation("Cloud file migration completed.");
    }
}
