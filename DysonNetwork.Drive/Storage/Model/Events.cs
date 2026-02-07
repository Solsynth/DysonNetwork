using DysonNetwork.Shared.EventBus;

namespace DysonNetwork.Drive.Storage.Model;

public class FileUploadedEvent : EventBase
{
    public static string Type => "file_uploaded";
    public override string EventType => Type;
    public override string StreamName => "file_events";

    public string FileId { get; set; } = null!;
    public Guid RemoteId { get; set; }
    public string? StorageId { get; set; }
    public string? ContentType { get; set; }
    public string ProcessingFilePath { get; set; } = null!;
    public bool IsTempFile { get; set; }
}

public record FileUploadedEventPayload(
    string FileId,
    Guid RemoteId,
    string? StorageId,
    string? ContentType,
    string ProcessingFilePath,
    bool IsTempFile
);
