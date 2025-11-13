using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Shared.Models;

[Index(nameof(FolderId), nameof(AccountId))]
[Index(nameof(FileId))]
public class SnCloudFileIndex : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Reference to the folder containing this file
    /// </summary>
    public Guid FolderId { get; init; }

    /// <summary>
    /// Navigation property to the folder
    /// </summary>
    [JsonIgnore]
    public SnCloudFolder Folder { get; init; } = null!;

    [MaxLength(32)] public string FileId { get; init; } = null!;
    public SnCloudFile File { get; init; } = null!;
    public Guid AccountId { get; init; }
    [NotMapped] public SnAccount? Account { get; init; }

    /// <summary>
    /// Cached full path of the file (stored in database for performance)
    /// </summary>
    [MaxLength(2048)]
    public string Path { get; set; } = null!;

    /// <summary>
    /// Creates a new file index with the specified folder and file
    /// </summary>
    public static SnCloudFileIndex Create(SnCloudFolder folder, SnCloudFile file, Guid accountId)
    {
        // Build the full path by traversing the folder hierarchy
        var pathSegments = new List<string>();
        var currentFolder = folder;

        while (currentFolder != null)
        {
            if (!string.IsNullOrEmpty(currentFolder.Name))
            {
                pathSegments.Insert(0, currentFolder.Name);
            }
            currentFolder = currentFolder.ParentFolder;
        }

        // Add the file name
        if (!string.IsNullOrEmpty(file.Name))
        {
            pathSegments.Add(file.Name);
        }

        var fullPath = "/" + string.Join("/", pathSegments);

        return new SnCloudFileIndex
        {
            FolderId = folder.Id,
            FileId = file.Id,
            AccountId = accountId,
            Path = fullPath
        };
    }
}
