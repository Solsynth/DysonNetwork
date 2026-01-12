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
                f.PoolId != null
            )
            .ToListAsync();

        logger.LogDebug("Found {Count} cloud files to migrate.", cloudFiles.Count);

        foreach (var cf in cloudFiles)
        {
            if (await db.FileObjects.AnyAsync(fo => fo.Id == cf.Id))
            {
                logger.LogWarning("FileObject for {Id} already exists, skipping.", cf.Id);
                continue;
            }

            var ext = Path.GetExtension(cf.Name);
            var mimeType = ext != "" && MimeTypes.TryGetMimeType(ext, out var mime) ? mime : "application/octet-stream";

            var fileObject = new SnFileObject
            {
                Id = cf.Id,
                MimeType = mimeType,
                HasCompression = mimeType.StartsWith("image/"),
                HasThumbnail = mimeType.StartsWith("video/")
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

            var permission = new SnFilePermission
            {
                Id = Guid.NewGuid(),
                FileId = cf.Id,
                SubjectType = SnFilePermissionType.Anyone,
                SubjectId = string.Empty,
                Permission = SnFilePermissionLevel.Read
            };

            fileObject.FileReplicas.Add(fileReplica);

            db.FileObjects.Add(fileObject);
            db.FileReplicas.Add(fileReplica);
            db.FilePermissions.Add(permission);

            cf.ObjectId = fileObject.Id;
            cf.Object = fileObject;
        }

        await db.SaveChangesAsync();
        
        logger.LogInformation("Cloud file migration completed.");
    }

    public async Task MigratePermissionsAsync()
    {
        logger.LogInformation("Starting file permission migration.");

        var filesWithoutPermission = await db.Files
            .Where(f => !db.FilePermissions.Any(p => p.FileId == f.Id))
            .ToListAsync();

        logger.LogDebug("Found {Count} files without permissions.", filesWithoutPermission.Count);

        foreach (var file in filesWithoutPermission)
        {
            var permission = new SnFilePermission
            {
                Id = Guid.NewGuid(),
                FileId = file.Id,
                SubjectType = SnFilePermissionType.Anyone,
                SubjectId = string.Empty,
                Permission = SnFilePermissionLevel.Read
            };

            db.FilePermissions.Add(permission);
        }

        await db.SaveChangesAsync();
        
        logger.LogInformation("Permission migration completed. Created {Count} permissions.", filesWithoutPermission.Count);
    }
}
