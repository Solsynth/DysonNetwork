using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Shared.Models;

[Index(nameof(Path), nameof(AccountId))]
public class SnCloudFileIndex : ModelBase
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// The path of the file,
    /// only store the parent folder.
    /// With the trailing slash.
    ///
    /// Like /hello/here/ not /hello/here/text.txt or /hello/here
    /// Or the user home folder files, store as /
    ///
    /// Besides, the folder name should be all query-safe, not contains % or
    /// other special characters that will mess up the pgsql query
    /// </summary>
    [MaxLength(8192)]
    public string Path { get; init; } = null!;

    [MaxLength(32)] public string FileId { get; init; } = null!;
    public SnCloudFile File { get; init; } = null!;
    public Guid AccountId { get; init; }
    [NotMapped] public SnAccount? Account { get; init; }
}
