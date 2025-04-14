using System.Globalization;
using FFMpegCore;
using System.Security.Cryptography;
using Blurhash.ImageSharp;
using Microsoft.EntityFrameworkCore;
using Minio;
using Minio.DataModel.Args;
using NodaTime;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using ExifTag = SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag;

namespace DysonNetwork.Sphere.Storage;

public class FileService(AppDatabase db, IConfiguration configuration)
{
    // The analysis file method no longer will remove the GPS EXIF data
    // It should be handled on the client side, and for some specific cases it should be keep
    public async Task<CloudFile> AnalyzeFileAsync(
        Account.Account account,
        string fileId,
        Stream stream,
        string fileName,
        string? contentType
    )
    {
        var fileSize = stream.Length;
        var hash = await HashFileAsync(stream, fileSize: fileSize);
        contentType ??= !fileName.Contains('.') ? "application/octet-stream" : MimeTypes.GetMimeType(fileName);

        var existingFile = await db.Files.Where(f => f.Hash == hash).FirstOrDefaultAsync();
        if (existingFile is not null) return existingFile;

        var file = new CloudFile
        {
            Id = fileId,
            Name = fileName,
            MimeType = contentType,
            Size = fileSize,
            Hash = hash,
            Account = account,
        };

        switch (contentType.Split('/')[0])
        {
            case "image":
                stream.Position = 0;
                using (var imageSharp = await Image.LoadAsync<Rgba32>(stream))
                {
                    var width = imageSharp.Width;
                    var height = imageSharp.Height;
                    var blurhash = Blurhasher.Encode(imageSharp, 3, 3);
                    var format = imageSharp.Metadata.DecodedImageFormat?.Name ?? "unknown";

                    var exifProfile = imageSharp.Metadata.ExifProfile;
                    ushort orientation = 1;
                    List<IExifValue> exif = [];

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
                break;
        }

        db.Files.Add(file);
        await db.SaveChangesAsync();
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

    public async Task<CloudFile> UploadFileToRemoteAsync(CloudFile file, Stream stream, string? targetRemote)
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
            .WithObject(file.Id)
            .WithStreamData(stream)
            .WithObjectSize(stream.Length)
            .WithContentType(contentType)
        );

        file.UploadedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);
        db.Update(file);
        await db.SaveChangesAsync();
        return file;
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

        return;
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
}