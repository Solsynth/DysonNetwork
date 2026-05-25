using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Sphere.Timeline;

public class FriendPresenceEvent(SnPresenceActivity activity) : ITimelineEvent
{
    public SnPresenceActivity Activity { get; set; } = activity;

    public SnTimelineEvent ToActivity()
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        return new SnTimelineEvent
        {
            Id = Activity.Id,
            Type = "presence.friend",
            ResourceIdentifier = $"presence:{Activity.AccountId}:{Activity.Id}",
            Data = this,
            CreatedAt = Activity.UpdatedAt,
            UpdatedAt = now,
        };
    }
}
