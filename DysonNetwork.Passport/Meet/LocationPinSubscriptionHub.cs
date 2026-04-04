using System.Collections.Concurrent;
using System.Threading.Channels;
using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Passport.Meet;

public class LocationPinStreamEvent
{
    public string Type { get; set; } = string.Empty;
    public Instant SentAt { get; set; }
    public SnLocationPin Pin { get; set; } = null!;
}

public class LocationPinSubscriptionHub
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Channel<LocationPinStreamEvent>>> _subscriptions = new();

    public ChannelReader<LocationPinStreamEvent> Subscribe(Guid subscriptionId)
    {
        var channel = Channel.CreateUnbounded<LocationPinStreamEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _subscriptions[subscriptionId] = new ConcurrentDictionary<Guid, Channel<LocationPinStreamEvent>>();
        _subscriptions[subscriptionId][subscriptionId] = channel;
        return channel.Reader;
    }

    public ChannelReader<LocationPinStreamEvent> SubscribeToPin(Guid pinId, Guid subscriptionId)
    {
        var pinSubscriptions = _subscriptions.GetOrAdd(pinId, _ => new ConcurrentDictionary<Guid, Channel<LocationPinStreamEvent>>());
        var channel = Channel.CreateUnbounded<LocationPinStreamEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        pinSubscriptions[subscriptionId] = channel;
        return channel.Reader;
    }

    public void Unsubscribe(Guid subscriptionId)
    {
        foreach (var (_, pinSubscriptions) in _subscriptions)
        {
            if (pinSubscriptions.TryRemove(subscriptionId, out var channel))
            {
                channel.Writer.TryComplete();
            }
        }
    }

    public void UnsubscribeFromPin(Guid pinId, Guid subscriptionId)
    {
        if (_subscriptions.TryGetValue(pinId, out var pinSubscriptions))
        {
            if (pinSubscriptions.TryRemove(subscriptionId, out var channel))
            {
                channel.Writer.TryComplete();
            }
            if (pinSubscriptions.IsEmpty)
            {
                _subscriptions.TryRemove(pinId, out _);
            }
        }
    }

    public Task PublishAsync(string type, SnLocationPin pin, CancellationToken cancellationToken = default)
    {
        var payload = new LocationPinStreamEvent
        {
            Type = type,
            SentAt = SystemClock.Instance.GetCurrentInstant(),
            Pin = pin
        };

        if (_subscriptions.TryGetValue(pin.Id, out var pinSubscriptions))
        {
            foreach (var (_, channel) in pinSubscriptions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                channel.Writer.TryWrite(payload);
            }
        }

        return Task.CompletedTask;
    }
}
