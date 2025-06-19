using NodaTime;

namespace DysonNetwork.Sphere.Post;

public class PostViewInfo
{
    public Guid PostId { get; set; }
    public string? ViewerId { get; set; }
    public Instant ViewedAt { get; set; }
}
