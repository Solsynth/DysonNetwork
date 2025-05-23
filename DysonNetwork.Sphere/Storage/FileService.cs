using System.Globalization;
using FFMpegCore;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Minio;
using Minio.DataModel.Args;
using NodaTime;
using Quartz;
using tusdotnet.Stores;

namespace DysonNetwork.Sphere.Storage;

public class FileService(
    AppDatabase db,
    IConfiguration configuration,
    TusDiskStore store,
    ILogger<FileService> logger,
    IServiceScopeFactory scopeFactory,
    IMemoryCache cache
)
{
    private const string CacheKeyPrefix = "cloudfile_";
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
        
        if (cache.TryGetValue(cacheKey, out CloudFile? cachedFile))
            return cachedFile;
    
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == fileId);
        
        if (file != null)
            cache.Set(cacheKey, file, CacheDuration);
            
        return file;
    }
    
    private static readonly string TempFilePrefix = "dyn-cloudfile";

    // The analysis file method no longer will remove the GPS EXIF data
    // It should be handled on the client side, and for some specific cases it should be keep
    public async Task<CloudFile> ProcessNewFileAsync(
        Account.Account account,
        string fileId,
        Stream stream,
        string fileName,
        string? contentType
    )
    {
        var result = new List<(string filePath, string suffix)>();

        var ogFilePath = Path.Join(configuration.GetValue<string>("Tus:StorePath"), fileId);
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
            AccountId = account.Id
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
                    int orientation = 1;
                    Dictionary<string, object> exif = [];

                    foreach (var field in vipsImage.GetFields())
                    {
                        var value = vipsImage.Get(field);
                        exif.Add(field, value);
                        if (field == "orientation") orientation = (int)value;
                    }

                    if (orientation is 6 or 8)
                        (width, height) = (height, width);

                    var aspectRatio = height != 0 ? (double)width / height : 0;

                    file.FileMeta = new Dictionary<string, object>
                    {
                        ["blur"] = blurhash,
                        ["format"] = format,
                        ["width"] = width,
                        ["height"] = height,
                        ["orientation"] = orientation,
                        ["ratio"] = aspectRatio,
                        ["exif"] = exif
                    };
                }

                break;
            case "video":
            case "audio":
                try
                {
                    var mediaInfo = await FFProbe.AnalyseAsync(stream);
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
                }
                catch
                {
                    // ignored
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
                    file.MimeType = "image/webp";

                    using var vipsImage = NetVips.Image.NewFromFile(ogFilePath);
                    var imagePath = Path.Join(Path.GetTempPath(), $"{TempFilePrefix}#{file.Id}");
                    vipsImage.WriteToFile(imagePath + ".webp");
                    result.Add((imagePath + ".webp", string.Empty));

                    if (vipsImage.Width * vipsImage.Height >= 1024 * 1024)
                    {
                        var scale = 1024.0 / Math.Max(vipsImage.Width, vipsImage.Height);
                        var imageCompressedPath =
                            Path.Join(Path.GetTempPath(), $"{TempFilePrefix}#{file.Id}-compressed");

                        // Create and save image within the same synchronous block to avoid disposal issues
                        using var compressedImage = vipsImage.Resize(scale);
                        compressedImage.WriteToFile(imageCompressedPath + ".webp");

                        result.Add((imageCompressedPath + ".webp", ".compressed"));
                        file.HasCompression = true;
                    }
                }
                else
                {
                    var tempFilePath = Path.Join(Path.GetTempPath(), $"{TempFilePrefix}#{file.Id}");
                    await using var fileStream = File.Create(tempFilePath);
                    stream.Position = 0;
                    await stream.CopyToAsync(fileStream);
                    result.Add((tempFilePath, string.Empty));
                }

                logger.LogInformation("Optimized file {fileId}, now uploading...", fileId);

                if (result.Count > 0)
                {
                    List<Task<CloudFile>> tasks = [];
                    tasks.AddRange(result.Select(result =>
                        nfs.UploadFileToRemoteAsync(file, result.filePath, null, result.suffix, true)));

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
    }

    public async Task DeleteFileDataAsync(CloudFile file)
    {
        if (file.StorageId is null) return;
        if (file.UploadedTo is null) return;

        var repeatedStorageId = await db.Files
            .Where(f => f.StorageId == file.StorageId && f.Id != file.Id && f.UsedCount > 0)
            .AnyAsync();
        if (repeatedStorageId) return;

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

    public async Task MarkUsageAsync(CloudFile file, int delta)
    {
        await db.Files.Where(o => o.Id == file.Id)
            .ExecuteUpdateAsync(setter => setter.SetProperty(
                    b => b.UsedCount,
                    b => b.UsedCount + delta
                )
            );
    }

    public async Task MarkUsageRangeAsync(ICollection<CloudFile> files, int delta)
    {
        var ids = files.Select(f => f.Id).ToArray();
        await db.Files.Where(o => ids.Contains(o.Id))
            .ExecuteUpdateAsync(setter => setter.SetProperty(
                    b => b.UsedCount,
                    b => b.UsedCount + delta
                )
            );
    }
    
    

    public async Task SetExpiresRangeAsync(ICollection<CloudFile> files, Duration? duration)
    {
        var ids = files.Select(f => f.Id).ToArray();
        await db.Files.Where(o => ids.Contains(o.Id))
            .ExecuteUpdateAsync(setter => setter.SetProperty(
                    b => b.ExpiredAt,
                    duration.HasValue 
                        ? b => SystemClock.Instance.GetCurrentInstant() + duration.Value
                        : _ => null
                )
            );
    }
    
    public async Task<(ICollection<CloudFile> current, ICollection<CloudFile> added, ICollection<CloudFile> removed)> DiffAndMarkFilesAsync(
        ICollection<string>? newFileIds,
        ICollection<CloudFile>? previousFiles = null
    )
    {
        if (newFileIds == null) return ([], [], previousFiles ?? []);
        
        var records = await db.Files.Where(f => newFileIds.Contains(f.Id)).ToListAsync();
        var previous = previousFiles?.ToDictionary(f => f.Id) ?? new Dictionary<string, CloudFile>();
        var current = records.ToDictionary(f => f.Id);
    
        var added = current.Keys.Except(previous.Keys).Select(id => current[id]).ToList();
        var removed = previous.Keys.Except(current.Keys).Select(id => previous[id]).ToList();
    
        if (added.Count > 0) await MarkUsageRangeAsync(added, 1);
        if (removed.Count > 0) await MarkUsageRangeAsync(removed, -1);
    
        return (newFileIds.Select(id => current[id]).ToList(), added, removed);
    }
    
    public async Task<(ICollection<CloudFile> current, ICollection<CloudFile> added, ICollection<CloudFile> removed)> DiffAndSetExpiresAsync(
        ICollection<string>? newFileIds,
        Duration? duration,
        ICollection<CloudFile>? previousFiles = null
    )
    {
        if (newFileIds == null) return ([], [], previousFiles ?? []);
        
        var records = await db.Files.Where(f => newFileIds.Contains(f.Id)).ToListAsync();
        var previous = previousFiles?.ToDictionary(f => f.Id) ?? new Dictionary<string, CloudFile>();
        var current = records.ToDictionary(f => f.Id);
    
        var added = current.Keys.Except(previous.Keys).Select(id => current[id]).ToList();
        var removed = previous.Keys.Except(current.Keys).Select(id => previous[id]).ToList();
    
        if (added.Count > 0) await SetExpiresRangeAsync(added, duration);
        if (removed.Count > 0) await SetExpiresRangeAsync(removed, null);
    
        return (newFileIds.Select(id => current[id]).ToList(), added, removed);
    }
    
    
}

