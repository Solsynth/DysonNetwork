using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Sphere.ActivityPub;

public class BoostInfo
{
    public Guid BoostId { get; set; }
    public Instant BoostedAt { get; set; }
    public string? ActivityPubUri { get; set; }
    public string? WebUrl { get; set; }
    public SnPost OriginalPost { get; set; } = null!;
    public SnFediverseActor? OriginalActor { get; set; }
}

public class PostResponse : SnPost
{
    public BoostInfo? BoostInfo { get; set; }
}
