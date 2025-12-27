using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DysonNetwork.Shared.Models;

public class SnRewindPoint : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Year { get; set; } = DateTime.UtcNow.Year;
    
    /// <summary>
    /// Due to every year the Solar Network upgrade become better and better.
    /// The rewind data might be incompatible at that time,
    /// this field provide the clues for the client to parsing the data correctly.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;
    [MaxLength(4096)] public string? SharableCode { get; set; }

    [Column(TypeName = "jsonb")] public Dictionary<string, object?> Data { get; set; } = new();
    
    public Guid AccountId { get; set; }
    public SnAccount Account { get; set; } = null!;
}
