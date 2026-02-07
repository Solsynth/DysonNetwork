using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Google.Protobuf.Collections;
using NodaTime;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using DysonNetwork.Shared.Data;

namespace DysonNetwork.Drive.Storage.Model;

// File Upload Task Parameters
public class FileUploadParameters
{
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public long ChunkSize { get; set; } = 5242880L;
    public int ChunksCount { get; set; }
    public int ChunksUploaded { get; set; }
    public Guid PoolId { get; set; }
    public Guid? BundleId { get; set; }
    public string? EncryptPassword { get; set; }
    public string Hash { get; set; } = string.Empty;
    public List<int> UploadedChunks { get; set; } = [];
    public string? Path { get; set; }
}

// File Move Task Parameters
public class FileMoveParameters
{
    public List<string> FileIds { get; set; } = [];
    public Guid TargetPoolId { get; set; }
    public Guid? TargetBundleId { get; set; }
    public int FilesProcessed { get; set; }
}

// File Compression Task Parameters
public class FileCompressParameters
{
    public List<string> FileIds { get; set; } = [];
    public string CompressionFormat { get; set; } = "zip";
    public int CompressionLevel { get; set; } = 6;
    public string? OutputFileName { get; set; }
    public int FilesProcessed { get; set; }
    public string? ResultFileId { get; set; }
}

// Bulk Operation Task Parameters
public class BulkOperationParameters
{
    public string OperationType { get; set; } = string.Empty;
    public List<string> TargetIds { get; set; } = [];
    public Dictionary<string, object?> OperationParameters { get; set; } = new();
    public int ItemsProcessed { get; set; }
    public Dictionary<string, object?>? OperationResults { get; set; }
}

// Storage Migration Task Parameters
public class StorageMigrationParameters
{
    public Guid SourcePoolId { get; set; }
    public Guid TargetPoolId { get; set; }
    public List<string> FileIds { get; set; } = new();
    public bool PreserveOriginals { get; set; } = true;
    public long TotalBytesToTransfer { get; set; }
    public long BytesTransferred { get; set; }
    public int FilesMigrated { get; set; }
}

// Helper class for parameter operations using GrpcTypeHelper
public static class ParameterHelper
{
    public static T? Typed<T>(Dictionary<string, object?> parameters)
    {
        var rawParams = InfraObjectCoder.ConvertObjectToByteString(parameters);
        return InfraObjectCoder.ConvertByteStringToObject<T>(rawParams);
    }

    public static Dictionary<string, object?> Untyped<T>(T parameters)
    {
        var rawParams = InfraObjectCoder.ConvertObjectToByteString(parameters);
        return InfraObjectCoder.ConvertByteStringToObject<Dictionary<string, object?>>(rawParams) ?? [];
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
    public string? Path { get; set; }
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

    [MaxLength(64)] public string TaskId { get; set; } = null!;

    [MaxLength(256)] public string Name { get; set; } = null!;

    [MaxLength(1024)] public string? Description { get; set; }

    public TaskType Type { get; set; }

    public TaskStatus Status { get; set; } = TaskStatus.InProgress;

    public Guid AccountId { get; set; }

    // Progress tracking (0-100)
    public double Progress { get; set; }

    // Task-specific parameters stored as JSON
    [Column(TypeName = "jsonb")] public Dictionary<string, object?> Parameters { get; set; } = new();

    // Task results/output stored as JSON
    [Column(TypeName = "jsonb")] public Dictionary<string, object?> Results { get; set; } = new();

    [MaxLength(1024)] public string? ErrorMessage { get; set; }

    public Instant? StartedAt { get; set; }
    public Instant? CompletedAt { get; set; }
    public Instant? ExpiredAt { get; set; }

    public Instant LastActivity { get; set; }

    // Priority (higher = more important)
    public int Priority { get; set; } = 0;

    // Estimated duration in seconds
    public long? EstimatedDurationSeconds { get; set; }
}

public class PersistentUploadTask : PersistentTask
{
    public PersistentUploadTask()
    {
        Type = TaskType.FileUpload;
        Name = "File Upload";
    }

    // Convenience properties using typed parameters
    [NotMapped]
    public FileUploadParameters TypedParameters
    {
        get => ParameterHelper.Typed<FileUploadParameters>(Parameters)!;
        set => Parameters = ParameterHelper.Untyped(value);
    }

    [MaxLength(256)]
    public string FileName
    {
        get => TypedParameters.FileName;
        set
        {
            var parameters = TypedParameters;
            parameters.FileName = value;
            TypedParameters = parameters;
        }
    }

