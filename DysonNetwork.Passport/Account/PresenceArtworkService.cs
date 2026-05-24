using System.Security.Cryptography;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using NodaTime;

namespace DysonNetwork.Passport.Account;

public class PresenceArtworkS3Configuration
{
    public string? ServiceUrl { get; set; }
    public string? Endpoint { get; set; }
    public string Region { get; set; } = "us-east-1";
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string Bucket { get; set; } = string.Empty;
    public bool ForcePathStyle { get; set; } = true;
    public bool EnableSsl { get; set; } = true;
}

public class PresenceArtworkConfiguration
{
    public string KeyPrefix { get; set; } = "presence-artwork";
    public long MaxFileSizeBytes { get; set; } = 1024 * 1024;
    public int RetentionDays { get; set; } = 30;
    public string CleanupCron { get; set; } = PresenceArtworkService.DefaultCleanupCron;
    public PresenceArtworkS3Configuration S3 { get; set; } = new();
}

public class PresenceArtworkUploadResult
{
    public required SnPresenceArtwork Artwork { get; init; }
    public required bool Created { get; init; }
}

public class PresenceArtworkReadResult
{
    public required Stream Stream { get; init; }
    public long? ContentLength { get; init; }
}

public class PresenceArtworkService(
    AppDatabase db,
    IConfiguration configuration,
    ILogger<PresenceArtworkService> logger
)
{
    public const string DefaultCleanupCron = "0 20 * * * ?";
    private const string HashPrefix = "sha256:";

    private static readonly Dictionary<string, string> MimeToExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/gif"] = ".gif",
        ["image/webp"] = ".webp",
        ["image/svg+xml"] = ".svg",
        ["image/avif"] = ".avif"
    };

    private readonly PresenceArtworkConfiguration _config =
        configuration.GetSection("PresenceArtwork").Get<PresenceArtworkConfiguration>()
        ?? new PresenceArtworkConfiguration();

    private readonly Lazy<IMinioClient> _s3 = new(() =>
    {
        var s3Config = configuration.GetSection("PresenceArtwork").Get<PresenceArtworkConfiguration>()?.S3
            ?? new PresenceArtworkS3Configuration();

        if (string.IsNullOrWhiteSpace(s3Config.Bucket))
            throw new InvalidOperationException("PresenceArtwork:S3:Bucket is required.");
        if (string.IsNullOrWhiteSpace(s3Config.AccessKey) || string.IsNullOrWhiteSpace(s3Config.SecretKey))
            throw new InvalidOperationException("PresenceArtwork:S3 credentials are required.");

        var endpoint = s3Config.Endpoint;
        var useSsl = s3Config.EnableSsl;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            if (string.IsNullOrWhiteSpace(s3Config.ServiceUrl))
                throw new InvalidOperationException("PresenceArtwork:S3:Endpoint or ServiceUrl is required.");

            var uri = new Uri(s3Config.ServiceUrl);
            endpoint = uri.Authority;
            useSsl = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        }

        var client = new MinioClient()
            .WithEndpoint(endpoint)
            .WithRegion(s3Config.Region)
            .WithCredentials(s3Config.AccessKey, s3Config.SecretKey);

        if (useSsl)
            client = client.WithSSL();

        return client.Build();
    });

    public static bool IsArtworkReference(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.StartsWith(HashPrefix, StringComparison.OrdinalIgnoreCase);

    public int RetentionDays => Math.Max(1, _config.RetentionDays);
    public string CleanupCron => string.IsNullOrWhiteSpace(_config.CleanupCron) ? DefaultCleanupCron : _config.CleanupCron;

    private string GetBucket() => _config.S3.Bucket;

    private static string NormalizeHash(string hash)
    {
        var value = hash.Trim();
        if (!value.StartsWith(HashPrefix, StringComparison.OrdinalIgnoreCase))
            value = HashPrefix + value;

        var digest = value[HashPrefix.Length..].ToLowerInvariant();
        if (digest.Length != 64 || !digest.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Invalid artwork hash.");

        return HashPrefix + digest;
    }

    private static string ResolveExtension(string mimeType, string originalFileName)
    {
        if (MimeToExtension.TryGetValue(mimeType, out var ext))
            return ext;

        var fallback = Path.GetExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(fallback) || fallback.Length > 10)
            return ".bin";

        return fallback.ToLowerInvariant();
    }

    private string BuildObjectKey(string hash, string extension)
    {
        var prefix = (_config.KeyPrefix ?? string.Empty).Trim('/');
        var digest = hash[HashPrefix.Length..];
        return string.IsNullOrWhiteSpace(prefix)
            ? $"{digest[..2]}/{digest}{extension}"
            : $"{prefix}/{digest[..2]}/{digest}{extension}";
    }

    public async Task<PresenceArtworkUploadResult> SaveArtworkAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        if (file.Length <= 0)
            throw new InvalidOperationException("Artwork file cannot be empty.");
        if (file.Length > _config.MaxFileSizeBytes)
            throw new InvalidOperationException($"Artwork file is too large. Maximum allowed is {_config.MaxFileSizeBytes} bytes.");
        if (string.IsNullOrWhiteSpace(file.ContentType) || !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only image files are supported for presence artwork.");

        await using var input = file.OpenReadStream();
        await using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, cancellationToken);

        var hash = HashPrefix + Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
        var now = SystemClock.Instance.GetCurrentInstant();
        var existing = await db.PresenceArtworks.FirstOrDefaultAsync(a => a.Hash == hash, cancellationToken);
        if (existing is not null)
        {
            existing.LastReferencedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            return new PresenceArtworkUploadResult { Artwork = existing, Created = false };
        }

        var extension = ResolveExtension(file.ContentType, file.FileName);
        var objectKey = BuildObjectKey(hash, extension);
        buffer.Position = 0;

        await _s3.Value.PutObjectAsync(new PutObjectArgs()
            .WithBucket(GetBucket())
            .WithObject(objectKey)
            .WithStreamData(buffer)
            .WithObjectSize(buffer.Length)
            .WithContentType(file.ContentType), cancellationToken);

        var artwork = new SnPresenceArtwork
        {
            Hash = hash,
            MimeType = file.ContentType,
            Size = buffer.Length,
            StoragePath = objectKey,
            LastReferencedAt = now
        };

        try
        {
            db.PresenceArtworks.Add(artwork);
            await db.SaveChangesAsync(cancellationToken);
            return new PresenceArtworkUploadResult { Artwork = artwork, Created = true };
        }
        catch
        {
            await DeleteArtworkObjectByKeyAsync(objectKey, cancellationToken);
            throw;
        }
    }

    public async Task<SnPresenceArtwork?> GetArtworkAsync(string hash, CancellationToken cancellationToken = default)
    {
        hash = NormalizeHash(hash);
        return await db.PresenceArtworks.FirstOrDefaultAsync(a => a.Hash == hash, cancellationToken);
    }

    public async Task<PresenceArtworkReadResult?> OpenArtworkAsync(SnPresenceArtwork artwork, CancellationToken cancellationToken = default)
    {
        try
        {
            var memoryStream = new MemoryStream();
            await _s3.Value.GetObjectAsync(new GetObjectArgs()
                .WithBucket(GetBucket())
                .WithObject(artwork.StoragePath)
                .WithCallbackStream(stream => stream.CopyTo(memoryStream)), cancellationToken);
            memoryStream.Position = 0;
            return new PresenceArtworkReadResult
            {
                Stream = memoryStream,
                ContentLength = artwork.Size > 0 ? artwork.Size : null
            };
        }
        catch (ObjectNotFoundException)
        {
            logger.LogWarning("Presence artwork missing in storage. hash={Hash}, key={Key}", artwork.Hash, artwork.StoragePath);
            return null;
        }
    }

    public async Task TouchArtworkReferenceAsync(string hash, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeHash(hash);
        var artwork = await db.PresenceArtworks.FirstOrDefaultAsync(a => a.Hash == normalized, cancellationToken);
        if (artwork is null)
            throw new KeyNotFoundException("Presence artwork not found.");

        artwork.LastReferencedAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ValidateAndTouchReferencesAsync(IEnumerable<string?> values, CancellationToken cancellationToken = default)
    {
        var hashes = values
            .Where(IsArtworkReference)
            .Select(v => NormalizeHash(v!))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (hashes.Count == 0)
            return;

        var artworks = await db.PresenceArtworks.Where(a => hashes.Contains(a.Hash)).ToListAsync(cancellationToken);
        if (artworks.Count != hashes.Count)
        {
            var existing = artworks.Select(a => a.Hash).ToHashSet(StringComparer.Ordinal);
            var missing = hashes.First(h => !existing.Contains(h));
            throw new KeyNotFoundException($"Presence artwork not found: {missing}");
        }

        var now = SystemClock.Instance.GetCurrentInstant();
        foreach (var artwork in artworks)
            artwork.LastReferencedAt = now;

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteArtworkObjectByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await _s3.Value.RemoveObjectAsync(new RemoveObjectArgs()
                .WithBucket(GetBucket())
                .WithObject(key), cancellationToken);
        }
        catch (ObjectNotFoundException)
        {
        }
    }

    public async Task<int> CleanupExpiredArtworksAsync(CancellationToken cancellationToken = default)
    {
        var threshold = SystemClock.Instance.GetCurrentInstant() - Duration.FromDays(RetentionDays);
        var expired = await db.PresenceArtworks
            .Where(a => a.LastReferencedAt < threshold)
            .Take(200)
            .ToListAsync(cancellationToken);

        if (expired.Count == 0)
            return 0;

        foreach (var artwork in expired)
            await DeleteArtworkObjectByKeyAsync(artwork.StoragePath, cancellationToken);

        db.PresenceArtworks.RemoveRange(expired);
        await db.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }
}
