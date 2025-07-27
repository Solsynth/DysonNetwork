using System.Globalization;
using FFMpegCore;
using System.Security.Cryptography;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Proto;
using Google.Protobuf.WellKnownTypes;
using Microsoft.EntityFrameworkCore;
using Minio;
using Minio.DataModel.Args;
using NetVips;
using NodaTime;
using tusdotnet.Stores;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace DysonNetwork.Drive.Storage;

public class FileService(
    AppDatabase db,
    IConfiguration configuration,
    TusDiskStore store,
    ILogger<FileService> logger,
    IServiceScopeFactory scopeFactory,
    ICacheService cache
)
{
    private const string CacheKeyPrefix = "file:";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The api for getting file meta with cache,
    /// the best use case is for accessing the file data.
    ///
    /// <b>This function won't load uploader's information, only keep minimal file meta</b>
    /// </summary>
    /// <param name="fileId">The id of the cloud file requested</param>
    /// <returns>The minimal file meta</returns>
    public async Task<CloudFile?> GetFileAsync(string fileId)
    {
        var cacheKey = $"{CacheKeyPrefix}{fileId}";

        var cachedFile = await cache.GetAsync<CloudFile>(cacheKey);
        if (cachedFile is not null)
            return cachedFile;

        var file = await db.Files
            .Where(f => f.Id == fileId)
            .Include(f => f.Pool)
            .FirstOrDefaultAsync();

        if (file != null)
            await cache.SetAsync(cacheKey, file, CacheDuration);

        return file;
    }

    public async Task<List<CloudFile>> GetFilesAsync(List<string> fileIds)
    {
        var cachedFiles = new Dictionary<string, CloudFile>();
        var uncachedIds = new List<string>();

        // Check cache first
        foreach (var fileId in fileIds)
        {
            var cacheKey = $"{CacheKeyPrefix}{fileId}";
            var cachedFile = await cache.GetAsync<CloudFile>(cacheKey);

            if (cachedFile != null)
                cachedFiles[fileId] = cachedFile;
            else
                uncachedIds.Add(fileId);
        }

        // Load uncached files from database
        if (uncachedIds.Count > 0)
        {
            var dbFiles = await db.Files
                .Where(f => uncachedIds.Contains(f.Id))
                .Include(f => f.Pool)
                .ToListAsync();

            // Add to cache
            foreach (var file in dbFiles)
            {
                var cacheKey = $"{CacheKeyPrefix}{file.Id}";
                await cache.SetAsync(cacheKey, file, CacheDuration);
                cachedFiles[file.Id] = file;
            }
        }

        // Preserve original order
        return fileIds
            .Select(f => cachedFiles.GetValueOrDefault(f))
            .Where(f => f != null)
            .Cast<CloudFile>()
            .ToList();
    }

    private const string TempFilePrefix = "dyn-cloudfile";

    private static readonly string[] AnimatedImageTypes =
        ["image/gif", "image/apng", "image/webp", "image/avif"];

    public async Task<CloudFile> ProcessNewFileAsync(
        Account account,
        string fileId,
        string filePool,
        Stream stream,
        string fileName,
        string? contentType,
        string? encryptPassword,
        Instant? expiredAt
    )
    {
        var pool = await GetPoolAsync(Guid.Parse(filePool));
        if (pool is null) throw new InvalidOperationException("Pool not found");

        if (pool.StorageConfig.Expiration is not null && expiredAt.HasValue)
        {
            var expectedExpiration = SystemClock.Instance.GetCurrentInstant() - expiredAt.Value;
            var effectiveExpiration = pool.StorageConfig.Expiration < expectedExpiration
                ? pool.StorageConfig.Expiration
                : expectedExpiration;
            expiredAt = SystemClock.Instance.GetCurrentInstant() + effectiveExpiration;
        }

        var ogFilePath = Path.GetFullPath(Path.Join(configuration.GetValue<string>("Tus:StorePath"), fileId));
        var fileSize = stream.Length;
        contentType ??= !fileName.Contains('.') ? "application/octet-stream" : MimeTypes.GetMimeType(fileName);

        if (!string.IsNullOrWhiteSpace(encryptPassword))
        {
            if (!pool.PolicyConfig.AllowEncryption)
                throw new InvalidOperationException("Encryption is not allowed in this pool");
            var encryptedPath = Path.Combine(Path.GetTempPath(), $"{fileId}.encrypted");
            FileEncryptor.EncryptFile(ogFilePath, encryptedPath, encryptPassword);
            File.Delete(ogFilePath); // Delete original unencrypted
            File.Move(encryptedPath, ogFilePath); // Replace the original one with encrypted
            contentType = "application/octet-stream";
        }

        var hash = await HashFileAsync(ogFilePath);

        var file = new CloudFile
        {
            Id = fileId,
            Name = fileName,
            MimeType = contentType,
            Size = fileSize,
            Hash = hash,
            ExpiredAt = expiredAt,
            AccountId = Guid.Parse(account.Id),
            IsEncrypted = !string.IsNullOrWhiteSpace(encryptPassword) && pool.PolicyConfig.AllowEncryption
        };

        // TODO: Enable the feature later
        // var existingFile = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Hash == hash);
        // file.StorageId = existingFile?.StorageId ?? file.Id;
        //
        // if (existingFile is not null)
        // {
        //     file.FileMeta = existingFile.FileMeta;
        //     file.HasCompression = existingFile.HasCompression;
        //     file.SensitiveMarks = existingFile.SensitiveMarks;
        //     file.MimeType = existingFile.MimeType;
        //     file.UploadedAt = existingFile.UploadedAt;
        //     file.PoolId = existingFile.PoolId;
        //
        //     db.Files.Add(file);
        //     await db.SaveChangesAsync();
        //     // Since the file content is a duplicate, we can delete the new upload and we are done.
        //     await stream.DisposeAsync();
        //     await store.DeleteFileAsync(file.Id, CancellationToken.None);
        //     return file;
        // }

        // Extract metadata on the current thread for a faster initial response
        if (!pool.PolicyConfig.NoMetadata)
            await ExtractMetadataAsync(file, ogFilePath, stream);

        db.Files.Add(file);
        await db.SaveChangesAsync();
        
        file.StorageId ??= file.Id;

        // Offload optimization (image conversion, thumbnailing) and uploading to a background task
        _ = Task.Run(() =>
            ProcessAndUploadInBackgroundAsync(file.Id, filePool, file.StorageId, contentType, ogFilePath, stream));

        return file;
    }

    /// <summary>
    /// Extracts metadata from the file based on its content type.
    /// This runs synchronously to ensure the initial database record has basic metadata.
    /// </summary>
    private async Task ExtractMetadataAsync(CloudFile file, string filePath, Stream stream)
    {
        switch (file.MimeType?.Split('/')[0])
        {
            case "image":
                try
                {
                    var blurhash = BlurHashSharp.SkiaSharp.BlurHashEncoder.Encode(3, 3, filePath);
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
                    file.FileMeta = meta;
                }
                catch (Exception ex)
                {
                    file.FileMeta = new Dictionary<string, object?>();
                    logger.LogError(ex, "Failed to analyze image file {FileId}", file.Id);
                }

                break;

            case "video":
            case "audio":
                try
                {
                    var mediaInfo = await FFProbe.AnalyseAsync(filePath);
                    file.FileMeta = new Dictionary<string, object?>
                    {
                        ["duration"] = mediaInfo.Duration.TotalSeconds,
                        ["format_name"] = mediaInfo.Format.FormatName,
                        ["format_long_name"] = mediaInfo.Format.FormatLongName,
                        ["start_time"] = mediaInfo.Format.StartTime.ToString(),
                        ["bit_rate"] = mediaInfo.Format.BitRate.ToString(CultureInfo.InvariantCulture),
                        ["tags"] = mediaInfo.Format.Tags ?? new Dictionary<string, string>(),
                        ["chapters"] = mediaInfo.Chapters,
                        // Add detailed stream information
                        ["video_streams"] = mediaInfo.VideoStreams.Select(s => new
                        {
                            s.AvgFrameRate, s.BitRate, s.CodecName, s.Duration, s.Height, s.Width, s.Language,
                            s.PixelFormat, s.Rotation
                        }).ToList(),
                        ["audio_streams"] = mediaInfo.AudioStreams.Select(s => new
                            {
                                s.BitRate, s.Channels, s.ChannelLayout, s.CodecName, s.Duration, s.Language,
                                s.SampleRateHz
                            })
                            .ToList(),
                    };
                    if (mediaInfo.PrimaryVideoStream is not null)
                        file.FileMeta["ratio"] = (double)mediaInfo.PrimaryVideoStream.Width /
                                                 mediaInfo.PrimaryVideoStream.Height;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to analyze media file {FileId}", file.Id);
                }

                break;
        }
    }

    /// <summary>
    /// Handles file optimization (image compression, video thumbnail) and uploads to remote storage in the background.
    /// </summary>
    private async Task ProcessAndUploadInBackgroundAsync(
        string fileId,
        string remoteId,
        string storageId,
        string contentType,
        string originalFilePath,
        Stream stream
    )
    {
        var pool = await GetPoolAsync(Guid.Parse(remoteId));
        if (pool is null) return;

        await using var bgStream = stream; // Ensure stream is disposed at the end of this task
        using var scope = scopeFactory.CreateScope();
        var nfs = scope.ServiceProvider.GetRequiredService<FileService>();
        var scopedDb = scope.ServiceProvider.GetRequiredService<AppDatabase>();

        var uploads = new List<(string FilePath, string Suffix, string ContentType, bool SelfDestruct)>();
        var newMimeType = contentType;
        var hasCompression = false;
        var hasThumbnail = false;

        try
        {
            logger.LogInformation("Processing file {FileId} in background...", fileId);

            if (!pool.PolicyConfig.NoOptimization)
                switch (contentType.Split('/')[0])
                {
                    case "image" when !AnimatedImageTypes.Contains(contentType):
                        newMimeType = "image/webp";
                        using (var vipsImage = Image.NewFromFile(originalFilePath))
                        {
                            var imageToWrite = vipsImage;

                            if (vipsImage.Interpretation is Enums.Interpretation.Scrgb or Enums.Interpretation.Xyz)
                            {
                                imageToWrite = vipsImage.Colourspace(Enums.Interpretation.Srgb);
                            }

                            var webpPath = Path.Join(Path.GetTempPath(), $"{TempFilePrefix}#{fileId}.webp");
                            imageToWrite.Autorot().WriteToFile(webpPath,
                                new VOption { { "lossless", true }, { "strip", true } });
                            uploads.Add((webpPath, string.Empty, newMimeType, true));

                            if (imageToWrite.Width * imageToWrite.Height >= 1024 * 1024)
                            {
                                var scale = 1024.0 / Math.Max(imageToWrite.Width, imageToWrite.Height);
                                var compressedPath =
                                    Path.Join(Path.GetTempPath(), $"{TempFilePrefix}#{fileId}-compressed.webp");
                                using var compressedImage = imageToWrite.Resize(scale);
                                compressedImage.Autorot().WriteToFile(compressedPath,
                                    new VOption { { "Q", 80 }, { "strip", true } });
                                uploads.Add((compressedPath, ".compressed", newMimeType, true));
                                hasCompression = true;
                            }

                            if (!ReferenceEquals(imageToWrite, vipsImage))
                            {
                                imageToWrite.Dispose(); // Clean up manually created colourspace-converted image
                            }
                        }

                        break;

                    case "video":
                        uploads.Add((originalFilePath, string.Empty, contentType, false));
                        var thumbnailPath = Path.Join(Path.GetTempPath(), $"{TempFilePrefix}#{fileId}.thumbnail.webp");
                        try
                        {
                            var mediaInfo = await FFProbe.AnalyseAsync(originalFilePath);
                            var snapshotTime = mediaInfo.Duration > TimeSpan.FromSeconds(5)
                                ? TimeSpan.FromSeconds(5)
                                : TimeSpan.FromSeconds(1);

                            await FFMpeg.SnapshotAsync(originalFilePath, thumbnailPath, captureTime: snapshotTime);

                            if (File.Exists(thumbnailPath))
                            {
                                uploads.Add((thumbnailPath, ".thumbnail", "image/webp", true));
                                hasThumbnail = true;
                            }
                            else
                            {
                                logger.LogWarning("FFMpeg did not produce thumbnail for video {FileId}", fileId);
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Failed to generate thumbnail for video {FileId}", fileId);
                        }

                        break;

                    default:
                        uploads.Add((originalFilePath, string.Empty, contentType, false));
                        break;
                }
            else uploads.Add((originalFilePath, string.Empty, contentType, false));

            logger.LogInformation("Optimized file {FileId}, now uploading...", fileId);

            if (uploads.Count > 0)
            {
                var destPool = Guid.Parse(remoteId!);
                var uploadTasks = uploads.Select(item =>
                    nfs.UploadFileToRemoteAsync(
                        storageId,
                        destPool,
                        item.FilePath,
                        item.Suffix,
                        item.ContentType,
                        item.SelfDestruct
                    )
                ).ToList();

                await Task.WhenAll(uploadTasks);

                logger.LogInformation("Uploaded file {FileId} done!", fileId);

                var fileToUpdate = await scopedDb.Files.FirstAsync(f => f.Id == fileId);
                if (hasThumbnail) fileToUpdate.HasThumbnail = true;

                var now = SystemClock.Instance.GetCurrentInstant();
                await scopedDb.Files.Where(f => f.Id == fileId).ExecuteUpdateAsync(setter => setter
                    .SetProperty(f => f.UploadedAt, now)
                    .SetProperty(f => f.PoolId, destPool)
                    .SetProperty(f => f.MimeType, newMimeType)
                    .SetProperty(f => f.HasCompression, hasCompression)
                    .SetProperty(f => f.HasThumbnail, hasThumbnail)
                );
            }
        }
        catch (Exception err)
        {
            logger.LogError(err, "Failed to process and upload {FileId}", fileId);
        }
        finally
        {
            await store.DeleteFileAsync(fileId, CancellationToken.None);
            await nfs._PurgeCacheAsync(fileId);
        }
    }

    private static async Task<string> HashFileAsync(string filePath, int chunkSize = 1024 * 1024)
    {
        using var stream = File.OpenRead(filePath);
        var fileSize = stream.Length;
        if (fileSize > chunkSize * 1024 * 5)
            return await HashFastApproximateAsync(filePath, chunkSize);

        using var md5 = MD5.Create();
        var hashBytes = await md5.ComputeHashAsync(stream);
        stream.Position = 0; // Reset stream position after reading
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static async Task<string> HashFastApproximateAsync(string filePath, int chunkSize = 1024 * 1024)
    {
        await using var stream = File.OpenRead(filePath);

        // Scale the chunk size to kB level
        chunkSize *= 1024;

        using var md5 = MD5.Create();

        var buffer = new byte[chunkSize * 2];
        var fileLength = stream.Length;

        var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, chunkSize));

        if (fileLength > chunkSize)
        {
            stream.Seek(-chunkSize, SeekOrigin.End);
            bytesRead += await stream.ReadAsync(buffer.AsMemory(chunkSize, chunkSize));
        }

        var hash = md5.ComputeHash(buffer, 0, bytesRead);
        stream.Position = 0; // Reset stream position
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task UploadFileToRemoteAsync(string storageId, Guid targetRemote, string filePath,
        string? suffix = null, string? contentType = null, bool selfDestruct = false)
    {
        await using var fileStream = File.OpenRead(filePath);
        await UploadFileToRemoteAsync(storageId, targetRemote, fileStream, suffix, contentType);
        if (selfDestruct) File.Delete(filePath);
    }

    public async Task UploadFileToRemoteAsync(string storageId, Guid targetRemote, Stream stream,
        string? suffix = null, string? contentType = null)
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

    public async Task<CloudFile> UpdateFileAsync(CloudFile file, FieldMask updateMask)
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

        await _PurgeCacheAsync(file.Id);
        // Re-fetch the file to return the updated state
        return await db.Files.AsNoTracking().FirstAsync(f => f.Id == file.Id);
    }

    public async Task DeleteFileAsync(CloudFile file)
    {
        db.Remove(file);
        await db.SaveChangesAsync();
        await _PurgeCacheAsync(file.Id);

        await DeleteFileDataAsync(file);
    }

    private async Task DeleteFileDataAsync(CloudFile file, bool force = false)
    {
        if (file.StorageId is null) return;
        if (!file.PoolId.HasValue) return;

        if (!force)
        {
            // Check if any other file with the same storage ID is referenced
            var sameOriginFiles = await db.Files
                .Where(f => f.StorageId == file.StorageId && f.Id != file.Id)
                .Select(f => f.Id)
                .ToListAsync();

            // Check if any of these files are referenced
            if (sameOriginFiles.Count != 0)
                return;
        }

        // If any other file with the same storage ID is referenced, don't delete the actual file data
        var dest = await GetRemoteStorageConfig(file.PoolId.Value);
        if (dest is null) throw new InvalidOperationException($"No remote storage configured for pool {file.PoolId}");
        var client = CreateMinioClient(dest);
        if (client is null)
            throw new InvalidOperationException(
                $"Failed to configure client for remote destination '{file.PoolId}'"
            );

        var bucket = dest.Bucket;
        var objectId = file.StorageId ?? file.Id; // Use StorageId if available, otherwise fall back to Id

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
                // Ignore errors when deleting compressed version
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
                // Ignore errors when deleting thumbnail
                logger.LogWarning("Failed to delete thumbnail of file {fileId}", file.Id);
            }
        }
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

    // Helper method to purge the cache for a specific file
    // Made internal to allow FileReferenceService to use it
    internal async Task _PurgeCacheAsync(string fileId)
    {
        var cacheKey = $"{CacheKeyPrefix}{fileId}";
        await cache.RemoveAsync(cacheKey);
    }

    // Helper method to purge cache for multiple files
    internal async Task _PurgeCacheRangeAsync(IEnumerable<string> fileIds)
    {
        var tasks = fileIds.Select(_PurgeCacheAsync);
        await Task.WhenAll(tasks);
    }

    public async Task<List<CloudFile?>> LoadFromReference(List<CloudFileReferenceObject> references)
    {
        var cachedFiles = new Dictionary<string, CloudFile>();
        var uncachedIds = new List<string>();

        // Check cache first
        foreach (var reference in references)
        {
            var cacheKey = $"{CacheKeyPrefix}{reference.Id}";
            var cachedFile = await cache.GetAsync<CloudFile>(cacheKey);

            if (cachedFile != null)
            {
                cachedFiles[reference.Id] = cachedFile;
            }
            else
            {
                uncachedIds.Add(reference.Id);
            }
        }

        // Load uncached files from database
        if (uncachedIds.Count > 0)
        {
            var dbFiles = await db.Files
                .Where(f => uncachedIds.Contains(f.Id))
                .ToListAsync();

            // Add to cache
            foreach (var file in dbFiles)
            {
                var cacheKey = $"{CacheKeyPrefix}{file.Id}";
                await cache.SetAsync(cacheKey, file, CacheDuration);
                cachedFiles[file.Id] = file;
            }
        }

        // Preserve original order
        return references
            .Select(r => cachedFiles.GetValueOrDefault(r.Id))
            .Where(f => f != null)
            .ToList();
    }

    /// <summary>
    /// Gets the number of references to a file based on CloudFileReference records
    /// </summary>
    /// <param name="fileId">The ID of the file</param>
    /// <returns>The number of references to the file</returns>
    public async Task<int> GetReferenceCountAsync(string fileId)
    {
        return await db.FileReferences
            .Where(r => r.FileId == fileId)
            .CountAsync();
    }

    /// <summary>
    /// Checks if a file is referenced by any resource
    /// </summary>
    /// <param name="fileId">The ID of the file to check</param>
    /// <returns>True if the file is referenced, false otherwise</returns>
    public async Task<bool> IsReferencedAsync(string fileId)
    {
        return await db.FileReferences
            .Where(r => r.FileId == fileId)
            .AnyAsync();
    }

    /// <summary>
    /// Checks if an EXIF field should be ignored (e.g., GPS data).
    /// </summary>
    private static bool IsIgnoredField(string fieldName)
    {
        // Common GPS EXIF field names
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
        var tasks = files.Select(f => DeleteFileDataAsync(f, true));
        await Task.WhenAll(tasks);
        var fileIds = files.Select(f => f.Id).ToList();
        await _PurgeCacheRangeAsync(fileIds);
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
        var tasks = files.Select(f => DeleteFileDataAsync(f, true));
        await Task.WhenAll(tasks);
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
        var tasks = files.Select(f => DeleteFileDataAsync(f, true));
        await Task.WhenAll(tasks);
        var fileIds = files.Select(f => f.Id).ToList();
        await _PurgeCacheRangeAsync(fileIds);
        db.RemoveRange(files);
        await db.SaveChangesAsync();
        return count;
    }
}

/// <summary>
/// A helper class to build an ExecuteUpdateAsync call for CloudFile.
/// </summary>
file class UpdatableCloudFile(CloudFile file)
{
    public string Name { get; set; } = file.Name;
    public string? Description { get; set; } = file.Description;
    public Dictionary<string, object?>? FileMeta { get; set; } = file.FileMeta;
    public Dictionary<string, object?>? UserMeta { get; set; } = file.UserMeta;
    public bool IsMarkedRecycle { get; set; } = file.IsMarkedRecycle;

    public Expression<Func<SetPropertyCalls<CloudFile>, SetPropertyCalls<CloudFile>>> ToSetPropertyCalls()
    {
        var userMeta = UserMeta ?? new Dictionary<string, object?>();
        return setter => setter
            .SetProperty(f => f.Name, Name)
            .SetProperty(f => f.Description, Description)
            .SetProperty(f => f.FileMeta, FileMeta)
            .SetProperty(f => f.UserMeta, userMeta!)
            .SetProperty(f => f.IsMarkedRecycle, IsMarkedRecycle);
    }
}