public class CloudFileUnusedRecyclingJob(AppDatabase db, FileService fs, ILogger<CloudFileUnusedRecyclingJob> logger)
    : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("Deleting unused cloud files...");

        var cutoff = SystemClock.Instance.GetCurrentInstant() - Duration.FromHours(1);
        var now = SystemClock.Instance.GetCurrentInstant();

        // Get files to delete along with their storage IDs
        var files = await db.Files
            .Where(f =>
                (f.ExpiredAt == null && f.UsedCount == 0 && f.CreatedAt < cutoff) ||
                (f.ExpiredAt != null && f.ExpiredAt >= now)
            )
            .ToListAsync();

        if (files.Count == 0)
        {
            logger.LogInformation("No files to delete");
            return;
        }

        logger.LogInformation($"Found {files.Count} files to process...");

        // Group files by StorageId and find which ones are safe to delete
        var storageIds = files.Where(f => f.StorageId != null)
            .Select(f => f.StorageId!)
            .Distinct()
            .ToList();

        var usedStorageIds = await db.Files
            .Where(f => f.StorageId != null &&
                        storageIds.Contains(f.StorageId) &&
                        !files.Select(ff => ff.Id).Contains(f.Id))
            .Select(f => f.StorageId!)
            .Distinct()
            .ToListAsync();

        // Group files for deletion
        var filesToDelete = files.Where(f => f.StorageId == null || !usedStorageIds.Contains(f.StorageId))
            .GroupBy(f => f.UploadedTo)
            .ToDictionary(grouping => grouping.Key!, grouping => grouping.ToList());

        // Delete files by remote storage
        foreach (var group in filesToDelete)
        {
            if (string.IsNullOrEmpty(group.Key)) continue;

            try
            {
                var dest = fs.GetRemoteStorageConfig(group.Key);
                var client = fs.CreateMinioClient(dest);
                if (client == null) continue;

                // Create delete tasks for each file in the group
                var deleteTasks = group.Value.Select(file =>
                {
                    var objectId = file.StorageId ?? file.Id;
                    var tasks = new List<Task>
                    {
                        client.RemoveObjectAsync(new RemoveObjectArgs()
                            .WithBucket(dest.Bucket)
                            .WithObject(objectId))
                    };

                    if (file.HasCompression)
                    {
                        tasks.Add(client.RemoveObjectAsync(new RemoveObjectArgs()
                            .WithBucket(dest.Bucket)
                            .WithObject(objectId + ".compressed")));
                    }

                    return Task.WhenAll(tasks);
                });

                await Task.WhenAll(deleteTasks);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error deleting files from remote storage {remote}", group.Key);
            }
        }

        // Delete all file records from the database
        var fileIds = files.Select(f => f.Id).ToList();
        await db.Files
            .Where(f => fileIds.Contains(f.Id))
            .ExecuteDeleteAsync();

        logger.LogInformation($"Completed deleting {files.Count} files");
    }
}