using System.Collections.Concurrent;
using DysonNetwork.Passport.Account;
using DysonNetwork.Shared.EventBus;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Queue;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NodaTime;

namespace DysonNetwork.Passport.Meet;

public class LocationPinCacheEntry
{
    public Guid PinId { get; set; }
    public Guid AccountId { get; set; }
    public string DeviceId { get; set; } = null!;
    public string? LocationName { get; set; }
    public string? LocationAddress { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public Instant LastUpdateAt { get; set; }
    public bool IsDirty { get; set; }
}

public class LocationPinService(
    AppDatabase db,
    AccountService accounts,
    RelationshipService relationships,
    LocationPinSubscriptionHub subscriptions,
    IEventBus eventBus,
    ILogger<LocationPinService> logger
)
{
    public static readonly Duration DefaultOfflineTtl = Duration.FromHours(24);
    private static readonly Duration OfflineHeartbeatThreshold = Duration.FromMinutes(5);
    private static readonly Duration LocationUpdateInterval = Duration.FromSeconds(30);
    private static readonly Duration RateLimitWindow = Duration.FromSeconds(5);

    private readonly ConcurrentDictionary<(Guid AccountId, string DeviceId), LocationPinCacheEntry> _cache = new();
    private readonly ConcurrentDictionary<Guid, Instant> _lastUpdateTimes = new();
    private readonly SemaphoreSlim _flushLock = new(1, 1);

    public async Task<SnLocationPin> CreatePinAsync(
        Guid accountId,
        string deviceId,
        LocationVisibility visibility,
        string? locationName,
        string? locationAddress,
        Geometry? location,
        bool keepOnDisconnect,
        Dictionary<string, object>? metadata,
        CancellationToken cancellationToken = default
    )
    {
        var now = SystemClock.Instance.GetCurrentInstant();

        var existingPin = await db.LocationPins
            .FirstOrDefaultAsync(p => p.AccountId == accountId && p.Status == LocationPinStatus.Active, cancellationToken);

        if (existingPin != null)
        {
            await RemovePinInternalAsync(existingPin, cancellationToken);
        }

        var pin = new SnLocationPin
        {
            AccountId = accountId,
            DeviceId = deviceId,
            Visibility = visibility,
            LocationName = locationName,
            LocationAddress = locationAddress,
            Location = location,
            Status = LocationPinStatus.Active,
            LastHeartbeatAt = now,
            KeepOnDisconnect = keepOnDisconnect,
            Metadata = metadata ?? [],
            ExpiresAt = null
        };

        db.LocationPins.Add(pin);
        await db.SaveChangesAsync(cancellationToken);

        var cacheEntry = new LocationPinCacheEntry
        {
            PinId = pin.Id,
            AccountId = accountId,
            DeviceId = deviceId,
            LocationName = locationName,
            LocationAddress = locationAddress,
            Latitude = location?.Coordinate?.Y,
            Longitude = location?.Coordinate?.X,
            LastUpdateAt = now,
            IsDirty = false
        };
        _cache[(accountId, deviceId)] = cacheEntry;
        _lastUpdateTimes[accountId] = now;

        await eventBus.PublishAsync(new LocationPinCreatedEvent
        {
            PinId = pin.Id,
            AccountId = accountId,
            Visibility = visibility
        }, cancellationToken);

        await subscriptions.PublishAsync("pin_created", pin, cancellationToken);

        logger.LogInformation("Created location pin {PinId} for account {AccountId}", pin.Id, accountId);

        return pin;
    }

    public async Task<SnLocationPin?> UpdateLocationAsync(
        Guid accountId,
        string deviceId,
        Geometry? location,
        string? locationName,
        string? locationAddress,
        CancellationToken cancellationToken = default
    )
    {
        var now = SystemClock.Instance.GetCurrentInstant();

        if (!CanUpdate(accountId))
        {
            return null;
        }

        var pin = await db.LocationPins
            .FirstOrDefaultAsync(p => p.AccountId == accountId && p.Status == LocationPinStatus.Active, cancellationToken);

        if (pin == null)
        {
            return null;
        }

        if (!_cache.TryGetValue((accountId, deviceId), out var cacheEntry))
        {
            cacheEntry = new LocationPinCacheEntry
            {
                PinId = pin.Id,
                AccountId = accountId,
                DeviceId = deviceId,
                LastUpdateAt = now,
                IsDirty = false
            };
            _cache[(accountId, deviceId)] = cacheEntry;
        }

        cacheEntry.Latitude = location?.Coordinate?.Y;
        cacheEntry.Longitude = location?.Coordinate?.X;
        cacheEntry.LocationName = locationName;
        cacheEntry.LocationAddress = locationAddress;
        cacheEntry.LastUpdateAt = now;
        cacheEntry.IsDirty = true;

        pin.LastHeartbeatAt = now;
        _lastUpdateTimes[accountId] = now;

        await eventBus.PublishAsync(new LocationPinUpdatedEvent
        {
            PinId = pin.Id,
            AccountId = accountId,
            Latitude = cacheEntry.Latitude,
            Longitude = cacheEntry.Longitude,
            LocationName = locationName,
            LocationAddress = locationAddress
        }, cancellationToken);

        await subscriptions.PublishAsync("pin_updated", pin, cancellationToken);

        return pin;
    }

    public async Task<bool> RemovePinAsync(Guid accountId, string deviceId, CancellationToken cancellationToken = default)
    {
        var pin = await db.LocationPins
            .FirstOrDefaultAsync(p => p.AccountId == accountId && p.DeviceId == deviceId && p.Status == LocationPinStatus.Active, cancellationToken);

        if (pin == null)
        {
            return false;
        }

        _cache.TryRemove((accountId, deviceId), out _);
        _lastUpdateTimes.TryRemove(accountId, out _);

        await RemovePinInternalAsync(pin, cancellationToken);

        await eventBus.PublishAsync(new LocationPinRemovedEvent
        {
            PinId = pin.Id,
            AccountId = accountId
        }, cancellationToken);

        await subscriptions.PublishAsync("pin_removed", pin, cancellationToken);

        logger.LogInformation("Removed location pin {PinId} for account {AccountId}", pin.Id, accountId);

        return true;
    }

    public async Task DisconnectPinAsync(
        Guid accountId,
        string deviceId,
        bool keepOnDisconnect,
        CancellationToken cancellationToken = default
    )
    {
        var pin = await db.LocationPins
            .FirstOrDefaultAsync(p => p.AccountId == accountId && p.DeviceId == deviceId && p.Status == LocationPinStatus.Active, cancellationToken);

        if (pin == null)
        {
            return;
        }

        await FlushPinToDbAsync(accountId, deviceId, cancellationToken);

        if (!keepOnDisconnect)
        {
            await RemovePinAsync(accountId, deviceId, cancellationToken);
            return;
        }

        pin.Status = LocationPinStatus.Offline;
        pin.ExpiresAt = SystemClock.Instance.GetCurrentInstant() + DefaultOfflineTtl;
        await db.SaveChangesAsync(cancellationToken);

        _cache.TryRemove((accountId, deviceId), out _);
        _lastUpdateTimes.TryRemove(accountId, out _);

        await eventBus.PublishAsync(new LocationPinOfflineEvent
        {
            PinId = pin.Id,
            AccountId = accountId
        }, cancellationToken);

        await subscriptions.PublishAsync("pin_offline", pin, cancellationToken);

        logger.LogInformation("Pin {PinId} went offline for account {AccountId}, keeping until {ExpiresAt}", pin.Id, accountId, pin.ExpiresAt);
    }

    public async Task<SnLocationPin?> GetPinAsync(Guid pinId, Guid requesterId, Geometry? requesterLocation = null, CancellationToken cancellationToken = default)
    {
        var pin = await db.LocationPins
            .FirstOrDefaultAsync(p => p.Id == pinId && p.Status != LocationPinStatus.Removed, cancellationToken);

        if (pin == null)
        {
            return null;
        }

        if (!await CanAccessPinAsync(pin, requesterId, cancellationToken))
        {
            return null;
        }

        pin.Account = await accounts.GetAccount(pin.AccountId);
        return pin;
    }

    public async Task<List<SnLocationPin>> ListNearbyPinsAsync(
        Guid requesterId,
        Geometry? requesterLocation,
        LocationVisibility? visibilityFilter = null,
        int offset = 0,
        int take = 50,
        CancellationToken cancellationToken = default
    )
    {
        await CheckAndExpireOfflinePinsAsync(cancellationToken);

        var friendIds = await relationships.ListAccountFriends(requesterId);

        var query = db.LocationPins
            .AsQueryable();

        query = query.Where(p => p.Status == LocationPinStatus.Active || p.Status == LocationPinStatus.Offline);

        if (visibilityFilter.HasValue)
        {
            query = query.Where(p => p.Visibility == visibilityFilter.Value);
        }

        var pins = await query.ToListAsync(cancellationToken);

        var accessiblePins = new List<SnLocationPin>();
        foreach (var pin in pins)
        {
            if (await CanAccessPinAsync(pin, requesterId, cancellationToken))
            {
                accessiblePins.Add(pin);
            }
        }

        var result = accessiblePins
            .OrderByDescending(p => p.LastHeartbeatAt)
            .Skip(offset)
            .Take(Math.Clamp(take, 1, 100))
            .ToList();

        foreach (var pin in result)
        {
            pin.Account = await accounts.GetAccount(pin.AccountId);
        }

        return result;
    }

    public async Task FlushPinToDbAsync(Guid accountId, string deviceId, CancellationToken cancellationToken = default)
    {
        if (!_cache.TryGetValue((accountId, deviceId), out var cacheEntry) || !cacheEntry.IsDirty)
        {
            return;
        }

        await _flushLock.WaitAsync(cancellationToken);
        try
        {
            if (!_cache.TryGetValue((accountId, deviceId), out cacheEntry) || !cacheEntry.IsDirty)
            {
                return;
            }

            var pin = await db.LocationPins
                .FirstOrDefaultAsync(p => p.Id == cacheEntry.PinId, cancellationToken);

            if (pin == null)
            {
                return;
            }

            if (cacheEntry.Latitude.HasValue && cacheEntry.Longitude.HasValue)
            {
                pin.Location = new Point(new Coordinate(cacheEntry.Longitude.Value, cacheEntry.Latitude.Value)) { SRID = 4326 };
            }

            pin.LocationName = cacheEntry.LocationName;
            pin.LocationAddress = cacheEntry.LocationAddress;
            pin.LastHeartbeatAt = cacheEntry.LastUpdateAt;

            await db.SaveChangesAsync(cancellationToken);

            cacheEntry.IsDirty = false;

            logger.LogDebug("Flushed location pin {PinId} to database", pin.Id);
        }
        finally
        {
            _flushLock.Release();
        }
    }

    public async Task FlushAllDirtyPinsAsync(CancellationToken cancellationToken = default)
    {
        var dirtyEntries = _cache.Values.Where(c => c.IsDirty).ToList();
        foreach (var entry in dirtyEntries)
        {
            await FlushPinToDbAsync(entry.AccountId, entry.DeviceId, cancellationToken);
        }
    }

    public async Task CheckAndExpireOfflinePinsAsync(CancellationToken cancellationToken = default)
    {
        var now = SystemClock.Instance.GetCurrentInstant();

        var expiredPins = await db.LocationPins
            .Where(p => p.Status == LocationPinStatus.Offline && p.ExpiresAt != null && p.ExpiresAt <= now)
            .ToListAsync(cancellationToken);

        foreach (var pin in expiredPins)
        {
            pin.Status = LocationPinStatus.Removed;
            logger.LogInformation("Expired offline pin {PinId} for account {AccountId}", pin.Id, pin.AccountId);
        }

        if (expiredPins.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        var stalePins = await db.LocationPins
            .Where(p => p.Status == LocationPinStatus.Active && p.LastHeartbeatAt <= now - OfflineHeartbeatThreshold)
            .ToListAsync(cancellationToken);

        foreach (var pin in stalePins)
        {
            pin.Status = LocationPinStatus.Offline;
            pin.ExpiresAt = now + DefaultOfflineTtl;
            logger.LogInformation("Pin {PinId} marked offline due to heartbeat timeout", pin.Id);
        }

        if (stalePins.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private bool CanUpdate(Guid accountId)
    {
        if (!_lastUpdateTimes.TryGetValue(accountId, out var lastUpdate))
        {
            return true;
        }

        var now = SystemClock.Instance.GetCurrentInstant();
        return now - lastUpdate >= RateLimitWindow;
    }

    private async Task<bool> CanAccessPinAsync(SnLocationPin pin, Guid requesterId, CancellationToken cancellationToken)
    {
        if (pin.AccountId == requesterId)
        {
            return true;
        }

        switch (pin.Visibility)
        {
            case LocationVisibility.Public:
                return true;

            case LocationVisibility.Private:
                return false;

            case LocationVisibility.Unlisted:
                var isFriend = await relationships.HasRelationshipWithStatus(pin.AccountId, requesterId);
                return isFriend;

            default:
                return false;
        }
    }

    private async Task RemovePinInternalAsync(SnLocationPin pin, CancellationToken cancellationToken)
    {
        pin.Status = LocationPinStatus.Removed;
        pin.ExpiresAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync(cancellationToken);
    }
}
