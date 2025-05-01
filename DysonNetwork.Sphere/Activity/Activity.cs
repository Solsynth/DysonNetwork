using System.ComponentModel.DataAnnotations;

namespace DysonNetwork.Sphere.Activity;

public enum ActivityVisibility
{
    Public,
    Friends,
}

public class Activity : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Type { get; set; } = null!;
    [MaxLength(4096)] public string ResourceIdentifier { get; set; } = null!;
    public ActivityVisibility Visibility { get; set; } = ActivityVisibility.Public;
    public Dictionary<string, object> Meta =  new();
    
    public long AccountId { get; set; }
    public Account.Account Account { get; set; } = null!;
}