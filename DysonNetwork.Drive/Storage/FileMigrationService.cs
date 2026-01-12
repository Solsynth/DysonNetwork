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
            var ext = Path.GetExtension(cf.Name);
            var mimeType = ext != "" && MimeTypes.TryGetMimeType(ext, out var mime) ? mime : "application/octet-stream";

            var fileObject = await db.FileObjects.FindAsync(cf.Id);

            if (fileObject == null)
            {
                fileObject = new SnFileObject
                {
                    Id = cf.Id,
                    MimeType = mimeType,
                    HasCompression = mimeType.StartsWith("image/"),
                    HasThumbnail = mimeType.StartsWith("video/")
                };

                db.FileObjects.Add(fileObject);
            }

            var replicaExists = await db.FileReplicas.AnyAsync(r =>
                r.ObjectId == fileObject.Id &&
                r.PoolId == cf.PoolId!.Value);

            if (!replicaExists)
            {
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
                db.FileReplicas.Add(fileReplica);
            }

            var permissionExists = await db.FilePermissions.AnyAsync(p => p.FileId == cf.Id);

            if (!permissionExists)
            {
                var permission = new SnFilePermission
                {
                    Id = Guid.NewGuid(),
                    FileId = cf.Id,
                    SubjectType = SnFilePermissionType.Anyone,
                    SubjectId = string.Empty,
                    Permission = SnFilePermissionLevel.Read
                };

                db.FilePermissions.Add(permission);
            }

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
