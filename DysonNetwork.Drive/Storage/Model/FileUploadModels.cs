using DysonNetwork.Shared.Models;
using NodaTime;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace DysonNetwork.Drive.Storage.Model;

public static class ParameterHelper
{
    public static T GetParameterValue<T>(Dictionary<string, object?> parameters, string key, T defaultValue = default!)
    {
        if (!parameters.TryGetValue(key, out var value) || value == null)
        {
            return defaultValue;
        }

        // If the value is already the correct type, return it directly
        if (value is T typedValue)
        {
            return typedValue;
        }

        // Handle JsonElement by deserializing to the target type
        if (value is JsonElement jsonElement)
        {
            try
            {
                return jsonElement.Deserialize<T>() ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        // Handle numeric conversions
        if (typeof(T) == typeof(int))
        {
            if (value is long longValue)
            {
                return (T)(object)(int)longValue;
            }
            if (value is string stringValue && int.TryParse(stringValue, out int intValue))
            {
                return (T)(object)intValue;
            }
        }
        else if (typeof(T) == typeof(long))
        {
            if (value is int intValue)
            {
                return (T)(object)(long)intValue;
            }
            if (value is string stringValue && long.TryParse(stringValue, out long longValue))
            {
                return (T)(object)longValue;
            }
        }
        else if (typeof(T) == typeof(string))
        {
            return (T)(object)value.ToString()!;
        }
        else if (typeof(T) == typeof(bool))
        {
            if (value is string stringValue && bool.TryParse(stringValue, out bool boolValue))
            {
                return (T)(object)boolValue;
            }
        }

        // Fallback to Convert.ChangeType for other types
        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return defaultValue;
        }
    }

    public static List<T> GetParameterList<T>(Dictionary<string, object?> parameters, string key, List<T> defaultValue = null!)
    {
        defaultValue ??= [];

        if (!parameters.TryGetValue(key, out var value) || value == null)
        {
            return defaultValue;
        }

        // If the value is already the correct type, return it directly
        if (value is List<T> typedList)
        {
            return typedList;
        }

        // Handle JsonElement by deserializing to the target type
        if (value is JsonElement jsonElement)
        {
            try
            {
                return jsonElement.Deserialize<List<T>>() ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        return defaultValue;
    }
}

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
        get => ParameterHelper.GetParameterValue(Parameters, "file_name", string.Empty);
        set => Parameters["file_name"] = value;
    }

    public long FileSize
    {
        get => ParameterHelper.GetParameterValue(Parameters, "file_size", 0L);
        set => Parameters["file_size"] = value;
    }

    [MaxLength(128)]
    public string ContentType
    {
        get => ParameterHelper.GetParameterValue(Parameters, "content_type", string.Empty);
        set => Parameters["content_type"] = value;
    }

    public long ChunkSize
    {
        get => ParameterHelper.GetParameterValue(Parameters, "chunk_size", 5242880L);
        set => Parameters["chunk_size"] = value;
    }

    public int ChunksCount
    {
        get => ParameterHelper.GetParameterValue(Parameters, "chunks_count", 0);
        set => Parameters["chunks_count"] = value;
    }

    public int ChunksUploaded
    {
        get => ParameterHelper.GetParameterValue(Parameters, "chunks_uploaded", 0);
        set
        {
            Parameters["chunks_uploaded"] = value;
            Progress = ChunksCount > 0 ? (double)value / ChunksCount * 100 : 0;
        }
    }

    public Guid PoolId
    {
        get
        {
            var poolIdStr = ParameterHelper.GetParameterValue(Parameters, "pool_id", Guid.Empty.ToString());
            return Guid.Parse(poolIdStr);
        }
        set => Parameters["pool_id"] = value.ToString();
    }

    public Guid? BundleId
    {
        get
        {
            var bundleIdStr = ParameterHelper.GetParameterValue(Parameters, "bundle_id", string.Empty);
            return string.IsNullOrEmpty(bundleIdStr) ? null : Guid.Parse(bundleIdStr);
        }
        set => Parameters["bundle_id"] = value?.ToString();
    }

    [MaxLength(256)]
    public string? EncryptPassword
    {
        get => ParameterHelper.GetParameterValue<string?>(Parameters, "encrypt_password", null);
        set => Parameters["encrypt_password"] = value;
    }

    public string Hash
    {
        get => ParameterHelper.GetParameterValue(Parameters, "hash", string.Empty);
        set => Parameters["hash"] = value;
    }

    // JSON array of uploaded chunk indices for resumability
    public List<int> UploadedChunks
    {
        get => ParameterHelper.GetParameterList<int>(Parameters, "uploaded_chunks", []);
        set => Parameters["uploaded_chunks"] = value;
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
        get => ParameterHelper.GetParameterList<string>(Parameters, "file_ids", []);
        set => Parameters["file_ids"] = value;
    }

    public Guid TargetPoolId
    {
        get
        {
            var targetPoolIdStr = ParameterHelper.GetParameterValue(Parameters, "target_pool_id", Guid.Empty.ToString());
            return Guid.Parse(targetPoolIdStr);
        }
        set => Parameters["target_pool_id"] = value.ToString();
    }

    public Guid? TargetBundleId
    {
        get
        {
            var bundleIdStr = ParameterHelper.GetParameterValue(Parameters, "target_bundle_id", string.Empty);
            return string.IsNullOrEmpty(bundleIdStr) ? null : Guid.Parse(bundleIdStr);
        }
        set => Parameters["target_bundle_id"] = value?.ToString();
    }

    public int FilesProcessed
    {
        get => ParameterHelper.GetParameterValue(Parameters, "files_processed", 0);
        set
        {
            Parameters["files_processed"] = value;
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
        get => ParameterHelper.GetParameterList<string>(Parameters, "file_ids", []);
        set => Parameters["file_ids"] = value;
    }

    [MaxLength(32)]
    public string CompressionFormat
    {
        get => ParameterHelper.GetParameterValue(Parameters, "compression_format", "zip");
        set => Parameters["compression_format"] = value;
    }

    public int CompressionLevel
    {
        get => ParameterHelper.GetParameterValue(Parameters, "compression_level", 6);
        set => Parameters["compression_level"] = value;
    }

    public string? OutputFileName
    {
        get => ParameterHelper.GetParameterValue<string?>(Parameters, "output_file_name", null);
        set => Parameters["output_file_name"] = value;
    }

    public int FilesProcessed
    {
        get => ParameterHelper.GetParameterValue(Parameters, "files_processed", 0);
        set
        {
            Parameters["files_processed"] = value;
            Progress = FileIds.Count > 0 ? (double)value / FileIds.Count * 100 : 0;
        }
    }

    public string? ResultFileId
    {
        get => ParameterHelper.GetParameterValue<string?>(Results, "result_file_id", null);
        set => Results["result_file_id"] = value;
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
        get => ParameterHelper.GetParameterValue(Parameters, "operation_type", string.Empty);
        set => Parameters["operation_type"] = value;
    }

    public List<string> TargetIds
    {
        get => ParameterHelper.GetParameterList<string>(Parameters, "target_ids", []);
        set => Parameters["target_ids"] = value;
    }

    [Column(TypeName = "jsonb")]
    public Dictionary<string, object?> OperationParameters
    {
        get => ParameterHelper.GetParameterValue(Parameters, "operation_parameters", new Dictionary<string, object?>());
        set => Parameters["operation_parameters"] = value;
    }

    public int ItemsProcessed
    {
        get => ParameterHelper.GetParameterValue(Parameters, "items_processed", 0);
        set
        {
            Parameters["items_processed"] = value;
            Progress = TargetIds.Count > 0 ? (double)value / TargetIds.Count * 100 : 0;
        }
    }

    [Column(TypeName = "jsonb")]
    public Dictionary<string, object?> OperationResults
    {
        get => ParameterHelper.GetParameterValue(Results, "operation_results", new Dictionary<string, object?>());
        set => Results["operation_results"] = value;
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
        get
        {
            var sourcePoolIdStr = ParameterHelper.GetParameterValue(Parameters, "source_pool_id", Guid.Empty.ToString());
            return Guid.Parse(sourcePoolIdStr);
        }
        set => Parameters["source_pool_id"] = value.ToString();
    }

    public Guid TargetPoolId
    {
        get
        {
            var targetPoolIdStr = ParameterHelper.GetParameterValue(Parameters, "target_pool_id", Guid.Empty.ToString());
            return Guid.Parse(targetPoolIdStr);
        }
        set => Parameters["target_pool_id"] = value.ToString();
    }

    public List<string> FileIds
    {
        get => ParameterHelper.GetParameterList<string>(Parameters, "file_ids", []);
        set => Parameters["file_ids"] = value;
    }

    public bool PreserveOriginals
    {
        get => ParameterHelper.GetParameterValue(Parameters, "preserve_originals", true);
        set => Parameters["preserve_originals"] = value;
    }

    public long TotalBytesToTransfer
    {
        get => ParameterHelper.GetParameterValue(Parameters, "total_bytes_to_transfer", 0L);
        set => Parameters["total_bytes_to_transfer"] = value;
    }

    public long BytesTransferred
    {
        get => ParameterHelper.GetParameterValue(Parameters, "bytes_transferred", 0L);
        set
        {
            Parameters["bytes_transferred"] = value;
            Progress = TotalBytesToTransfer > 0 ? (double)value / TotalBytesToTransfer * 100 : 0;
        }
    }

    public int FilesMigrated
    {
        get => ParameterHelper.GetParameterValue(Parameters, "files_migrated", 0);
        set => Parameters["files_migrated"] = value;
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