    public long FileSize
    {
        get => TypedParameters.FileSize;
        set
        {
            var parameters = TypedParameters;
            parameters.FileSize = value;
            TypedParameters = parameters;
        }
    }

    [MaxLength(128)]
    public string ContentType
    {
        get => TypedParameters.ContentType;
        set
        {
            var parameters = TypedParameters;
            parameters.ContentType = value;
            TypedParameters = parameters;
        }
    }

    public long ChunkSize
    {
        get => TypedParameters.ChunkSize;
        set
        {
            var parameters = TypedParameters;
            parameters.ChunkSize = value;
            TypedParameters = parameters;
        }
    }

    public int ChunksCount
    {
        get => TypedParameters.ChunksCount;
        set
        {
            var parameters = TypedParameters;
            parameters.ChunksCount = value;
            TypedParameters = parameters;
        }
    }

    public int ChunksUploaded
    {
        get => TypedParameters.ChunksUploaded;
        set
        {
            var parameters = TypedParameters;
            parameters.ChunksUploaded = value;
            TypedParameters = parameters;
            Progress = ChunksCount > 0 ? (double)value / ChunksCount * 100 : 0;
        }
    }

    public Guid PoolId
    {
        get => TypedParameters.PoolId;
        set
        {
            var parameters = TypedParameters;
            parameters.PoolId = value;
            TypedParameters = parameters;
        }
    }

    public Guid? BundleId
    {
        get => TypedParameters.BundleId;
        set
        {
            var parameters = TypedParameters;
            parameters.BundleId = value;
            TypedParameters = parameters;
        }
    }

    [MaxLength(256)]
    public string? EncryptPassword
    {
        get => TypedParameters.EncryptPassword;
        set
        {
            var parameters = TypedParameters;
            parameters.EncryptPassword = value;
            TypedParameters = parameters;
        }
    }

    public string Hash
    {
        get => TypedParameters.Hash;
        set
        {
            var parameters = TypedParameters;
            parameters.Hash = value;
            TypedParameters = parameters;
        }
    }

    // JSON array of uploaded chunk indices for resumability
    public List<int> UploadedChunks
    {
        get => TypedParameters.UploadedChunks;
        set
        {
            var parameters = TypedParameters;
            parameters.UploadedChunks = value;
            TypedParameters = parameters;
        }
    }

