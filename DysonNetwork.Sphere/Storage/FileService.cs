using System.Globalization;
using FFMpegCore;
using System.Security.Cryptography;
using Blurhash.ImageSharp;
using Microsoft.EntityFrameworkCore;
using Minio;
using Minio.DataModel.Args;
using NodaTime;
using Quartz;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using tusdotnet.Stores;
using ExifTag = SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag;

namespace DysonNetwork.Sphere.Storage;

public class FileService(
    AppDatabase db,
    IConfiguration configuration,
    TusDiskStore store,
    ILogger<FileService> logger,
    IServiceScopeFactory scopeFactory
)
{
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

        switch (contentType.Split('/')[0])
        {
            case "image":
                stream.Position = 0;
                // We still need ImageSharp for blurhash calculation
                using (var imageSharp = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(stream))
                {
                    var blurhash = Blurhasher.Encode(imageSharp, 3, 3);

                    // Reset stream position after ImageSharp read
                    stream.Position = 0;

                    // Use NetVips for the rest
                    using var vipsImage = NetVips.Image.NewFromStream(stream);

                    var width = vipsImage.Width;
                    var height = vipsImage.Height;
                    var format = vipsImage.Get("vips-loader") ?? "unknown";

                    // Try to get orientation from exif data
                    ushort orientation = 1;
                    List<IExifValue> exif = [];

                    // NetVips supports reading exif with vipsImage.GetField("exif-ifd0-Orientation")
                    // but we'll keep the ImageSharp exif handling for now
                    var exifProfile = imageSharp.Metadata.ExifProfile;
                    if (exifProfile?.Values.FirstOrDefault(e => e.Tag == ExifTag.Orientation)
                            ?.GetValue() is ushort o)
                        orientation = o;

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

                    List<Task> tasks = [];

                    var ogFilePath = Path.Join(configuration.GetValue<string>("Tus:StorePath"), file.Id);
                    var vipsImage = NetVips.Image.NewFromFile(ogFilePath);
                    var imagePath = Path.Join(Path.GetTempPath(), $"{TempFilePrefix}#{file.Id}");
                    tasks.Add(Task.Run(() => vipsImage.WriteToFile(imagePath + ".webp")));
                    result.Add((imagePath + ".webp", string.Empty));

                    if (vipsImage.Width * vipsImage.Height >= 1024 * 1024)
                    {
                        var scale = 1024.0 / Math.Max(vipsImage.Width, vipsImage.Height);
                        var imageCompressedPath =
                            Path.Join(Path.GetTempPath(), $"{TempFilePrefix}#{file.Id}-compressed");
                        
                        // Create and save image within the same synchronous block to avoid disposal issues
                        tasks.Add(Task.Run(() => {
                            using var compressedImage = vipsImage.Resize(scale);
                            compressedImage.WriteToFile(imageCompressedPath + ".webp");
                            vipsImage.Dispose();
                        }));
                        
                        result.Add((imageCompressedPath + ".webp", ".compressed"));
                        file.HasCompression = true;
                    }
                    else
                    {
                        vipsImage.Dispose();
                    }

                    await Task.WhenAll(tasks);
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
        if (file.UploadedTo is null) return;
        var dest = GetRemoteStorageConfig(file.UploadedTo);
        var client = CreateMinioClient(dest);
        if (client is null)
            throw new InvalidOperationException(
                $"Failed to configure client for remote destination '{file.UploadedTo}'"
            );

        var bucket = dest.Bucket;
        await client.RemoveObjectAsync(
            new RemoveObjectArgs().WithBucket(bucket).WithObject(file.Id)
        );

        db.Remove(file);
        await db.SaveChangesAsync();
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
}

public class CloudFileUnusedRecyclingJob(AppDatabase db, FileService fs, ILogger<CloudFileUnusedRecyclingJob> logger)
    : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("Deleting unused cloud files...");

        var cutoff = SystemClock.Instance.GetCurrentInstant() - Duration.FromHours(1);
        var now = SystemClock.Instance.GetCurrentInstant();
        var files = db.Files
            .Where(f =>
                (f.ExpiredAt == null && f.UsedCount == 0 && f.CreatedAt < cutoff) ||
                (f.ExpiredAt != null && f.ExpiredAt >= now)
            )
            .ToList();

        logger.LogInformation($"Deleting {files.Count} unused cloud files...");

        var tasks = files.Select(fs.DeleteFileDataAsync);
        await Task.WhenAll(tasks);

        await db.Files
            .Where(f => f.UsedCount == 0 && f.CreatedAt < cutoff)
            .ExecuteDeleteAsync();
    }
}