using System.Globalization;
using FFMpegCore;
using System.Security.Cryptography;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Proto;
using Microsoft.EntityFrameworkCore;
using Minio;
using Minio.DataModel.Args;
using NetVips;
using NodaTime;
using tusdotnet.Stores;

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
            .FirstOrDefaultAsync();

        if (file != null)
            await cache.SetAsync(cacheKey, file, CacheDuration);

        return file;
    }

    private static readonly string TempFilePrefix = "dyn-cloudfile";

    private static readonly string[] AnimatedImageTypes =
        ["image/gif", "image/apng", "image/webp", "image/avif"];

    // The analysis file method no longer will remove the GPS EXIF data
    // It should be handled on the client side, and for some specific cases it should be keep
    public async Task<CloudFile> ProcessNewFileAsync(
        Account account,
        string fileId,
        Stream stream,
        string fileName,
        string? contentType
    )
    {
        var result = new List<(string filePath, string suffix)>();

        var ogFilePath = Path.GetFullPath(Path.Join(configuration.GetValue<string>("Tus:StorePath"), fileId));
        var fileSize = stream.Length;
        var hash = await HashFileAsync(stream, fileSize: fileSize);
        contentType ??= !fileName.Contains('.') ? "application/octet-stream" : MimeTypes.GetMimeType(fileName);

        var file = new CloudFile
        {
            Id = fileId,
            Name = fileName,
            MimeType = contentType,
            Size = fileSize,
            Hash = hash,
            AccountId = Guid.Parse(account.Id)
        };

        var existingFile = await db.Files.FirstOrDefaultAsync(f => f.Hash == hash);
        file.StorageId = existingFile is not null ? existingFile.StorageId : file.Id;

        if (existingFile is not null)
        {
            file.FileMeta = existingFile.FileMeta;
            file.HasCompression = existingFile.HasCompression;
            file.SensitiveMarks = existingFile.SensitiveMarks;

            db.Files.Add(file);
            await db.SaveChangesAsync();
            return file;
        }

        switch (contentType.Split('/')[0])
        {
            case "image":
                var blurhash =
                    BlurHashSharp.SkiaSharp.BlurHashEncoder.Encode(xComponent: 3, yComponent: 3, filename: ogFilePath);

                // Rewind stream
                stream.Position = 0;

                // Use NetVips for the rest
                using (var vipsImage = NetVips.Image.NewFromStream(stream))
                {
                    var width = vipsImage.Width;
                    var height = vipsImage.Height;
                    var format = vipsImage.Get("vips-loader") ?? "unknown";

                    // Try to get orientation from exif data
                    var orientation = 1;
                    var meta = new Dictionary<string, object>
                    {
                        ["blur"] = blurhash,
                        ["format"] = format,
                        ["width"] = width,
                        ["height"] = height,
                        ["orientation"] = orientation,
                    };
                    Dictionary<string, object> exif = [];

                    foreach (var field in vipsImage.GetFields())
                    {
                        var value = vipsImage.Get(field);

                        // Skip GPS-related EXIF fields to remove location data
                        if (IsIgnoredField(field))
                            continue;

                        if (field.StartsWith("exif-")) exif[field.Replace("exif-", "")] = value;
                        else meta[field] = value;

                        if (field == "orientation") orientation = (int)value;
                    }

                    if (orientation is 6 or 8)
                        (width, height) = (height, width);

                    var aspectRatio = height != 0 ? (double)width / height : 0;

                    meta["exif"] = exif;
                    meta["ratio"] = aspectRatio;
                    file.FileMeta = meta;
                }

                break;
            case "video":
            case "audio":
                try
                {
                    var mediaInfo = await FFProbe.AnalyseAsync(ogFilePath);
                    file.FileMeta = new Dictionary<string, object>
                    {
                        ["duration"] = mediaInfo.Duration.TotalSeconds,
                        ["format_name"] = mediaInfo.Format.FormatName,
                        ["format_long_name"] = mediaInfo.Format.FormatLongName,
                        ["start_time"] = mediaInfo.Format.StartTime.ToString(),
                        ["bit_rate"] = mediaInfo.Format.BitRate.ToString(CultureInfo.InvariantCulture),
                        ["tags"] = mediaInfo.Format.Tags ?? [],
                        ["chapters"] = mediaInfo.Chapters,
                    };
                    if (mediaInfo.PrimaryVideoStream is not null)
                        file.FileMeta["ratio"] =
                            mediaInfo.PrimaryVideoStream.Width / mediaInfo.PrimaryVideoStream.Height;
                }
                catch (Exception ex)
                {
                    logger.LogError("File analyzed failed, unable collect video / audio information: {Message}",
                        ex.Message);
                }

                break;
        }

        db.Files.Add(file);
        await db.SaveChangesAsync();

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var nfs = scope.ServiceProvider.GetRequiredService<FileService>();

            try
            {
                logger.LogInformation("Processed file {fileId}, now trying optimizing if possible...", fileId);

                if (contentType.Split('/')[0] == "image")
                {
                    // Skip compression for animated image types
                    var animatedMimeTypes = AnimatedImageTypes;
                    if (Enumerable.Contains(animatedMimeTypes, contentType))
                    {
                        logger.LogInformation(
                            "File {fileId} is an animated image (MIME: {mime}), skipping WebP conversion.", fileId,
                            contentType
                        );
                        var tempFilePath = Path.Join(Path.GetTempPath(), $"{TempFilePrefix}#{file.Id}");
                        result.Add((tempFilePath, string.Empty));
                        return;
                    }

                    file.MimeType = "image/webp";

                    using var vipsImage = Image.NewFromFile(ogFilePath);
                    var imagePath = Path.Join(Path.GetTempPath(), $"{TempFilePrefix}#{file.Id}");
                    vipsImage.Autorot().WriteToFile(imagePath + ".webp",
                        new VOption { { "lossless", true }, { "strip", true } });
                    result.Add((imagePath + ".webp", string.Empty));

                    if (vipsImage.Width * vipsImage.Height >= 1024 * 1024)
                    {
                        var scale = 1024.0 / Math.Max(vipsImage.Width, vipsImage.Height);
                        var imageCompressedPath =
                            Path.Join(Path.GetTempPath(), $"{TempFilePrefix}#{file.Id}-compressed");

                        // Create and save image within the same synchronous block to avoid disposal issues
                        using var compressedImage = vipsImage.Resize(scale);
                        compressedImage.Autorot().WriteToFile(imageCompressedPath + ".webp",
                            new VOption { { "Q", 80 }, { "strip", true } });

                        result.Add((imageCompressedPath + ".webp", ".compressed"));
                        file.HasCompression = true;
                    }
                }
                else
                {
                    // No extra process for video, add it to the upload queue.
                    result.Add((ogFilePath, string.Empty));
                }

                logger.LogInformation("Optimized file {fileId}, now uploading...", fileId);

                if (result.Count > 0)
                {
                    List<Task<CloudFile>> tasks = [];
                    tasks.AddRange(result.Select(item =>
                        nfs.UploadFileToRemoteAsync(file, item.filePath, null, item.suffix, true))
                    );

                    await Task.WhenAll(tasks);
                    file = await tasks.First();
                }
                else
                {
                    file = await nfs.UploadFileToRemoteAsync(file, stream, null);
                }

                logger.LogInformation("Uploaded file {fileId} done!", fileId);

                var scopedDb = scope.ServiceProvider.GetRequiredService<AppDatabase>();
                await scopedDb.Files.Where(f => f.Id == file.Id).ExecuteUpdateAsync(setter => setter
                    .SetProperty(f => f.UploadedAt, file.UploadedAt)
                    .SetProperty(f => f.UploadedTo, file.UploadedTo)
                    .SetProperty(f => f.MimeType, file.MimeType)
                    .SetProperty(f => f.HasCompression, file.HasCompression)
                );
            }
            catch (Exception err)
            {
                logger.LogError(err, "Failed to process {fileId}", fileId);
            }

            await stream.DisposeAsync();
            await store.DeleteFileAsync(file.Id, CancellationToken.None);
            await nfs._PurgeCacheAsync(file.Id);
        });

        return file;
    }

    private static async Task<string> HashFileAsync(Stream stream, int chunkSize = 1024 * 1024, long? fileSize = null)
    {
        fileSize ??= stream.Length;
        if (fileSize > chunkSize * 1024 * 5)
            return await HashFastApproximateAsync(stream, chunkSize);

        using var md5 = MD5.Create();
        var hashBytes = await md5.ComputeHashAsync(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static async Task<string> HashFastApproximateAsync(Stream stream, int chunkSize = 1024 * 1024)
    {
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
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task<CloudFile> UploadFileToRemoteAsync(CloudFile file, string filePath, string? targetRemote,
        string? suffix = null, bool selfDestruct = false)
    {
        var fileStream = File.OpenRead(filePath);
        var result = await UploadFileToRemoteAsync(file, fileStream, targetRemote, suffix);
        if (selfDestruct) File.Delete(filePath);
        return result;
    }

    public async Task<CloudFile> UploadFileToRemoteAsync(CloudFile file, Stream stream, string? targetRemote,
        string? suffix = null)
    {
        if (file.UploadedAt.HasValue) return file;

        file.UploadedTo = targetRemote ?? configuration.GetValue<string>("Storage:PreferredRemote")!;

        var dest = GetRemoteStorageConfig(file.UploadedTo);
        var client = CreateMinioClient(dest);
        if (client is null)
            throw new InvalidOperationException(
                $"Failed to configure client for remote destination '{file.UploadedTo}'"
            );

        var bucket = dest.Bucket;
        var contentType = file.MimeType ?? "application/octet-stream";

        await client.PutObjectAsync(new PutObjectArgs()
            .WithBucket(bucket)
            .WithObject(string.IsNullOrWhiteSpace(suffix) ? file.Id : file.Id + suffix)
            .WithStreamData(stream) // Fix this disposed
            .WithObjectSize(stream.Length)
            .WithContentType(contentType)
        );

        file.UploadedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);
        return file;
    }

    public async Task DeleteFileAsync(CloudFile file)
    {
        await DeleteFileDataAsync(file);

        db.Remove(file);
        await db.SaveChangesAsync();
        await _PurgeCacheAsync(file.Id);
    }

    public async Task DeleteFileDataAsync(CloudFile file)
    {
        if (file.StorageId is null) return;
        if (file.UploadedTo is null) return;

        // Check if any other file with the same storage ID is referenced
        var otherFilesWithSameStorageId = await db.Files
            .Where(f => f.StorageId == file.StorageId && f.Id != file.Id)
            .Select(f => f.Id)
            .ToListAsync();

        // Check if any of these files are referenced
        var anyReferenced = false;
        if (otherFilesWithSameStorageId.Any())
        {
            anyReferenced = await db.FileReferences
                .Where(r => otherFilesWithSameStorageId.Contains(r.FileId))
                .AnyAsync();
        }

        // If any other file with the same storage ID is referenced, don't delete the actual file data
        if (anyReferenced) return;

        var dest = GetRemoteStorageConfig(file.UploadedTo);
        var client = CreateMinioClient(dest);
        if (client is null)
            throw new InvalidOperationException(
                $"Failed to configure client for remote destination '{file.UploadedTo}'"
            );

        var bucket = dest.Bucket;
        var objectId = file.StorageId ?? file.Id; // Use StorageId if available, otherwise fall back to Id

        await client.RemoveObjectAsync(
            new RemoveObjectArgs().WithBucket(bucket).WithObject(objectId)
        );

        if (file.HasCompression)
        {
            // Also remove the compressed version if it exists
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
    }

    public RemoteStorageConfig GetRemoteStorageConfig(string destination)
    {
        var destinations = configuration.GetSection("Storage:Remote").Get<List<RemoteStorageConfig>>()!;
        var dest = destinations.FirstOrDefault(d => d.Id == destination);
        if (dest is null) throw new InvalidOperationException($"Remote destination '{destination}' not found");
        return dest;
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
    /// Checks if an EXIF field contains GPS location data
    /// </summary>
    /// <param name="fieldName">The EXIF field name</param>
    /// <returns>True if the field contains GPS data, false otherwise</returns>
    private static bool IsGpsExifField(string fieldName)
    {
        // Common GPS EXIF field names
        var gpsFields = new[]
        {
            "gps-latitude",
            "gps-longitude",
            "gps-altitude",
            "gps-latitude-ref",
            "gps-longitude-ref",
            "gps-altitude-ref",
            "gps-timestamp",
            "gps-datestamp",
            "gps-speed",
            "gps-speed-ref",
            "gps-track",
            "gps-track-ref",
            "gps-img-direction",
            "gps-img-direction-ref",
            "gps-dest-latitude",
            "gps-dest-longitude",
            "gps-dest-latitude-ref",
            "gps-dest-longitude-ref",
            "gps-processing-method",
            "gps-area-information"
        };

        return gpsFields.Any(gpsField =>
            fieldName.Equals(gpsField, StringComparison.OrdinalIgnoreCase) ||
            fieldName.StartsWith("gps", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsIgnoredField(string fieldName)
    {
        if (IsGpsExifField(fieldName)) return true;
        if (fieldName.EndsWith("-data")) return true;
        return false;
    }
}