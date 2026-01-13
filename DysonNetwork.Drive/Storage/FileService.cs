using System.Globalization;
using FFMpegCore;
using System.Security.Cryptography;
using DysonNetwork.Drive.Storage.Model;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Proto;
using Google.Protobuf.WellKnownTypes;
using Microsoft.EntityFrameworkCore;
using Minio;
using Minio.DataModel.Args;
using NATS.Client.Core;
using NetVips;
using NodaTime;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using NATS.Net;
using DysonNetwork.Shared.Models;

namespace DysonNetwork.Drive.Storage;

public class FileService(
    AppDatabase db,
    ILogger<FileService> logger,
    ICacheService cache,
    INatsConnection nats
)
{
    private const string CacheKeyPrefix = "file:";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

    public async Task<SnCloudFile?> GetFileAsync(string fileId)
    {
        var cacheKey = string.Concat(CacheKeyPrefix, fileId);

        var cachedFile = await cache.GetAsync<SnCloudFile>(cacheKey);
        if (cachedFile is not null)
            return cachedFile;

        var file = await db.Files
            .Where(f => f.Id == fileId)
            .Include(f => f.Pool)
            .Include(f => f.Bundle)
            .Include(f => f.Object)
            .ThenInclude(o => o.FileReplicas)
            .FirstOrDefaultAsync();

        if (file != null)
            await cache.SetAsync(cacheKey, file, CacheDuration);

        return file;
    }

    public async Task<List<SnCloudFile>> GetFilesAsync(List<string> fileIds)
    {
        var cachedFiles = new Dictionary<string, SnCloudFile>();
        var uncachedIds = new List<string>();

        foreach (var fileId in fileIds)
        {
            var cacheKey = string.Concat(CacheKeyPrefix, fileId);
            var cachedFile = await cache.GetAsync<SnCloudFile>(cacheKey);

            if (cachedFile != null)
                cachedFiles[fileId] = cachedFile;
            else
                uncachedIds.Add(fileId);
        }

        if (uncachedIds.Count > 0)
        {
            var dbFiles = await db.Files
                .Where(f => uncachedIds.Contains(f.Id))
                .Include(f => f.Pool)
                .Include(f => f.Object)
                .ThenInclude(o => o.FileReplicas)
                .ToListAsync();

            foreach (var file in dbFiles)
            {
                var cacheKey = string.Concat(CacheKeyPrefix, file.Id);
                await cache.SetAsync(cacheKey, file, CacheDuration);
                cachedFiles[file.Id] = file;
            }
        }

        return fileIds
            .Select(f => cachedFiles.GetValueOrDefault(f))
            .Where(f => f != null)
            .Cast<SnCloudFile>()
            .ToList();
    }

    public async Task<SnCloudFile> ProcessNewFileAsync(
        Account account,
        string fileId,
        string filePool,
        string? fileBundleId,
        string filePath,
        string fileName,
        string? contentType,
        string? encryptPassword,
        Instant? expiredAt
    )
    {
        var accountId = Guid.Parse(account.Id);
        var pool = await ValidateAndGetPoolAsync(filePool);
        var bundle = await ValidateAndGetBundleAsync(fileBundleId, accountId);
        var finalExpiredAt = CalculateFinalExpiration(expiredAt, pool, bundle);

        var (managedTempPath, fileSize, finalContentType) =
            await PrepareFileAsync(fileId, filePath, fileName, contentType);

        var fileObject = CreateFileObject(fileId, accountId, finalContentType, fileSize);

        var file = CreateCloudFile(fileId, fileName, fileObject, finalExpiredAt, bundle, accountId);

        if (!pool.PolicyConfig.NoMetadata)
        {
            await ExtractMetadataAsync(file, managedTempPath);
        }

        var (processingPath, isTempFile) =
            await ProcessEncryptionAsync(fileId, managedTempPath, encryptPassword, pool, fileObject);

        fileObject.Hash = await HashFileAsync(processingPath);

        await SaveFileToDatabaseAsync(file, fileObject);

        await PublishFileUploadedEventAsync(file, pool, processingPath, isTempFile);

        return file;
    }

    private async Task<FilePool> ValidateAndGetPoolAsync(string filePool)
    {
        var pool = await GetPoolAsync(Guid.Parse(filePool));
        return pool ?? throw new InvalidOperationException("Pool not found: " + filePool);
    }

    private async Task<SnFileBundle?> ValidateAndGetBundleAsync(string? fileBundleId, Guid accountId)
    {
        if (fileBundleId is null) return null;

        var bundle = await GetBundleAsync(Guid.Parse(fileBundleId), accountId);
        return bundle ?? throw new InvalidOperationException("Bundle not found: " + fileBundleId);
    }

    private static Instant? CalculateFinalExpiration(Instant? expiredAt, FilePool pool, SnFileBundle? bundle)
    {
        var finalExpiredAt = expiredAt;

        // Apply pool expiration policy
        if (pool.StorageConfig.Expiration is not null && expiredAt.HasValue)
        {
            var expectedExpiration = SystemClock.Instance.GetCurrentInstant() - expiredAt.Value;
            var effectiveExpiration = pool.StorageConfig.Expiration < expectedExpiration
                ? pool.StorageConfig.Expiration
                : expectedExpiration;
            finalExpiredAt = SystemClock.Instance.GetCurrentInstant() + effectiveExpiration;
        }

        // Bundle expiration takes precedence
        if (bundle?.ExpiredAt != null)
            finalExpiredAt = bundle.ExpiredAt.Value;

        return finalExpiredAt;
    }

    private async Task<(string tempPath, long fileSize, string contentType)> PrepareFileAsync(
        string fileId,
        string filePath,
        string fileName,
        string? contentType
    )
    {
        var managedTempPath = Path.Combine(Path.GetTempPath(), fileId);
        File.Copy(filePath, managedTempPath, true);

        var fileInfo = new FileInfo(managedTempPath);
        var fileSize = fileInfo.Length;
        var finalContentType = contentType ??
                               (!fileName.Contains('.') ? "application/octet-stream" : MimeTypes.GetMimeType(fileName));

        return (managedTempPath, fileSize, finalContentType);
    }

    private SnFileObject CreateFileObject(
        string fileId,
        Guid accountId,
        string contentType,
        long fileSize
    )
    {
        return new SnFileObject
        {
            Id = fileId,
            MimeType = contentType,
            Size = fileSize,
        };
    }

    private SnCloudFile CreateCloudFile(
        string fileId,
        string fileName,
        SnFileObject fileObject,
        Instant? expiredAt,
        SnFileBundle? bundle,
        Guid accountId
    )
    {
        return new SnCloudFile
        {
            Id = fileId,
            Name = fileName,
            Object = fileObject,
            ObjectId = fileId,
            ExpiredAt = expiredAt,
            BundleId = bundle?.Id,
            AccountId = accountId,
        };
    }

    private Task<(string processingPath, bool isTempFile)> ProcessEncryptionAsync(
        string fileId,
        string managedTempPath,
        string? encryptPassword,
        FilePool pool,
        SnFileObject fileObject
    )
    {
        if (string.IsNullOrWhiteSpace(encryptPassword))
            return Task.FromResult((managedTempPath, true));

        if (!pool.PolicyConfig.AllowEncryption)
            throw new InvalidOperationException("Encryption is not allowed in this pool");

        var encryptedPath = Path.Combine(Path.GetTempPath(), $"{fileId}.encrypted");
        FileEncryptor.EncryptFile(managedTempPath, encryptedPath, encryptPassword);

        File.Delete(managedTempPath);

        fileObject.MimeType = "application/octet-stream";
        fileObject.Size = new FileInfo(encryptedPath).Length;

        return Task.FromResult((encryptedPath, true));
    }

    private async Task SaveFileToDatabaseAsync(SnCloudFile file, SnFileObject fileObject)
    {
        var replica = new SnFileReplica
        {
            Id = Guid.NewGuid(),
            ObjectId = file.Id,
            PoolId = file.PoolId,
            StorageId = file.StorageId ?? file.Id,
            Status = SnFileReplicaStatus.Available,
            IsPrimary = true
        };

        var permission = new SnFilePermission
        {
            Id = Guid.NewGuid(),
            FileId = file.Id,
            SubjectType = SnFilePermissionType.Someone,
            SubjectId = file.AccountId.ToString(),
            Permission = SnFilePermissionLevel.Write
        };

        db.Files.Add(file);
        db.FileObjects.Add(fileObject);
        db.FileReplicas.Add(replica);
        db.FilePermissions.Add(permission);

        await db.SaveChangesAsync();
        file.ObjectId = file.Id;
        file.StorageId ??= file.Id;
    }

    private async Task PublishFileUploadedEventAsync(SnCloudFile file, FilePool pool, string processingPath,
        bool isTempFile)
    {
        var js = nats.CreateJetStreamContext();
        await js.PublishAsync(
            FileUploadedEvent.Type,
            GrpcTypeHelper.ConvertObjectToByteString(new FileUploadedEventPayload(
                file.Id,
                pool.Id,
                file.StorageId,
                file.MimeType,
                processingPath,
                isTempFile)
            ).ToByteArray()
        );
    }

    private async Task ExtractMetadataAsync(SnCloudFile file, string filePath)
    {
        if (file.Object == null) return;

        switch (file.MimeType?.Split('/')[0])
        {
            case "image":
                try
                {
                    var blurhash = BlurHashSharp.SkiaSharp.BlurHashEncoder.Encode(3, 3, filePath);
                    await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    stream.Position = 0;

                    using var vipsImage = Image.NewFromStream(stream);
                    var width = vipsImage.Width;
                    var height = vipsImage.Height;
                    var orientation = 1;
                    try
                    {
                        orientation = vipsImage.Get("orientation") as int? ?? 1;
                    }
                    catch
                    {
                        // ignored
                    }

                    var meta = new Dictionary<string, object?>
                    {
                        ["blur"] = blurhash,
                        ["format"] = vipsImage.Get("vips-loader") ?? "unknown",
                        ["width"] = width,
                        ["height"] = height,
                        ["orientation"] = orientation,
                    };
                    var exif = new Dictionary<string, object>();

                    foreach (var field in vipsImage.GetFields())
                    {
                        if (IsIgnoredField(field)) continue;
                        var value = vipsImage.Get(field);
                        if (field.StartsWith("exif-"))
                            exif[field.Replace("exif-", "")] = value;
                        else
                            meta[field] = value;
                    }

                    if (orientation is 6 or 8) (width, height) = (height, width);
                    meta["exif"] = exif;
                    meta["ratio"] = height != 0 ? (double)width / height : 0;
                    file.Object.Meta = meta;
                }
                catch (Exception ex)
                {
                    file.Object.Meta = new Dictionary<string, object?>();
                    logger.LogError(ex, "Failed to analyze image file {FileId}", file.Id);
                }

                break;

            case "video":
            case "audio":
                try
                {
                    var mediaInfo = await FFProbe.AnalyseAsync(filePath);
                    file.Object.Meta = new Dictionary<string, object?>
                    {
                        ["width"] = mediaInfo.PrimaryVideoStream?.Width,
                        ["height"] = mediaInfo.PrimaryVideoStream?.Height,
                        ["duration"] = mediaInfo.Duration.TotalSeconds,
                        ["format_name"] = mediaInfo.Format.FormatName,
                        ["format_long_name"] = mediaInfo.Format.FormatLongName,
                        ["start_time"] = mediaInfo.Format.StartTime.ToString(),
                        ["bit_rate"] = mediaInfo.Format.BitRate.ToString(CultureInfo.InvariantCulture),
                        ["tags"] = mediaInfo.Format.Tags ?? new Dictionary<string, string>(),
                        ["chapters"] = mediaInfo.Chapters,
                        ["video_streams"] = mediaInfo.VideoStreams.Select(s => new
                        {
                            s.AvgFrameRate,
                            s.BitRate,
                            s.CodecName,
                            s.Duration,
                            s.Height,
                            s.Width,
                            s.Language,
                            s.PixelFormat,
                            s.Rotation
                        }).Where(s => double.IsNormal(s.AvgFrameRate)).ToList(),
                        ["audio_streams"] = mediaInfo.AudioStreams.Select(s => new
                            {
                                s.BitRate,
                                s.Channels,
                                s.ChannelLayout,
                                s.CodecName,
                                s.Duration,
                                s.Language,
                                s.SampleRateHz
                            })
                            .ToList(),
                    };
                    if (mediaInfo.PrimaryVideoStream is not null)
                        file.Object.Meta["ratio"] = (double)mediaInfo.PrimaryVideoStream.Width /
                                                      mediaInfo.PrimaryVideoStream.Height;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to analyze media file {FileId}", file.Id);
                }

                break;
        }
    }

    private static async Task<string> HashFileAsync(string filePath, int chunkSize = 1024 * 1024)
    {
        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length > chunkSize * 1024 * 5)
            return await HashFastApproximateAsync(filePath, chunkSize);

        await using var stream = File.OpenRead(filePath);
        using var md5 = MD5.Create();
        var hashBytes = await md5.ComputeHashAsync(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static async Task<string> HashFastApproximateAsync(string filePath, int chunkSize = 1024 * 1024)
    {
        await using var stream = File.OpenRead(filePath);

        var buffer = new byte[chunkSize * 2];
        var fileLength = stream.Length;

        var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, chunkSize));

        if (fileLength > chunkSize)
        {
            stream.Seek(-chunkSize, SeekOrigin.End);
            bytesRead += await stream.ReadAsync(buffer.AsMemory(chunkSize, chunkSize));
        }

        var hash = MD5.HashData(buffer.AsSpan(0, bytesRead));
        stream.Position = 0;
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task UploadFileToRemoteAsync(
        string storageId,
        Guid targetRemote,
        string filePath,
        string? suffix = null,
        string? contentType = null,
        bool selfDestruct = false
    )
    {
        await using var fileStream = File.OpenRead(filePath);
        await UploadFileToRemoteAsync(storageId, targetRemote, fileStream, suffix, contentType);
        if (selfDestruct) File.Delete(filePath);
    }

    private async Task UploadFileToRemoteAsync(
        string storageId,
        Guid targetRemote,
        Stream stream,
        string? suffix = null,
        string? contentType = null
    )
    {
        var dest = await GetRemoteStorageConfig(targetRemote);
        if (dest is null)
            throw new InvalidOperationException(
                $"Failed to configure client for remote destination '{targetRemote}'"
            );
        var client = CreateMinioClient(dest);

        var bucket = dest.Bucket;
        contentType ??= "application/octet-stream";

        await client!.PutObjectAsync(new PutObjectArgs()
            .WithBucket(bucket)
            .WithObject(string.IsNullOrWhiteSpace(suffix) ? storageId : storageId + suffix)
            .WithStreamData(stream)
            .WithObjectSize(stream.Length)
            .WithContentType(contentType)
        );
    }

    public async Task<SnCloudFile> UpdateFileAsync(SnCloudFile file, FieldMask updateMask)
    {
        var existingFile = await db.Files.FirstOrDefaultAsync(f => f.Id == file.Id);
        if (existingFile == null)
        {
            throw new InvalidOperationException($"File with ID {file.Id} not found.");
        }

        var updatable = new UpdatableCloudFile(existingFile);

        foreach (var path in updateMask.Paths)
        {
            switch (path)
            {
                case "name":
                    updatable.Name = file.Name;
                    break;
                case "description":
                    updatable.Description = file.Description;
                    break;
                case "file_meta":
                    updatable.FileMeta = file.FileMeta;
                    break;
                case "user_meta":
                    updatable.UserMeta = file.UserMeta;
                    break;
                case "is_marked_recycle":
                    updatable.IsMarkedRecycle = file.IsMarkedRecycle;
                    break;
                default:
                    logger.LogWarning("Attempted to update unmodifiable field: {Field}", path);
                    break;
            }
        }

        await db.Files.Where(f => f.Id == file.Id).ExecuteUpdateAsync(updatable.ToSetPropertyCalls());

        if (updateMask.Paths.Contains("file_meta"))
        {
            await db.FileObjects
                .Where(fo => fo.Id == file.ObjectId)
                .ExecuteUpdateAsync(setter => setter
                    .SetProperty(fo => fo.Meta, file.FileMeta));
        }

        await _PurgeCacheAsync(file.Id);
        return await db.Files
            .AsNoTracking()
            .Include(f => f.Object)
            .ThenInclude(o => o.FileReplicas)
            .FirstAsync(f => f.Id == file.Id);
    }

    public async Task DeleteFileAsync(SnCloudFile file, bool skipData = false)
    {
        db.Remove(file);
        await db.SaveChangesAsync();
        await _PurgeCacheAsync(file.Id);

        if (!skipData)
        {
            var hasOtherReferences = await db.Files
                .AnyAsync(f => f.ObjectId == file.ObjectId && f.Id != file.Id);

            if (!hasOtherReferences)
                await DeleteFileDataAsync(file);
        }
    }

    public async Task DeleteFileDataAsync(SnCloudFile file, bool force = false)
    {
        if (!file.PoolId.HasValue || file.ObjectId == null) return;

        if (!force)
        {
            var sameOriginFiles = await db.Files
                .Where(f => f.ObjectId == file.ObjectId && f.Id != file.Id)
                .Select(f => f.Id)
                .ToListAsync();

            if (sameOriginFiles.Count != 0)
                return;
        }

        var replicas = await db.FileReplicas
            .Where(r => r.ObjectId == file.ObjectId)
            .ToListAsync();

        if (replicas.Count == 0)
        {
            logger.LogWarning("No replicas found for file object {ObjectId}", file.ObjectId);
            return;
        }

        var primaryReplica = replicas.First(r => r.IsPrimary);
        var dest = await GetRemoteStorageConfig(file.PoolId.Value);
        if (dest is null) throw new InvalidOperationException($"No remote storage configured for pool {file.PoolId}");
        var client = CreateMinioClient(dest);
        if (client is null)
            throw new InvalidOperationException(
                $"Failed to configure client for remote destination '{file.PoolId}'"
            );

        var bucket = dest.Bucket;
        var objectId = primaryReplica.StorageId;

        await client.RemoveObjectAsync(
            new RemoveObjectArgs().WithBucket(bucket).WithObject(objectId)
        );

        if (file.HasCompression)
        {
            try
            {
                await client.RemoveObjectAsync(
                    new RemoveObjectArgs().WithBucket(bucket).WithObject(objectId + ".compressed")
                );
            }
            catch
            {
                logger.LogWarning("Failed to delete compressed version of file {fileId}", file.Id);
            }
        }

        if (file.HasThumbnail)
        {
            try
            {
                await client.RemoveObjectAsync(
                    new RemoveObjectArgs().WithBucket(bucket).WithObject(objectId + ".thumbnail")
                );
            }
            catch
            {
                logger.LogWarning("Failed to delete thumbnail of file {fileId}", file.Id);
            }
        }

        db.FileReplicas.RemoveRange(replicas);
        var fileObject = await db.FileObjects.FindAsync(file.ObjectId);
        if (fileObject != null) db.FileObjects.Remove(fileObject);
        await db.SaveChangesAsync();
    }

    public async Task DeleteFileDataBatchAsync(List<SnCloudFile> files)
    {
        files = files.Where(f => f.PoolId.HasValue && f.ObjectId != null).ToList();

        var objectIds = files.Select(f => f.ObjectId).Distinct().ToList();
        var replicas = await db.FileReplicas
            .Where(r => objectIds.Contains(r.ObjectId))
            .ToListAsync();

        foreach (var poolGroup in replicas.Where(r => r.PoolId.HasValue).GroupBy(r => r.PoolId!.Value))
        {
            var dest = await GetRemoteStorageConfig(poolGroup.Key);
            if (dest is null)
                throw new InvalidOperationException($"No remote storage configured for pool {poolGroup.Key}");
            var client = CreateMinioClient(dest);
            if (client is null)
                throw new InvalidOperationException(
                    $"Failed to configure client for remote destination '{poolGroup.Key}'"
                );

            List<string> objectsToDelete = [];

            foreach (var replica in poolGroup)
            {
                var file = files.First(f => f.ObjectId == replica.ObjectId);
                objectsToDelete.Add(replica.StorageId);
                if (file.HasCompression) objectsToDelete.Add(replica.StorageId + ".compressed");
                if (file.HasThumbnail) objectsToDelete.Add(replica.StorageId + ".thumbnail");
            }

            await client.RemoveObjectsAsync(
                new RemoveObjectsArgs().WithBucket(dest.Bucket).WithObjects(objectsToDelete)
            );

            db.FileReplicas.RemoveRange(poolGroup);
        }

        var fileObjects = await db.FileObjects
            .Where(fo => objectIds.Contains(fo.Id))
            .ToListAsync();
        db.FileObjects.RemoveRange(fileObjects);
        await db.SaveChangesAsync();
    }

    private async Task<SnFileBundle?> GetBundleAsync(Guid id, Guid accountId)
    {
        var bundle = await db.Bundles
            .Where(e => e.Id == id)
            .Where(e => e.AccountId == accountId)
            .FirstOrDefaultAsync();
        return bundle;
    }

    public async Task<FilePool?> GetPoolAsync(Guid destination)
    {
        var cacheKey = $"file:pool:{destination}";
        var cachedResult = await cache.GetAsync<FilePool?>(cacheKey);
        if (cachedResult != null) return cachedResult;

        var pool = await db.Pools.FirstOrDefaultAsync(p => p.Id == destination);
        if (pool != null)
            await cache.SetAsync(cacheKey, pool);

        return pool;
    }

    public async Task<RemoteStorageConfig?> GetRemoteStorageConfig(Guid destination)
    {
        var pool = await GetPoolAsync(destination);
        return pool?.StorageConfig;
    }

    public async Task<RemoteStorageConfig?> GetRemoteStorageConfig(string destination)
    {
        var id = Guid.Parse(destination);
        return await GetRemoteStorageConfig(id);
    }

    public IMinioClient? CreateMinioClient(RemoteStorageConfig dest)
    {
        var client = new MinioClient()
            .WithEndpoint(dest.Endpoint)
            .WithRegion(dest.Region)
            .WithCredentials(dest.SecretId, dest.SecretKey);
        if (dest.EnableSsl) client = client.WithSSL();

        return client.Build();
    }

    internal async Task _PurgeCacheAsync(string fileId)
    {
        var cacheKey = string.Concat(CacheKeyPrefix, fileId);
        await cache.RemoveAsync(cacheKey);
    }

    internal async Task _PurgeCacheRangeAsync(IEnumerable<string> fileIds)
    {
        var tasks = fileIds.Select(_PurgeCacheAsync);
        await Task.WhenAll(tasks);
    }

    public async Task<List<SnCloudFile?>> LoadFromReference(List<SnCloudFileReferenceObject> references)
    {
        var cachedFiles = new Dictionary<string, SnCloudFile>();
        var uncachedIds = new List<string>();

        foreach (var reference in references)
        {
            var cacheKey = string.Concat(CacheKeyPrefix, reference.Id);
            var cachedFile = await cache.GetAsync<SnCloudFile>(cacheKey);

            if (cachedFile != null)
            {
                cachedFiles[reference.Id] = cachedFile;
            }
            else
            {
                uncachedIds.Add(reference.Id);
            }
        }

        if (uncachedIds.Count > 0)
        {
            var dbFiles = await db.Files
                .Where(f => uncachedIds.Contains(f.Id))
                .Include(f => f.Object)
                .ThenInclude(o => o.FileReplicas)
                .ToListAsync();

            foreach (var file in dbFiles)
            {
                var cacheKey = string.Concat(CacheKeyPrefix, file.Id);
                await cache.SetAsync(cacheKey, file, CacheDuration);
                cachedFiles[file.Id] = file;
            }
        }

        return
        [
            .. references
                .Select(r => cachedFiles.GetValueOrDefault(r.Id))
                .Where(f => f != null)
        ];
    }

    public async Task<int> GetReferenceCountAsync(string fileId)
    {
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == fileId);
        if (file == null || file.ObjectId == null) return 0;

        return await db.Files
            .Where(f => f.ObjectId == file.ObjectId && f.Id != fileId)
            .CountAsync();
    }

    public async Task<bool> IsReferencedAsync(string fileId)
    {
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == fileId);
        if (file == null || file.ObjectId == null) return false;

        return await db.Files
            .Where(f => f.ObjectId == file.ObjectId && f.Id != fileId)
            .AnyAsync();
    }

    private static bool IsIgnoredField(string fieldName)
    {
        var gpsFields = new[]
        {
            "gps-latitude", "gps-longitude", "gps-altitude", "gps-latitude-ref", "gps-longitude-ref",
            "gps-altitude-ref", "gps-timestamp", "gps-datestamp", "gps-speed", "gps-speed-ref", "gps-track",
            "gps-track-ref", "gps-img-direction", "gps-img-direction-ref", "gps-dest-latitude",
            "gps-dest-longitude", "gps-dest-latitude-ref", "gps-dest-longitude-ref", "gps-processing-method",
            "gps-area-information"
        };

        if (fieldName.StartsWith("exif-GPS")) return true;
        if (fieldName.StartsWith("ifd3-GPS")) return true;
        if (fieldName.EndsWith("-data")) return true;
        return gpsFields.Any(gpsField => fieldName.StartsWith(gpsField, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<int> DeleteAccountRecycledFilesAsync(Guid accountId)
    {
        var files = await db.Files
            .Where(f => f.AccountId == accountId && f.IsMarkedRecycle)
            .ToListAsync();
        var count = files.Count;
        var fileIds = files.Select(f => f.Id).ToList();
        await _PurgeCacheRangeAsync(fileIds);
        db.RemoveRange(files);
        await db.SaveChangesAsync();
        return count;
    }

    public async Task<int> DeleteAccountFileBatchAsync(Guid accountId, List<string> fileIds)
    {
        var files = await db.Files
            .Where(f => f.AccountId == accountId && fileIds.Contains(f.Id))
            .ToListAsync();
        var count = files.Count;
        var fileIdsList = files.Select(f => f.Id).ToList();
        await _PurgeCacheRangeAsync(fileIdsList);
        db.RemoveRange(files);
        await db.SaveChangesAsync();
        return count;
    }

    public async Task<int> DeletePoolRecycledFilesAsync(Guid poolId)
    {
        var files = await db.Files
            .Where(f => f.PoolId == poolId && f.IsMarkedRecycle)
            .ToListAsync();
        var count = files.Count;
        var fileIds = files.Select(f => f.Id).ToList();
        await _PurgeCacheRangeAsync(fileIds);
        db.RemoveRange(files);
        await db.SaveChangesAsync();
        return count;
    }

    public async Task<int> DeleteAllRecycledFilesAsync()
    {
        var files = await db.Files
            .Where(f => f.IsMarkedRecycle)
            .ToListAsync();
        var count = files.Count;
        var fileIds = files.Select(f => f.Id).ToList();
        await _PurgeCacheRangeAsync(fileIds);
        db.RemoveRange(files);
        await db.SaveChangesAsync();
        return count;
    }

    public async Task SetPublicAsync(string fileId)
    {
        var existingPermission = await db.FilePermissions
            .FirstOrDefaultAsync(p =>
                p.FileId == fileId &&
                p.SubjectType == SnFilePermissionType.Anyone &&
                p.Permission == SnFilePermissionLevel.Read);

        if (existingPermission != null)
            return;

        var permission = new SnFilePermission
        {
            Id = Guid.NewGuid(),
            FileId = fileId,
            SubjectType = SnFilePermissionType.Anyone,
            SubjectId = string.Empty,
            Permission = SnFilePermissionLevel.Read
        };

        db.FilePermissions.Add(permission);
        await db.SaveChangesAsync();
    }

    public async Task UnsetPublicAsync(string fileId)
    {
        var permission = await db.FilePermissions
            .FirstOrDefaultAsync(p =>
                p.FileId == fileId &&
                p.SubjectType == SnFilePermissionType.Anyone &&
                p.Permission == SnFilePermissionLevel.Read);

        if (permission == null)
            return;

        db.FilePermissions.Remove(permission);
        await db.SaveChangesAsync();
    }
}

file class UpdatableCloudFile(SnCloudFile file)
{
    public string Name { get; set; } = file.Name;
    public string? Description { get; set; } = file.Description;
    public Dictionary<string, object?>? FileMeta { get; set; } = file.FileMeta;
    public Dictionary<string, object?>? UserMeta { get; set; } = file.UserMeta;
    public bool IsMarkedRecycle { get; set; } = file.IsMarkedRecycle;

    public Action<UpdateSettersBuilder<SnCloudFile>> ToSetPropertyCalls()
    {
        var userMeta = UserMeta ?? [];
        return setter => setter
            .SetProperty(f => f.Name, Name)
            .SetProperty(f => f.Description, Description)
            .SetProperty(f => f.UserMeta, userMeta)
            .SetProperty(f => f.IsMarkedRecycle, IsMarkedRecycle);
    }
}
