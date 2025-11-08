using DysonNetwork.Shared.Models;
using NodaTime;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DysonNetwork.Drive.Storage.Model;

public class CreateUploadTaskRequest
{
    public string Hash { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public long FileSize { get; set; }
    public string ContentType { get; set; } = null!;
    public Guid? PoolId { get; set; } = null!;
    public Guid? BundleId { get; set; }
    public string? EncryptPassword { get; set; }
    public Instant? ExpiredAt { get; set; }
    public long? ChunkSize { get; set; }
}

public class CreateUploadTaskResponse
{
    public bool FileExists { get; set; }
    public SnCloudFile? File { get; set; }
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
    public Guid PoolId { get; set; }
    public Guid? BundleId { get; set; }
    public string? EncryptPassword { get; set; }
    public Instant? ExpiredAt { get; set; }
    public string Hash { get; set; } = null!;
}

public class PersistentTask : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(64)]
    public string TaskId { get; set; } = null!;

    [MaxLength(256)]
    public string Name { get; set; } = null!;

    [MaxLength(1024)]
    public string? Description { get; set; }

    public TaskType Type { get; set; }

    public TaskStatus Status { get; set; } = TaskStatus.InProgress;

    public Guid AccountId { get; set; }

    // Progress tracking (0-100)
    public double Progress { get; set; }

    // Task-specific parameters stored as JSON
    [Column(TypeName = "jsonb")]
    public Dictionary<string, object?> Parameters { get; set; } = new();

    // Task results/output stored as JSON
    [Column(TypeName = "jsonb")]
    public Dictionary<string, object?> Results { get; set; } = new();

    [MaxLength(1024)]
    public string? ErrorMessage { get; set; }

    public Instant? StartedAt { get; set; }
    public Instant? CompletedAt { get; set; }
    public Instant? ExpiredAt { get; set; }

    public Instant LastActivity { get; set; }

    // Priority (higher = more important)
    public int Priority { get; set; } = 0;

    // Estimated duration in seconds
    public long? EstimatedDurationSeconds { get; set; }
}

// Backward compatibility - UploadTask inherits from PersistentTask
public class PersistentUploadTask : PersistentTask
{
    public PersistentUploadTask()
    {
        Type = TaskType.FileUpload;
        Name = "File Upload";
    }

    [MaxLength(256)]
    public string FileName
    {
        get => Parameters.GetValueOrDefault("fileName") as string ?? string.Empty;
        set => Parameters["fileName"] = value;
    }

    public long FileSize
    {
        get => Convert.ToInt64(Parameters.GetValueOrDefault("fileSize") ?? 0L);
        set => Parameters["fileSize"] = value;
    }

    [MaxLength(128)]
    public string ContentType
    {
        get => Parameters.GetValueOrDefault("contentType") as string ?? string.Empty;
        set => Parameters["contentType"] = value;
    }

    public long ChunkSize
    {
        get => Convert.ToInt64(Parameters.GetValueOrDefault("chunkSize") ?? 5242880L);
        set => Parameters["chunkSize"] = value;
    }

    public int ChunksCount
    {
        get => Convert.ToInt32(Parameters.GetValueOrDefault("chunksCount") ?? 0);
        set => Parameters["chunksCount"] = value;
    }

    public int ChunksUploaded
    {
        get => Convert.ToInt32(Parameters.GetValueOrDefault("chunksUploaded") ?? 0);
        set
        {
            Parameters["chunksUploaded"] = value;
            Progress = ChunksCount > 0 ? (double)value / ChunksCount * 100 : 0;
        }
    }

    public Guid PoolId
    {
        get => Guid.Parse(Parameters.GetValueOrDefault("poolId") as string ?? Guid.Empty.ToString());
        set => Parameters["poolId"] = value.ToString();
    }

    public Guid? BundleId
    {
        get
        {
            var bundleIdStr = Parameters.GetValueOrDefault("bundleId") as string;
            return string.IsNullOrEmpty(bundleIdStr) ? null : Guid.Parse(bundleIdStr);
        }
        set => Parameters["bundleId"] = value?.ToString();
    }

    [MaxLength(256)]
    public string? EncryptPassword
    {
        get => Parameters.GetValueOrDefault("encryptPassword") as string;
        set => Parameters["encryptPassword"] = value;
    }

    public string Hash
    {
        get => Parameters.GetValueOrDefault("hash") as string ?? string.Empty;
        set => Parameters["hash"] = value;
    }

    // JSON array of uploaded chunk indices for resumability
    public List<int> UploadedChunks
    {
        get => Parameters.GetValueOrDefault("uploadedChunks") as List<int> ?? [];
        set => Parameters["uploadedChunks"] = value;
    }
}

public enum TaskType
{
    FileUpload,
    FileMove,
    FileCompress,
    FileDecompress,
    FileEncrypt,
    FileDecrypt,
    BulkOperation,
    StorageMigration,
    FileConversion,
    Custom
}

public enum TaskStatus
{
    Pending,
    InProgress,
    Paused,
    Completed,
    Failed,
    Cancelled,
    Expired
}

// File Move Task
public class FileMoveTask : PersistentTask
{
    public FileMoveTask()
    {
        Type = TaskType.FileMove;
        Name = "Move Files";
    }

    public List<string> FileIds
    {
        get => Parameters.GetValueOrDefault("fileIds") as List<string> ?? [];
        set => Parameters["fileIds"] = value;
    }

    public Guid TargetPoolId
    {
        get => Guid.Parse(Parameters.GetValueOrDefault("targetPoolId") as string ?? Guid.Empty.ToString());
        set => Parameters["targetPoolId"] = value.ToString();
    }

