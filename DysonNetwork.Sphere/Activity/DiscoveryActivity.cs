using System;
using System.Collections.Generic;
using NodaTime;

namespace DysonNetwork.Sphere.Activity
{
    public class DiscoveryActivity : IActivity
    {
        public string Title { get; set; }
        public List<object> Items { get; set; }

        public DiscoveryActivity(string title, List<object> items)
        {
            Title = title;
            Items = items;
        }

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
}
