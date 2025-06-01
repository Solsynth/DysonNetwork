namespace DysonNetwork.Sphere.Storage;

/// <summary>
/// Common interface for cloud file entities that can be used in file operations.
/// This interface exposes the essential properties needed for file operations
/// and is implemented by both CloudFile and CloudFileReferenceObject.
/// </summary>
public interface ICloudFile
{
    /// <summary>
    /// Gets the unique identifier of the cloud file.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the name of the cloud file.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the file metadata dictionary.
    /// </summary>
    Dictionary<string, object>? FileMeta { get; }

    /// <summary>
    /// Gets the user metadata dictionary.
    /// </summary>
    Dictionary<string, object>? UserMeta { get; }

    /// <summary>
    /// Gets the MIME type of the file.
    /// </summary>
    string? MimeType { get; }

    /// <summary>
    /// Gets the hash of the file content.
    /// </summary>
    string? Hash { get; }

    /// <summary>
    /// Gets the size of the file in bytes.
    /// </summary>
    long Size { get; }

    /// <summary>
    /// Gets whether the file has a compressed version available.
    /// </summary>
    bool HasCompression { get; }
}
