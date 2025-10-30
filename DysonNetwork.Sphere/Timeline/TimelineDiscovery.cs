using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Sphere.Timeline;

public class TimelineDiscoveryEvent(List<DiscoveryItem> items) : ITimelineEvent
{
    public List<DiscoveryItem> Items { get; set; } = items;

    public SnTimelineEvent ToActivity()
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        return new SnTimelineEvent
        {
            Id = Guid.NewGuid(),
            Type = "discovery",
            ResourceIdentifier = "discovery",
            Data = this,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}

public record DiscoveryItem(string Type, object Data);

