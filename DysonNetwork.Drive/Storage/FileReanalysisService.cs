using System.Globalization;
using System.Security.Cryptography;
using DysonNetwork.Drive.Storage.Options;
using FFMpegCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using NetVips;
using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Drive.Storage;

public class FileReanalysisService(
    AppDatabase db,
    ILogger<FileReanalysisService> logger,
    IOptions<FileReanalysisOptions> options)
{
    private readonly FileReanalysisOptions _options = options.Value;
    private readonly HashSet<string> _failedFileIds = [];
    private readonly Dictionary<string, HashSet<string>> _bucketObjectCache = new();
    private int _totalProcessed = 0;
    private int _reanalysisSuccess = 0;
    private int _reanalysisFailure = 0;
    private int _validationCompressionProcessed = 0;
    private int _validationThumbnailProcessed = 0;

    private async Task<List<SnCloudFile>> GetFilesNeedingReanalysisAsync(int limit = 1000)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var deadline = now.Minus(Duration.FromMinutes(30));
        return await db.Files
            .Where(f => f.ObjectId != null)
            .Include(f => f.Object)
            .ThenInclude(f => f.FileReplicas)
            .Where(f => ((f.Object!.MimeType == null || !f.Object.MimeType.StartsWith("application/")) &&
                         (f.Object!.Meta == null || f.Object.Meta.Count == 0)) || f.Object.Size == 0 ||
                        f.Object.Hash == null)
            .Where(f => f.Object!.FileReplicas.Count > 0)
            .Where(f => f.CreatedAt <= deadline)
            .OrderBy(f => f.Object!.UpdatedAt)
            .Skip(_failedFileIds.Count)
            .Take(limit)
            .ToListAsync();
    }

    private async Task<List<SnCloudFile>> GetFilesNeedingCompressionValidationAsync(int offset, int limit = 1000)
    {
        return await db.Files
            .Where(f => f.ObjectId != null)
            .Include(f => f.Object)
            .ThenInclude(o => o!.FileReplicas)
            .Where(f => f.Object!.HasCompression)
            .Where(f => f.Object!.FileReplicas.Any(r => r.IsPrimary))
            .Take(limit)
            .Skip(offset)
            .ToListAsync();
    }

    private async Task<List<SnCloudFile>> GetFilesNeedingThumbnailValidationAsync(int offset, int limit = 1000)
    {
        return await db.Files
            .Where(f => f.ObjectId != null)
            .Include(f => f.Object)
            .ThenInclude(o => o!.FileReplicas)
            .Where(f => f.Object!.HasThumbnail)
            .Where(f => f.Object!.FileReplicas.Any(r => r.IsPrimary))
            .Take(limit)
            .Skip(offset)
            .ToListAsync();
    }

    private async Task<bool> ReanalyzeFileAsync(SnCloudFile file)
    {
        logger.LogInformation("Starting reanalysis for file {FileId}: {FileName}", file.Id, file.Name);

        if (file.Object == null)
        {
            logger.LogWarning("File {FileId} missing object, skipping reanalysis", file.Id);
            return true; // not a failure
        }

        if (file.Object.MimeType != null && file.Object.MimeType.StartsWith("application/") && file.Object.Size != 0 &&
            file.Object.Hash != null)
        {
            logger.LogInformation("File {FileId} already reanalyzed, no need for reanalysis", file.Id);
            return true; // skip
        }

        var primaryReplica = file.Object.FileReplicas.FirstOrDefault(r => r.IsPrimary);
        if (primaryReplica == null)
        {
            logger.LogWarning("File {FileId} has no primary replica, skipping reanalysis", file.Id);
            return true; // not a failure
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"reanalysis_{file.Id}_{Guid.NewGuid()}");
        try
        {
            await DownloadFileAsync(file, primaryReplica, tempPath);

            var fileInfo = new FileInfo(tempPath);
            var actualSize = fileInfo.Length;
            var actualHash = await HashFileAsync(tempPath);

            var meta = await ExtractMetadataAsync(file, tempPath);

            if (meta == null && !string.IsNullOrEmpty(file.MimeType) && (file.MimeType.StartsWith("image/") ||
                                                                         file.MimeType.StartsWith("video/") ||
                                                                         file.MimeType.StartsWith("audio/")))
            {
                logger.LogWarning("Failed to extract metadata for supported MIME type {MimeType} on file {FileId}",
                    file.MimeType, file.Id);
            }

            var updated = false;
            if (file.Object.Size == 0 || file.Object.Size != actualSize)
            {
                file.Object.Size = actualSize;
                updated = true;
            }

            if (string.IsNullOrEmpty(file.Object.Hash) || file.Object.Hash != actualHash)
            {
                file.Object.Hash = actualHash;
                updated = true;
            }

            if (meta is { Count: > 0 })
            {
                file.Object.Meta = meta;
                updated = true;
            }

            if (updated)
            {
                db.FileObjects.Update(file.Object);
                await db.SaveChangesAsync();
                var metaCount = meta?.Count ?? 0;
                logger.LogInformation("Successfully reanalyzed file {FileId}, updated metadata with {MetaCount} fields",
                    file.Id, metaCount);
            }
            else
            {
                logger.LogInformation("File {FileId} already up to date", file.Id);
            }

            return true;
        }
        catch (ObjectNotFoundException)
        {
            logger.LogWarning("File {FileId} not found in remote storage, deleting record", file.Id);
            db.Files.Remove(file);
            await db.SaveChangesAsync();
            return true; // handled
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to reanalyze file {FileId}", file.Id);
            return false; // failure
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private async Task ValidateBatchCompressionAndThumbnailAsync(
        List<SnCloudFile> files,
        bool validateCompression,
        bool validateThumbnail
    )
    {
        var poolIds = files.Select(f => f.Object!.FileReplicas.First(r => r.IsPrimary).PoolId)
            .Where(pid => pid.HasValue)
            .Select(pid => pid!.Value)
            .Distinct()
            .ToList();
        var pools = await db.Pools.Where(p => poolIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

        var groupedByPool = files.GroupBy(f => f.Object!.FileReplicas.First(r => r.IsPrimary).PoolId);

        foreach (var group in groupedByPool)
        {
            if (!group.Key.HasValue) continue;
            var poolId = group.Key.Value;
            var poolFiles = group.ToList();

            if (!pools.TryGetValue(poolId, out var pool))
            {
                logger.LogWarning("No pool found for pool {PoolId}, skipping batch validation", poolId);
                continue;
            }

            var dest = pool.StorageConfig;
            var client = CreateMinioClient(dest);
            if (client == null)
            {
                logger.LogWarning("Failed to create Minio client for pool {PoolId}, skipping batch validation", poolId);
                continue;
            }

            foreach (var file in poolFiles)
            {
                if (file.Object == null) continue;
                var primaryReplica = file.Object.FileReplicas.FirstOrDefault(r => r.IsPrimary);
                if (primaryReplica == null) continue;

                var baseStorageId = primaryReplica.StorageId;

                if (validateCompression && file.Object.HasCompression)
                {
                    try
                    {
                        var statArgs = new StatObjectArgs()
                            .WithBucket(dest.Bucket)
                            .WithObject(baseStorageId + ".compressed");
                        await client.StatObjectAsync(statArgs);
                    }
                    catch (ObjectNotFoundException)
                    {
                        logger.LogInformation(
                            "File {FileId} has compression flag but compressed version not found, setting HasCompression to false",
                            file.Id);
                        await db.FileObjects
                            .Where(f => f.Id == file.ObjectId!)
                            .ExecuteUpdateAsync(p => p.SetProperty(c => c.HasCompression, false));
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to stat compressed version for file {FileId}", file.Id);
                    }
                }

                if (validateThumbnail && file.Object.HasThumbnail)
                {
                    try
                    {
                        var statArgs = new StatObjectArgs()
                            .WithBucket(dest.Bucket)
                            .WithObject(baseStorageId + ".thumbnail");
                        await client.StatObjectAsync(statArgs);
                    }
                    catch (ObjectNotFoundException)
                    {
                        logger.LogInformation(
                            "File {FileId} has thumbnail flag but thumbnail not found, setting HasThumbnail to false",
                            file.Id);
                        await db.FileObjects
                            .Where(f => f.Id == file.ObjectId!)
                            .ExecuteUpdateAsync(p => p.SetProperty(c => c.HasThumbnail, false));
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to stat thumbnail for file {FileId}", file.Id);
                    }
                }
            }
        }
    }

    public async Task ProcessNextFileAsync()
    {
        List<SnCloudFile> reanalysisFiles = [];
        if (_options.Enabled)
        {
            reanalysisFiles = await GetFilesNeedingReanalysisAsync(10);
            reanalysisFiles = reanalysisFiles.Where(f => !_failedFileIds.Contains(f.Id.ToString())).ToList();

            if (reanalysisFiles.Count > 0)
            {
                var file = reanalysisFiles[0];
                var success = await ReanalyzeFileAsync(file);
                if (!success)
                {
                    logger.LogWarning("Failed to reanalyze file {FileId}, skipping for now", file.Id);
                    _failedFileIds.Add(file.Id);
                    _reanalysisFailure++;
                }
                else
                {
                    _reanalysisSuccess++;
                }

                _totalProcessed++;
                var successRate = (_reanalysisSuccess + _reanalysisFailure) > 0
                    ? (double)_reanalysisSuccess / (_reanalysisSuccess + _reanalysisFailure) * 100
                    : 0;
                logger.LogInformation(
                    "Reanalysis progress: {ReanalysisSuccess} succeeded, {ReanalysisFailure} failed ({SuccessRate:F1}%)",
                    _reanalysisSuccess, _reanalysisFailure, successRate);

                return;
            }
        }
        else
        {
            logger.LogDebug("File reanalysis is disabled, skipping reanalysis but continuing with validation");
        }

        if (_options.ValidateCompression)
        {
            var compressionFiles = await GetFilesNeedingCompressionValidationAsync(_validationCompressionProcessed);
            if (compressionFiles.Count > 0)
            {
                await ValidateBatchCompressionAndThumbnailAsync(compressionFiles, true, false);
                _validationCompressionProcessed += compressionFiles.Count;
                _totalProcessed += compressionFiles.Count;
                logger.LogInformation("Batch compression validation progress: {ValidationProcessed} processed",
                    _validationCompressionProcessed);
                return;
            }
        }

        if (_options.ValidateThumbnails)
        {
            var thumbnailFiles = await GetFilesNeedingThumbnailValidationAsync(_validationThumbnailProcessed);
            if (thumbnailFiles.Count > 0)
            {
                await ValidateBatchCompressionAndThumbnailAsync(thumbnailFiles, false, true);
                _validationThumbnailProcessed += thumbnailFiles.Count;
                _totalProcessed += thumbnailFiles.Count;
                logger.LogInformation("Batch thumbnail validation progress: {ValidationProcessed} processed",
                    _validationThumbnailProcessed);
                return;
            }
        }

        if (reanalysisFiles.Count > 0 && !_options.Enabled)
        {
            logger.LogInformation("Reanalysis is disabled, no other work to do");
        }
        else
        {
            logger.LogInformation("No files found needing reanalysis or validation");
        }
    }

    private async Task DownloadFileAsync(SnCloudFile file, SnFileReplica replica, string tempPath)
    {
        if (replica.PoolId == null)
        {
            throw new InvalidOperationException($"Replica for file {file.Id} has no pool ID");
        }

        var pool = await db.Pools.FindAsync(replica.PoolId.Value);
        if (pool == null)
        {
            throw new InvalidOperationException($"No remote storage configured for pool {replica.PoolId}");
        }

        var dest = pool.StorageConfig;

        var client = CreateMinioClient(dest);
        if (client == null)
        {
            throw new InvalidOperationException($"Failed to create Minio client for pool {replica.PoolId}");
        }

        await using var fileStream = File.Create(tempPath);
        var getObjectArgs = new GetObjectArgs()
            .WithBucket(dest.Bucket)
            .WithObject(replica.StorageId)
            .WithCallbackStream(async (stream, cancellationToken) =>
            {
                await stream.CopyToAsync(fileStream, cancellationToken);
            });

        await client.GetObjectAsync(getObjectArgs);
        logger.LogDebug("Downloaded file {FileId} to {TempPath}", file.Id, tempPath);
    }

    private async Task<Dictionary<string, object?>?> ExtractMetadataAsync(SnCloudFile file, string filePath)
    {
        var mimeType = file.MimeType;
        if (string.IsNullOrEmpty(mimeType))
        {
            logger.LogWarning("File {FileId} has no MIME type, skipping metadata extraction", file.Id);
            return null;
        }

        switch (mimeType.Split('/')[0])
        {
            case "image":
                return await ExtractImageMetadataAsync(file, filePath);
            case "video":
            case "audio":
                return await ExtractMediaMetadataAsync(file, filePath);
            default:
                logger.LogDebug("Skipping metadata extraction for unsupported MIME type {MimeType} on file {FileId}",
                    mimeType, file.Id);
                return null;
        }
    }

    private async Task<Dictionary<string, object?>?> ExtractImageMetadataAsync(SnCloudFile file, string filePath)
    {
        try
        {
            string? blurhash = null;
            try
            {
                blurhash = BlurHashSharp.SkiaSharp.BlurHashEncoder.Encode(3, 3, filePath);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to generate blurhash for file {FileId}, skipping", file.Id);
            }

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
                ["format"] = vipsImage.Get("vips-loader") ?? "unknown",
                ["width"] = width,
                ["height"] = height,
                ["orientation"] = orientation,
            };

            if (blurhash != null)
            {
                meta["blurhash"] = blurhash;
            }

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
            return meta;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to analyze image file {FileId}", file.Id);
            return null;
        }
    }

    private async Task<Dictionary<string, object?>?> ExtractMediaMetadataAsync(SnCloudFile file, string filePath)
    {
        try
        {
            var mediaInfo = await FFProbe.AnalyseAsync(filePath);
            var meta = new Dictionary<string, object?>
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
                meta["ratio"] = (double)mediaInfo.PrimaryVideoStream.Width /
                                mediaInfo.PrimaryVideoStream.Height;
            return meta;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to analyze media file {FileId}", file.Id);
            return null;
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

    private IMinioClient? CreateMinioClient(RemoteStorageConfig dest)
    {
        var client = new MinioClient()
            .WithEndpoint(dest.Endpoint)
            .WithRegion(dest.Region)
            .WithCredentials(dest.SecretId, dest.SecretKey);
        if (dest.EnableSsl) client = client.WithSSL();

        return client.Build();
    }
}