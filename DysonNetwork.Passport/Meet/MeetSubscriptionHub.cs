using System.Collections.Concurrent;
using System.Threading.Channels;
using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Passport.Meet;

public class MeetStreamEvent
{
    public string Type { get; set; } = string.Empty;
    public Instant SentAt { get; set; }
    public SnMeet Meet { get; set; } = null!;
}

public class MeetSubscriptionHub
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Channel<MeetStreamEvent>>> _subscriptions = new();

    public ChannelReader<MeetStreamEvent> Subscribe(Guid meetId, Guid subscriptionId)
    {
        var meetSubscriptions = _subscriptions.GetOrAdd(meetId, _ => new ConcurrentDictionary<Guid, Channel<MeetStreamEvent>>());
        var channel = Channel.CreateUnbounded<MeetStreamEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        meetSubscriptions[subscriptionId] = channel;
        return channel.Reader;
    }

    public void Unsubscribe(Guid meetId, Guid subscriptionId)
    {
        if (!_subscriptions.TryGetValue(meetId, out var meetSubscriptions)) return;
        if (meetSubscriptions.TryRemove(subscriptionId, out var channel))
            channel.Writer.TryComplete();
        if (meetSubscriptions.IsEmpty)
            _subscriptions.TryRemove(meetId, out _);
    }

    public Task PublishAsync(string type, SnMeet meet, CancellationToken cancellationToken = default)
    {
        if (!_subscriptions.TryGetValue(meet.Id, out var meetSubscriptions))
            return Task.CompletedTask;

        var payload = new MeetStreamEvent
        {
            Type = type,
            SentAt = SystemClock.Instance.GetCurrentInstant(),
            Meet = meet
        };

        foreach (var (_, channel) in meetSubscriptions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            channel.Writer.TryWrite(payload);
            if (meet.IsFinal)
                channel.Writer.TryComplete();
        }

        if (meet.IsFinal)
            _subscriptions.TryRemove(meet.Id, out _);

        return Task.CompletedTask;
    }
}
