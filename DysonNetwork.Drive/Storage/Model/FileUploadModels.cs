using DysonNetwork.Drive.Storage;
using NodaTime;

namespace DysonNetwork.Drive.Storage.Model
{
    public class CreateUploadTaskRequest
    {
        public string Hash { get; set; } = null!;
        public string FileName { get; set; } = null!;
        public long FileSize { get; set; }
        public string ContentType { get; set; } = null!;
        public string PoolId { get; set; } = null!;
        public string? BundleId { get; set; }
        public string? EncryptPassword { get; set; }
        public Instant? ExpiredAt { get; set; }
        public long? ChunkSize { get; set; }
    }

    public class CreateUploadTaskResponse
    {
        public bool FileExists { get; set; }
        public CloudFile? File { get; set; }
        public string? TaskId { get; set; }
        public long? ChunkSize { get; set; }
        public int? ChunksCount { get; set; }
    }
    
    internal class UploadTask
    {
        public string TaskId { get; set; } = null!;
        public string FileName { get; set; } = null!;
        public long FileSize { get; set; }
        public string ContentType { get; set; } = null!;
        public long ChunkSize { get; set; }
        public int ChunksCount { get; set; }
        public string PoolId { get; set; } = null!;
        public string? BundleId { get; set; }
        public string? EncryptPassword { get; set; }
        public Instant? ExpiredAt { get; set; }
        public string Hash { get; set; } = null!;
    }
}
