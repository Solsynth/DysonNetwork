using System.ComponentModel.DataAnnotations;
using DysonNetwork.Develop.Identity;
using DysonNetwork.Shared.Data;

namespace DysonNetwork.Develop.Project;

public class DevProject : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Slug { get; set; } = string.Empty;
    [MaxLength(1024)] public string Name { get; set; } = string.Empty;
    [MaxLength(4096)] public string Description { get; set; } = string.Empty;
    
    public Developer Developer { get; set; } = null!;
    public Guid DeveloperId { get; set; }
}