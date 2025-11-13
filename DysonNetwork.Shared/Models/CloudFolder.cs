using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Shared.Models;

[Index(nameof(Path), nameof(AccountId))]
[Index(nameof(ParentFolderId))]
public class SnCloudFolder : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The name of the folder
    /// </summary>
    [MaxLength(256)]
    public string Name { get; init; } = null!;

    /// <summary>
    /// The full path of the folder (for querying purposes)
    /// With trailing slash, e.g., /documents/work/
    /// </summary>
    [MaxLength(8192)]
    public string Path { get; set; } = null!;

    /// <summary>
    /// Reference to the parent folder (null for root folders)
    /// </summary>
    public Guid? ParentFolderId { get; set; }

    /// <summary>
    /// Navigation property to the parent folder
    /// </summary>
    [ForeignKey(nameof(ParentFolderId))]
    public SnCloudFolder? ParentFolder { get; init; }

    /// <summary>
    /// Navigation property to child folders
    /// </summary>
    [InverseProperty(nameof(ParentFolder))]
    public List<SnCloudFolder> ChildFolders { get; init; } = [];

    /// <summary>
    /// Navigation property to files in this folder
    /// </summary>
    [InverseProperty(nameof(SnCloudFileIndex.Folder))]
    public List<SnCloudFileIndex> Files { get; init; } = [];

    /// <summary>
    /// The account that owns this folder
    /// </summary>
    public Guid AccountId { get; init; }

    /// <summary>
    /// Navigation property to the account
    /// </summary>
    [NotMapped]
    public SnAccount? Account { get; init; }

    /// <summary>
    /// Optional description for the folder
    /// </summary>
    [MaxLength(4096)]
    public string? Description { get; init; }

    /// <summary>
    /// Custom metadata for the folder
    /// </summary>
    [Column(TypeName = "jsonb")]
    public Dictionary<string, object?>? FolderMeta { get; init; }

    /// <summary>
    /// Creates a new folder with proper path normalization
    /// </summary>
    public static SnCloudFolder Create(string name, Guid accountId, SnCloudFolder? parentFolder = null)
    {
        var normalizedPath = parentFolder != null 
            ? $"{parentFolder.Path.TrimEnd('/')}/{name}/"
            : $"/{name}/";

        return new SnCloudFolder
        {
            Name = name,
            Path = normalizedPath,
            ParentFolderId = parentFolder?.Id,
            AccountId = accountId
        };
    }

    /// <summary>
    /// Creates the root folder for an account
    /// </summary>
    public static SnCloudFolder CreateRoot(Guid accountId)
    {
        return new SnCloudFolder
        {
            Name = "Root",
            Path = "/",
            ParentFolderId = null,
            AccountId = accountId
        };
    }
}
