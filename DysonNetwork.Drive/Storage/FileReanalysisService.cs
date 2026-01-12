using System.Globalization;
using FFMpegCore;
using Microsoft.EntityFrameworkCore;
using Minio;
using Minio.DataModel.Args;
using NetVips;
using DysonNetwork.Shared.Models;

namespace DysonNetwork.Drive.Storage;

public class FileReanalysisService(
    AppDatabase db,
    ILogger<FileReanalysisService> logger
)
{
    public async Task<List<SnCloudFile>> GetFilesNeedingReanalysisAsync(int limit = 100)
    {
        return await db.Files
            .Where(f => f.ObjectId != null && f.PoolId != null)
            .Include(f => f.Object)
            .Include(f => f.Pool)
            .Where(f => f.Object != null && (f.Object.Meta == null || f.Object.Meta.Count == 0))
            .Take(limit)
            .ToListAsync();
    }

    public async Task ReanalyzeFileAsync(SnCloudFile file)
    {
        logger.LogInformation("Starting reanalysis for file {FileId}: {FileName}", file.Id, file.Name);

        if (file.Object == null || file.Pool == null)
        {
            logger.LogWarning("File {FileId} missing object or pool, skipping reanalysis", file.Id);
            return;
        }

        var primaryReplica = file.Object.FileReplicas.FirstOrDefault(r => r.IsPrimary);
        if (primaryReplica == null)
        {
            logger.LogWarning("File {FileId} has no primary replica, skipping reanalysis", file.Id);
            return;
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"reanalysis_{file.Id}_{Guid.NewGuid()}");
        try
        {
            await DownloadFileAsync(file, primaryReplica, tempPath);

            var meta = await ExtractMetadataAsync(file, tempPath);
            if (meta != null && meta.Count > 0)
            {
                file.Object.Meta = meta;
                await db.SaveChangesAsync();
                logger.LogInformation("Successfully reanalyzed file {FileId}, updated metadata with {MetaCount} fields", file.Id, meta.Count);
            }
            else
            {
                logger.LogWarning("No metadata extracted for file {FileId}", file.Id);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to reanalyze file {FileId}", file.Id);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public async Task ProcessNextFileAsync()
    {
        var files = await GetFilesNeedingReanalysisAsync(1);
        if (files.Count == 0)
        {
            logger.LogInformation("No files found needing reanalysis");
            return;
        }

        var file = files[0];
        await ReanalyzeFileAsync(file);
    }

    private async Task DownloadFileAsync(SnCloudFile file, SnFileReplica replica, string tempPath)
    {
        var dest = file.Pool!.StorageConfig;
        if (dest == null)
        {
            throw new InvalidOperationException($"No remote storage configured for pool {file.PoolId}");
        }

        var client = CreateMinioClient(dest);
        if (client == null)
        {
            throw new InvalidOperationException($"Failed to create Minio client for pool {file.PoolId}");
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
                logger.LogDebug("Skipping metadata extraction for unsupported MIME type {MimeType} on file {FileId}", mimeType, file.Id);
                return null;
        }
    }

    private async Task<Dictionary<string, object?>?> ExtractImageMetadataAsync(SnCloudFile file, string filePath)
    {
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
                ["blurhash"] = blurhash,
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
