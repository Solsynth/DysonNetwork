using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Sphere.Timeline;

public class FriendStatusEvent(SnAccountStatus status) : ITimelineEvent
{
    public SnAccountStatus Status { get; set; } = status;

    public SnTimelineEvent ToActivity()
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        return new SnTimelineEvent
        {
            Id = Status.Id,
            Type = "status.friend",
            ResourceIdentifier = $"status:{Status.AccountId}:{Status.Id}",
            Data = this,
            CreatedAt = GetStatusTimelineInstant(Status, now),
            UpdatedAt = now,
        };
    }

    private static Instant GetStatusTimelineInstant(SnAccountStatus status, Instant fallbackNow)
    {
        if (status.UpdatedAt != default)
            return status.UpdatedAt;
        if (status.CreatedAt != default)
            return status.CreatedAt;
        return fallbackNow;
    }
}
