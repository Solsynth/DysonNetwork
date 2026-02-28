using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Messager.Chat.Voice;

public class ChatVoiceS3Configuration
{
    public string? ServiceUrl { get; set; }
    public string Region { get; set; } = "us-east-1";
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string Bucket { get; set; } = string.Empty;
    public bool ForcePathStyle { get; set; } = true;
}

public class ChatVoiceConfiguration
{
    public string KeyPrefix { get; set; } = "voice";
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;
    public int RetentionDays { get; set; } = 30;
    public string CleanupCron { get; set; } = "0 15 * * * ?";
    public ChatVoiceS3Configuration S3 { get; set; } = new();
}

public class VoiceClipReadResult
{
    public Stream Stream { get; set; } = null!;
    public long? ContentLength { get; set; }
}

public class ChatVoiceService(
    AppDatabase db,
    IConfiguration configuration,
    ILogger<ChatVoiceService> logger
)
{
    private static readonly Dictionary<string, string> MimeToExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        ["audio/webm"] = ".webm",
        ["audio/ogg"] = ".ogg",
        ["audio/mpeg"] = ".mp3",
        ["audio/mp4"] = ".m4a",
        ["audio/wav"] = ".wav",
        ["audio/x-wav"] = ".wav",
        ["audio/aac"] = ".aac",
        ["audio/flac"] = ".flac"
    };

    private readonly ChatVoiceConfiguration _config =
        configuration.GetSection("VoiceMessages").Get<ChatVoiceConfiguration>() ?? new ChatVoiceConfiguration();

    private readonly Lazy<IAmazonS3> _s3 = new(() =>
    {
        var s3Config = configuration.GetSection("VoiceMessages").Get<ChatVoiceConfiguration>()?.S3 ?? new ChatVoiceS3Configuration();

        if (string.IsNullOrWhiteSpace(s3Config.Bucket))
            throw new InvalidOperationException("VoiceMessages:S3:Bucket is required.");
        if (string.IsNullOrWhiteSpace(s3Config.AccessKey) || string.IsNullOrWhiteSpace(s3Config.SecretKey))
            throw new InvalidOperationException("VoiceMessages:S3 credentials are required.");

        var clientConfig = new AmazonS3Config
        {
            ForcePathStyle = s3Config.ForcePathStyle
        };

        if (!string.IsNullOrWhiteSpace(s3Config.ServiceUrl))
            clientConfig.ServiceURL = s3Config.ServiceUrl;
        else
            clientConfig.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(s3Config.Region);

        var credentials = new BasicAWSCredentials(s3Config.AccessKey, s3Config.SecretKey);
        return new AmazonS3Client(credentials, clientConfig);
    });

    private string GetBucket() => _config.S3.Bucket;

    private static string ResolveExtension(string mimeType, string originalFileName)
    {
        if (MimeToExtension.TryGetValue(mimeType, out var ext))
            return ext;

        var fallback = Path.GetExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(fallback) || fallback.Length > 10)
            return ".bin";

        return fallback.ToLowerInvariant();
    }

    private string BuildObjectKey(Guid roomId, Guid clipId, string extension)
    {
        var prefix = _config.KeyPrefix.Trim('/');
        return $"{prefix}/{roomId}/{clipId}{extension}";
    }

    public int RetentionDays => Math.Max(1, _config.RetentionDays);
    public string CleanupCron => string.IsNullOrWhiteSpace(_config.CleanupCron) ? "0 15 * * * ?" : _config.CleanupCron;

    public async Task<SnChatVoiceClip> SaveVoiceClipAsync(
        Guid roomId,
        SnChatMember sender,
        IFormFile file,
        int? durationMs
    )
    {
        if (file.Length <= 0)
            throw new InvalidOperationException("Voice file cannot be empty.");

        if (file.Length > _config.MaxFileSizeBytes)
            throw new InvalidOperationException(
                $"Voice file is too large. Maximum allowed is {_config.MaxFileSizeBytes} bytes.");

        if (string.IsNullOrWhiteSpace(file.ContentType) ||
            !file.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only audio files are supported for voice messages.");

        var extension = ResolveExtension(file.ContentType, file.FileName);
        var clipId = Guid.NewGuid();
        var objectKey = BuildObjectKey(roomId, clipId, extension);

        await using (var input = file.OpenReadStream())
        {
            await _s3.Value.PutObjectAsync(new PutObjectRequest
            {
                BucketName = GetBucket(),
                Key = objectKey,
                InputStream = input,
                AutoCloseStream = false,
                ContentType = file.ContentType
            });
        }

        var clip = new SnChatVoiceClip
        {
            Id = clipId,
            ChatRoomId = roomId,
            SenderId = sender.Id,
            MimeType = file.ContentType,
            StoragePath = objectKey,
            OriginalFileName = file.FileName,
            Size = file.Length,
            DurationMs = durationMs
        };

        try
        {
            db.ChatVoiceClips.Add(clip);
            await db.SaveChangesAsync();
            return clip;
        }
        catch
        {
            await DeleteVoiceObjectByKeyAsync(objectKey);
            throw;
        }
    }

    public async Task<SnChatVoiceClip?> GetVoiceClipAsync(Guid roomId, Guid voiceId)
    {
        return await db.ChatVoiceClips
            .Where(v => v.Id == voiceId && v.ChatRoomId == roomId)
            .FirstOrDefaultAsync();
    }

    public async Task<VoiceClipReadResult?> OpenVoiceClipAsync(SnChatVoiceClip clip)
    {
        try
        {
            var response = await _s3.Value.GetObjectAsync(new GetObjectRequest
            {
                BucketName = GetBucket(),
                Key = clip.StoragePath
            });

            return new VoiceClipReadResult
            {
                Stream = response.ResponseStream,
                ContentLength = response.Headers.ContentLength
            };
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound || ex.ErrorCode == "NoSuchKey")
        {
            logger.LogWarning("Voice object missing in storage. clipId={ClipId}, key={Key}", clip.Id, clip.StoragePath);
            return null;
        }
    }

    public async Task DeleteVoiceObjectByKeyAsync(string key)
    {
        try
        {
            await _s3.Value.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = GetBucket(),
                Key = key
            });
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound || ex.ErrorCode == "NoSuchKey")
        {
            // Already gone, ignore.
        }
    }

    public async Task<int> CleanupExpiredVoiceClipsAsync(CancellationToken cancellationToken = default)
    {
        var threshold = NodaTime.SystemClock.Instance.GetCurrentInstant() - NodaTime.Duration.FromDays(RetentionDays);

        var expired = await db.ChatVoiceClips
            .Where(v => v.CreatedAt < threshold)
            .Take(200)
            .ToListAsync(cancellationToken);

        if (expired.Count == 0)
            return 0;

        foreach (var clip in expired)
            await DeleteVoiceObjectByKeyAsync(clip.StoragePath);

        db.ChatVoiceClips.RemoveRange(expired);
        await db.SaveChangesAsync(cancellationToken);

        return expired.Count;
    }
}
