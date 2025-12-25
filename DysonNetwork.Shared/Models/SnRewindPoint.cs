using System.ComponentModel.DataAnnotations.Schema;

namespace DysonNetwork.Shared.Models;

public class SnRewindPoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Year { get; set; } = DateTime.UtcNow.Year;

    [Column(TypeName = "jsonb")] public Dictionary<string, string> Data { get; set; } = new();
    
    public Guid AccountId { get; set; }
    public SnAccount Account { get; set; } = null!;
}
