using System;
using System.Collections.Generic;
using NodaTime;

namespace DysonNetwork.Sphere.Activity;

public class DiscoveryActivity(List<DiscoveryItem> items) : IActivity
{
    public List<DiscoveryItem> Items { get; set; } = items;

    public Activity ToActivity()
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        return new Activity
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