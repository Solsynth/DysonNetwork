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

public class PersonalizedTimelineDiscoveryEvent(
    string kind,
    string title,
    List<SnDiscoverySuggestion> items
) : ITimelineEvent
{
    public string Kind { get; set; } = kind;
    public string Title { get; set; } = title;
    public List<SnDiscoverySuggestion> Items { get; set; } = items;

    public SnTimelineEvent ToActivity()
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        return new SnTimelineEvent
        {
            Id = Guid.NewGuid(),
            Type = "discovery.v2",
            ResourceIdentifier = $"discovery:{Kind}",
            Data = this,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}
