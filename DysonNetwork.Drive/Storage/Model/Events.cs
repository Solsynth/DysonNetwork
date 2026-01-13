namespace DysonNetwork.Drive.Storage.Model;

public static class FileUploadedEvent
{
    public const string Type = "file_uploaded";
}

public record FileUploadedEventPayload(
    string FileId,
    Guid RemoteId,
    string? StorageId,
    string? ContentType,
    string ProcessingFilePath,
    bool IsTempFile
);