    public string? Path
    {
        get => TypedParameters.Path;
        set
        {
            var parameters = TypedParameters;
            parameters.Path = value;
            TypedParameters = parameters;
        }
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

[Flags]
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

    // Convenience properties using typed parameters
    public FileMoveParameters TypedParameters
    {
        get => ParameterHelper.Typed<FileMoveParameters>(Parameters)!;
        set => Parameters = ParameterHelper.Untyped(value);
    }

    public List<string> FileIds
    {
        get => TypedParameters.FileIds;
        set
        {
            var parameters = TypedParameters;
            parameters.FileIds = value;
            TypedParameters = parameters;
        }
    }

    public Guid TargetPoolId
    {
        get => TypedParameters.TargetPoolId;
        set
        {
            var parameters = TypedParameters;
            parameters.TargetPoolId = value;
            TypedParameters = parameters;
        }
    }

    public Guid? TargetBundleId
    {
        get => TypedParameters.TargetBundleId;
        set
        {
            var parameters = TypedParameters;
            parameters.TargetBundleId = value;
            TypedParameters = parameters;
        }
    }

    public int FilesProcessed
    {
        get => TypedParameters.FilesProcessed;
        set
        {
            var parameters = TypedParameters;
            parameters.FilesProcessed = value;
            TypedParameters = parameters;
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

    // Convenience properties using typed parameters
    public FileCompressParameters TypedParameters
    {
        get => ParameterHelper.Typed<FileCompressParameters>(Parameters)!;
        set => Parameters = ParameterHelper.Untyped(value);
    }

    public List<string> FileIds
    {
        get => TypedParameters.FileIds;
        set
        {
            var parameters = TypedParameters;
            parameters.FileIds = value;
            TypedParameters = parameters;
        }
    }

    [MaxLength(32)]
    public string CompressionFormat
    {
        get => TypedParameters.CompressionFormat;
        set
        {
            var parameters = TypedParameters;
            parameters.CompressionFormat = value;
            TypedParameters = parameters;
        }
    }

    public int CompressionLevel
    {
        get => TypedParameters.CompressionLevel;
        set
        {
            var parameters = TypedParameters;
            parameters.CompressionLevel = value;
            TypedParameters = parameters;
        }
    }

    public string? OutputFileName
    {
        get => TypedParameters.OutputFileName;
        set
        {
            var parameters = TypedParameters;
            parameters.OutputFileName = value;
            TypedParameters = parameters;
        }
    }

    public int FilesProcessed
    {
        get => TypedParameters.FilesProcessed;
        set
        {
            var parameters = TypedParameters;
            parameters.FilesProcessed = value;
            TypedParameters = parameters;
            Progress = FileIds.Count > 0 ? (double)value / FileIds.Count * 100 : 0;
        }
    }

    public string? ResultFileId
    {
        get => TypedParameters.ResultFileId;
        set
        {
            var parameters = TypedParameters;
            parameters.ResultFileId = value;
            TypedParameters = parameters;
        }
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

    // Convenience properties using typed parameters
    public BulkOperationParameters TypedParameters
    {
        get => ParameterHelper.Typed<BulkOperationParameters>(Parameters)!;
        set => Parameters = ParameterHelper.Untyped(value);
    }

    [MaxLength(128)]
    public string OperationType
    {
        get => TypedParameters.OperationType;
        set
        {
            var parameters = TypedParameters;
            parameters.OperationType = value;
            TypedParameters = parameters;
        }
    }

    public List<string> TargetIds
    {
        get => TypedParameters.TargetIds;
        set
        {
            var parameters = TypedParameters;
            parameters.TargetIds = value;
            TypedParameters = parameters;
        }
    }

    [Column(TypeName = "jsonb")]
    public Dictionary<string, object?> OperationParameters
    {
        get => TypedParameters.OperationParameters;
        set
        {
            var parameters = TypedParameters;
            parameters.OperationParameters = value;
            TypedParameters = parameters;
        }
    }

    public int ItemsProcessed
    {
        get => TypedParameters.ItemsProcessed;
        set
        {
            var parameters = TypedParameters;
            parameters.ItemsProcessed = value;
            TypedParameters = parameters;
            Progress = TargetIds.Count > 0 ? (double)value / TargetIds.Count * 100 : 0;
        }
    }

    [Column(TypeName = "jsonb")]
    public Dictionary<string, object?>? OperationResults
    {
        get => TypedParameters.OperationResults;
        set
        {
            var parameters = TypedParameters;
            parameters.OperationResults = value;
            TypedParameters = parameters;
        }
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

    // Convenience properties using typed parameters
    public StorageMigrationParameters TypedParameters
    {
        get => ParameterHelper.Typed<StorageMigrationParameters>(Parameters)!;
        set => Parameters = ParameterHelper.Untyped(value);
    }

    public Guid SourcePoolId
    {
        get => TypedParameters.SourcePoolId;
        set
        {
            var parameters = TypedParameters;
            parameters.SourcePoolId = value;
            TypedParameters = parameters;
        }
    }

    public Guid TargetPoolId
    {
        get => TypedParameters.TargetPoolId;
        set
        {
            var parameters = TypedParameters;
            parameters.TargetPoolId = value;
            TypedParameters = parameters;
        }
    }

    public List<string> FileIds
    {
        get => TypedParameters.FileIds;
        set
        {
            var parameters = TypedParameters;
            parameters.FileIds = value;
            TypedParameters = parameters;
        }
    }

    public bool PreserveOriginals
    {
        get => TypedParameters.PreserveOriginals;
        set
        {
            var parameters = TypedParameters;
            parameters.PreserveOriginals = value;
            TypedParameters = parameters;
        }
    }

    public long TotalBytesToTransfer
    {
        get => TypedParameters.TotalBytesToTransfer;
        set
        {
            var parameters = TypedParameters;
            parameters.TotalBytesToTransfer = value;
            TypedParameters = parameters;
        }
    }

    public long BytesTransferred
    {
        get => TypedParameters.BytesTransferred;
        set
        {
            var parameters = TypedParameters;
            parameters.BytesTransferred = value;
            TypedParameters = parameters;
            Progress = TotalBytesToTransfer > 0 ? (double)value / TotalBytesToTransfer * 100 : 0;
        }
    }

    public int FilesMigrated
    {
        get => TypedParameters.FilesMigrated;
        set
        {
            var parameters = TypedParameters;
            parameters.FilesMigrated = value;
            TypedParameters = parameters;
        }
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
