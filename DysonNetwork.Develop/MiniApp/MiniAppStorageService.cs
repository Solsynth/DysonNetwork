using Minio;
using Minio.DataModel.Args;
using System.Text.Json;
using System.IO.Compression;
using System.Security.Cryptography;

namespace DysonNetwork.Develop.MiniApp;

public class MiniAppStorageS3Configuration
{
    public string? ServiceUrl { get; set; }
    public string? Endpoint { get; set; }
    public string? PublicBaseUrl { get; set; }
    public string Region { get; set; } = "us-east-1";
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string Bucket { get; set; } = string.Empty;
    public bool EnableSsl { get; set; } = true;
}

public class MiniAppStorageConfiguration
{
    public const long MaxPackageFileSizeBytes = 5 * 1024 * 1024;
    public const long MaxMultipartRequestSizeBytes = MaxPackageFileSizeBytes + 64 * 1024;

    public string KeyPrefix { get; set; } = "plugins";
    public MiniAppStorageS3Configuration S3 { get; set; } = new();
}

public record MiniAppPackageUploadResult(
    string Key,
    string? Url,
    string FileName,
    string ContentType,
    long Size,
    string Sha256
);

public class MiniAppStorageService(IConfiguration configuration)
{
    private readonly MiniAppStorageConfiguration _config =
        configuration.GetSection("PluginStorage").Get<MiniAppStorageConfiguration>()
        ?? new MiniAppStorageConfiguration();

    private readonly Lazy<IMinioClient> _s3 = new(() =>
    {
        var config = configuration.GetSection("PluginStorage").Get<MiniAppStorageConfiguration>()
            ?? new MiniAppStorageConfiguration();
        var s3 = config.S3;

        if (string.IsNullOrWhiteSpace(s3.Bucket))
            throw new InvalidOperationException("PluginStorage:S3:Bucket is required.");
        if (string.IsNullOrWhiteSpace(s3.AccessKey) || string.IsNullOrWhiteSpace(s3.SecretKey))
            throw new InvalidOperationException("PluginStorage:S3 credentials are required.");

        var endpoint = s3.Endpoint;
        var useSsl = s3.EnableSsl;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            if (string.IsNullOrWhiteSpace(s3.ServiceUrl))
                throw new InvalidOperationException("PluginStorage:S3:Endpoint or ServiceUrl is required.");

            var uri = new Uri(s3.ServiceUrl);
            endpoint = uri.Authority;
            useSsl = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        }

        var client = new MinioClient()
            .WithEndpoint(endpoint)
            .WithRegion(s3.Region)
            .WithCredentials(s3.AccessKey, s3.SecretKey);

        if (useSsl)
            client = client.WithSSL();

        return client.Build();
    });

    public async Task<MiniAppPackageUploadResult> SavePackageAsync(
        Guid miniAppId,
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        if (file.Length <= 0)
            throw new InvalidOperationException("Plugin package cannot be empty.");
        if (file.Length > MiniAppStorageConfiguration.MaxPackageFileSizeBytes)
            throw new InvalidOperationException("Plugin package is too large. Maximum allowed is 5242880 bytes.");

        var contentType = file.ContentType?.Split(';', 2)[0].Trim();
        var hasZipExtension = string.Equals(Path.GetExtension(file.FileName), ".zip", StringComparison.OrdinalIgnoreCase);
        var isZipContentType = string.Equals(contentType, "application/zip", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(contentType, "application/x-zip-compressed", StringComparison.OrdinalIgnoreCase);
        if (!hasZipExtension && !isZipContentType)
            throw new InvalidOperationException("Only ZIP plugin packages are supported.");

        await using var input = file.OpenReadStream();
        await using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > MiniAppStorageConfiguration.MaxPackageFileSizeBytes)
            throw new InvalidOperationException("Plugin package is too large. Maximum allowed is 5242880 bytes.");

        ValidatePackage(buffer);
        var sha256 = Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();

        var prefix = (_config.KeyPrefix ?? string.Empty).Trim('/');
        var key = string.IsNullOrWhiteSpace(prefix)
            ? $"{miniAppId}/{Guid.NewGuid():N}.zip"
            : $"{prefix}/{miniAppId}/{Guid.NewGuid():N}.zip";

        buffer.Position = 0;
        await _s3.Value.PutObjectAsync(new PutObjectArgs()
            .WithBucket(_config.S3.Bucket)
            .WithObject(key)
            .WithStreamData(buffer)
            .WithObjectSize(buffer.Length)
            .WithContentType("application/zip"), cancellationToken);

        return new MiniAppPackageUploadResult(
            Key: key,
            Url: BuildPublicUrl(key),
            FileName: Path.GetFileName(file.FileName),
            ContentType: "application/zip",
            Size: buffer.Length,
            Sha256: sha256);
    }

    private static void ValidatePackage(MemoryStream buffer)
    {
        buffer.Position = 0;
        try
        {
            using var archive = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true);
            var entries = archive.Entries.ToList();
            if (entries.Count == 0)
                throw new InvalidOperationException("Plugin package cannot be empty.");

            foreach (var entry in entries)
            {
                var normalized = entry.FullName.Replace('\\', '/');
                if (normalized.StartsWith('/') || normalized.Split('/').Any(part => part == ".."))
                    throw new InvalidOperationException("Plugin package contains an unsafe path.");
            }

            var manifestEntries = entries.Where(entry =>
                string.Equals(Path.GetFileName(entry.FullName), "manifest.json", StringComparison.OrdinalIgnoreCase)).ToList();
            if (manifestEntries.Count == 0)
                throw new InvalidOperationException("Plugin package must contain manifest.json.");
            if (manifestEntries.Count > 1)
                throw new InvalidOperationException("Plugin package must contain only one manifest.json.");

            using var manifestStream = manifestEntries[0].Open();
            using var document = JsonDocument.Parse(manifestStream);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("id", out _) ||
                !document.RootElement.TryGetProperty("name", out _))
                throw new InvalidOperationException("manifest.json must contain id and name.");
        }
        catch (InvalidDataException)
        {
            throw new InvalidOperationException("The uploaded file is not a valid ZIP package.");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("manifest.json is not valid JSON.");
        }
        finally
        {
            buffer.Position = 0;
        }
    }

    private string? BuildPublicUrl(string key)
    {
        if (string.IsNullOrWhiteSpace(_config.S3.PublicBaseUrl))
            return null;

        var baseUri = new Uri(_config.S3.PublicBaseUrl.EndsWith('/')
            ? _config.S3.PublicBaseUrl
            : $"{_config.S3.PublicBaseUrl}/");
        return new Uri(baseUri, key).ToString();
    }
}
