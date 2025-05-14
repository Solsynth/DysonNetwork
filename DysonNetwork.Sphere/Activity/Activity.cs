using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DysonNetwork.Sphere.Activity;

public enum ActivityVisibility
{
    Public,
    Friends,
    Selected
}

public class Activity : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Type { get; set; } = null!;
    [MaxLength(4096)] public string ResourceIdentifier { get; set; } = null!;
    public ActivityVisibility Visibility { get; set; } = ActivityVisibility.Public;
    [Column(TypeName = "jsonb")] public Dictionary<string, object> Meta { get; set; } = new();
    [Column(TypeName = "jsonb")] public ICollection<Guid> UsersVisible { get; set; } = new List<Guid>();

    public Guid AccountId { get; set; }
    public Account.Account Account { get; set; } = null!;

    [NotMapped] public object? Data { get; set; }
}