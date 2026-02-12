using System.ComponentModel.DataAnnotations.Schema;
using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Insight.MiChan;

[Table("interactive_history")]
public class MiChanInteractiveHistory : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The ID of the resource that was interacted with (post ID, user ID, etc.)
    /// </summary>
    public Guid ResourceId { get; set; }

    /// <summary>
    /// The type of resource: "post", "user", etc.
    /// </summary>
    public string ResourceType { get; set; } = null!;

    /// <summary>
    /// The behaviour/action taken: "reply", "react", "repost", "conversation"
    /// </summary>
    public string Behaviour { get; set; } = null!;

    /// <summary>
    /// Whether this interaction record is still active/valid
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When this interaction record expires (null = never expires)
    /// </summary>
    public Instant? ExpiresAt { get; set; }
}
