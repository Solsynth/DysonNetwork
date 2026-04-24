using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Insight.Thought.Voice;

public class ThinkingVoiceConfiguration
{
    public string TempDirectory { get; set; } = "./storage/insight-voice";
    public long MaxFileSizeBytes { get; set; } = 15 * 1024 * 1024;
    public int RetentionHours { get; set; } = 24;
}

public class ThinkingVoiceReadResult
{
    public Stream Stream { get; set; } = null!;
    public long? ContentLength { get; set; }
}

public class ThinkingVoiceService(
    AppDatabase db,
    IConfiguration configuration,
    ILogger<ThinkingVoiceService> logger
)
{
    private readonly ThinkingVoiceConfiguration _config =
        configuration.GetSection("Thinking:VoiceMessages").Get<ThinkingVoiceConfiguration>() ??
        new ThinkingVoiceConfiguration();

    private static readonly HashSet<string> AllowedMimeTypes =
    [
        "audio/webm",
        "audio/ogg",
        "audio/mpeg",
        "audio/mp4",
        "audio/wav",
        "audio/x-wav",
        "audio/aac",
        "audio/flac"
    ];

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

    private static string ResolveExtension(string mimeType, string fileName)
    {
        if (MimeToExtension.TryGetValue(mimeType, out var ext))
        {
            return ext;
        }

        var fallback = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(fallback) || fallback.Length > 10)
        {
            return ".bin";
        }

        return fallback.ToLowerInvariant();
    }

    private string BuildStoragePath(Guid clipId, string extension)
    {
        var now = SystemClock.Instance.GetCurrentInstant().ToDateTimeUtc();
        var relativePath = Path.Combine(
            now.ToString("yyyy"),
            now.ToString("MM"),
            now.ToString("dd"),
            clipId + extension);
        return Path.Combine(_config.TempDirectory, relativePath);
    }

    private static string BuildAccessToken() => Guid.NewGuid().ToString("N");

    public async Task<SnThinkingVoiceClip> SaveVoiceClipAsync(
        Guid accountId,
        Guid? sequenceId,
        IFormFile file,
        int? durationMs,
        CancellationToken cancellationToken = default)
    {
        if (file.Length <= 0)
        {
            throw new InvalidOperationException("Voice file cannot be empty.");
        }

        if (file.Length > _config.MaxFileSizeBytes)
        {
            throw new InvalidOperationException($"Voice file is too large. Maximum allowed is {_config.MaxFileSizeBytes} bytes.");
        }

        if (string.IsNullOrWhiteSpace(file.ContentType) || !AllowedMimeTypes.Contains(file.ContentType))
        {
            throw new InvalidOperationException("Only supported audio formats are allowed for voice messages.");
        }

        var clipId = Guid.NewGuid();
        var ext = ResolveExtension(file.ContentType, file.FileName);
        var storagePath = BuildStoragePath(clipId, ext);
        var absolutePath = Path.GetFullPath(storagePath);
        var parentDir = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrWhiteSpace(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        await using (var source = file.OpenReadStream())
        await using (var target = File.Create(absolutePath))
        {
            await source.CopyToAsync(target, cancellationToken);
        }

        var now = SystemClock.Instance.GetCurrentInstant();
        var clip = new SnThinkingVoiceClip
        {
            Id = clipId,
            AccountId = accountId,
            SequenceId = sequenceId,
            MimeType = file.ContentType,
            StoragePath = absolutePath,
            OriginalFileName = file.FileName,
            Size = file.Length,
            DurationMs = durationMs,
            AccessToken = BuildAccessToken(),
            ExpiresAt = now + Duration.FromHours(Math.Max(1, _config.RetentionHours))
        };

        try
        {
            db.ThinkingVoiceClips.Add(clip);
            await db.SaveChangesAsync(cancellationToken);
            return clip;
        }
        catch
        {
            try
            {
                if (File.Exists(absolutePath))
                {
                    File.Delete(absolutePath);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete temp voice file after DB error: {Path}", absolutePath);
            }

            throw;
        }
    }

    public async Task<SnThinkingVoiceClip?> GetVoiceClipAsync(Guid clipId, CancellationToken cancellationToken = default)
    {
        return await db.ThinkingVoiceClips.FirstOrDefaultAsync(v => v.Id == clipId, cancellationToken);
    }

    public async Task<List<SnThinkingVoiceClip>> GetAccessibleVoiceClipsAsync(
        Guid accountId,
        IEnumerable<Guid> clipIds,
        CancellationToken cancellationToken = default)
    {
        var ids = clipIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var now = SystemClock.Instance.GetCurrentInstant();
        return await db.ThinkingVoiceClips
            .Where(v => v.AccountId == accountId)
            .Where(v => ids.Contains(v.Id))
            .Where(v => v.ExpiresAt > now)
            .ToListAsync(cancellationToken);
    }

    public string BuildStreamUrl(Guid clipId, string accessToken)
    {
        return $"/api/thought/voice/{clipId}?token={accessToken}";
    }

    public SnCloudFileReferenceObject ToFileReference(SnThinkingVoiceClip clip)
    {
        return new SnCloudFileReferenceObject
        {
            Id = clip.Id.ToString(),
            Name = string.IsNullOrWhiteSpace(clip.OriginalFileName) ? $"voice-{clip.Id}" : clip.OriginalFileName,
            FileMeta = new Dictionary<string, object?>
            {
                ["voice_clip_id"] = clip.Id,
                ["expires_at"] = clip.ExpiresAt.ToUnixTimeMilliseconds(),
                ["duration_ms"] = clip.DurationMs
            },
            UserMeta = [],
            MimeType = clip.MimeType,
            Size = clip.Size,
            Url = BuildStreamUrl(clip.Id, clip.AccessToken)
        };
    }

    public async Task<ThinkingVoiceReadResult?> OpenVoiceClipAsync(
        SnThinkingVoiceClip clip,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(clip.StoragePath))
        {
            return null;
        }

        var stream = new FileStream(clip.StoragePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await Task.FromResult(new ThinkingVoiceReadResult
        {
            Stream = stream,
            ContentLength = clip.Size > 0 ? clip.Size : null
        });
    }

    public async Task<int> CleanupExpiredVoiceClipsAsync(CancellationToken cancellationToken = default)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var expired = await db.ThinkingVoiceClips
            .Where(v => v.ExpiresAt <= now)
            .Take(200)
            .ToListAsync(cancellationToken);

        if (expired.Count == 0)
        {
            return 0;
        }

        foreach (var clip in expired)
        {
            try
            {
                if (File.Exists(clip.StoragePath))
                {
                    File.Delete(clip.StoragePath);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete expired voice file at {Path}", clip.StoragePath);
            }
        }

        db.ThinkingVoiceClips.RemoveRange(expired);
        await db.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }
}
