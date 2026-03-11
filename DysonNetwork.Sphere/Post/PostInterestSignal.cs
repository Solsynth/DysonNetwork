using NodaTime;

namespace DysonNetwork.Sphere.Post;

public class PostInterestSignal
{
    public Guid AccountId { get; set; }
    public Guid PostId { get; set; }
    public double ScoreDelta { get; set; }
    public string SignalType { get; set; } = string.Empty;
    public Instant OccurredAt { get; set; }
}