    public Guid? TargetBundleId
    {
        get
        {
            var bundleIdStr = Parameters.GetValueOrDefault("targetBundleId") as string;
            return string.IsNullOrEmpty(bundleIdStr) ? null : Guid.Parse(bundleIdStr);
        }
        set => Parameters["targetBundleId"] = value?.ToString();
    }

    public int FilesProcessed
    {
        get => Convert.ToInt32(Parameters.GetValueOrDefault("filesProcessed") ?? 0);
        set
        {
            Parameters["filesProcessed"] = value;
            Progress = FileIds.Count > 0 ? (double)value / FileIds.Count * 100 : 0;
        }
    }
}

// File Compression Task
public class FileCompressTask : PersistentTask
{
    public FileCompressTask()
    {
        Type = TaskType.FileCompress;
        Name = "Compress Files";
    }

    public List<string> FileIds
    {
        get => Parameters.GetValueOrDefault("fileIds") as List<string> ?? [];
        set => Parameters["fileIds"] = value;
    }

    [MaxLength(32)]
    public string CompressionFormat
    {
        get => Parameters.GetValueOrDefault("compressionFormat") as string ?? "zip";
        set => Parameters["compressionFormat"] = value;
    }

    public int CompressionLevel
    {
        get => Convert.ToInt32(Parameters.GetValueOrDefault("compressionLevel") ?? 6);
        set => Parameters["compressionLevel"] = value;
    }

    public string? OutputFileName
    {
        get => Parameters.GetValueOrDefault("outputFileName") as string;
        set => Parameters["outputFileName"] = value;
    }

    public int FilesProcessed
    {
        get => Convert.ToInt32(Parameters.GetValueOrDefault("filesProcessed") ?? 0);
        set
        {
            Parameters["filesProcessed"] = value;
            Progress = FileIds.Count > 0 ? (double)value / FileIds.Count * 100 : 0;
        }
    }

    public string? ResultFileId
    {
        get => Results.GetValueOrDefault("resultFileId") as string;
        set => Results["resultFileId"] = value;
    }
}

// Bulk Operation Task
public class BulkOperationTask : PersistentTask
{
    public BulkOperationTask()
    {
        Type = TaskType.BulkOperation;
        Name = "Bulk Operation";
    }

    [MaxLength(128)]
    public string OperationType
    {
        get => Parameters.GetValueOrDefault("operationType") as string ?? string.Empty;
        set => Parameters["operationType"] = value;
    }

    public List<string> TargetIds
    {
        get => Parameters.GetValueOrDefault("targetIds") as List<string> ?? [];
        set => Parameters["targetIds"] = value;
    }

    [Column(TypeName = "jsonb")]
    public Dictionary<string, object?> OperationParameters
    {
        get => Parameters.GetValueOrDefault("operationParameters") as Dictionary<string, object?> ?? new();
        set => Parameters["operationParameters"] = value;
    }

    public int ItemsProcessed
    {
        get => Convert.ToInt32(Parameters.GetValueOrDefault("itemsProcessed") ?? 0);
        set
        {
            Parameters["itemsProcessed"] = value;
            Progress = TargetIds.Count > 0 ? (double)value / TargetIds.Count * 100 : 0;
        }
    }

    [Column(TypeName = "jsonb")]
    public Dictionary<string, object?> OperationResults
    {
        get => Results.GetValueOrDefault("operationResults") as Dictionary<string, object?> ?? new();
        set => Results["operationResults"] = value;
    }
}

// Storage Migration Task
public class StorageMigrationTask : PersistentTask
{
    public StorageMigrationTask()
    {
        Type = TaskType.StorageMigration;
        Name = "Storage Migration";
    }

    public Guid SourcePoolId
    {
        get => Guid.Parse(Parameters.GetValueOrDefault("sourcePoolId") as string ?? Guid.Empty.ToString());
        set => Parameters["sourcePoolId"] = value.ToString();
    }

    public Guid TargetPoolId
    {
        get => Guid.Parse(Parameters.GetValueOrDefault("targetPoolId") as string ?? Guid.Empty.ToString());
        set => Parameters["targetPoolId"] = value.ToString();
    }

    public List<string> FileIds
    {
        get => Parameters.GetValueOrDefault("fileIds") as List<string> ?? [];
        set => Parameters["fileIds"] = value;
    }

    public bool PreserveOriginals
    {
        get => Convert.ToBoolean(Parameters.GetValueOrDefault("preserveOriginals") ?? true);
        set => Parameters["preserveOriginals"] = value;
    }

    public long TotalBytesToTransfer
    {
        get => Convert.ToInt64(Parameters.GetValueOrDefault("totalBytesToTransfer") ?? 0L);
        set => Parameters["totalBytesToTransfer"] = value;
    }

    public long BytesTransferred
    {
        get => Convert.ToInt64(Parameters.GetValueOrDefault("bytesTransferred") ?? 0L);
        set
        {
            Parameters["bytesTransferred"] = value;
            Progress = TotalBytesToTransfer > 0 ? (double)value / TotalBytesToTransfer * 100 : 0;
        }
    }

    public int FilesMigrated
    {
        get => Convert.ToInt32(Parameters.GetValueOrDefault("filesMigrated") ?? 0);
        set => Parameters["filesMigrated"] = value;
    }
}

// Legacy enum for backward compatibility
public enum UploadTaskStatus
{
    InProgress = TaskStatus.InProgress,
    Completed = TaskStatus.Completed,
    Failed = TaskStatus.Failed,
    Expired = TaskStatus.Expired
}
