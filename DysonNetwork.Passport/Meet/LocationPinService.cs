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
    MeetSubscriptionHub meetSubscriptions,
    MeetExpirationScheduler expirationScheduler,
    IEventBus eventBus,
    ILogger<LocationPinService> logger
)
{
    public static readonly Duration DefaultOfflineTtl = Duration.FromHours(24);
    private static readonly Duration OfflineHeartbeatThreshold = Duration.FromMinutes(5);
    private static readonly Duration LocationUpdateInterval = Duration.FromSeconds(30);
    private static readonly Duration RateLimitWindow = Duration.FromSeconds(5);
    public static readonly Duration MeetActivityExtension = Duration.FromMinutes(30);
    public static readonly double TrailDistanceThresholdMeters = 10.0;
    private const double MetersPerDegreeLatitude = 111320.0;

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
        Guid? meetId = null,
        CancellationToken cancellationToken = default
    )
    {
        var now = SystemClock.Instance.GetCurrentInstant();

        SnLocationPin? existingForMeet = null;
        if (meetId.HasValue)
        {
            existingForMeet = await db.LocationPins
                .FirstOrDefaultAsync(p => p.MeetId == meetId && p.AccountId == accountId && p.Status == LocationPinStatus.Active, cancellationToken);
        }
        else
        {
            existingForMeet = await db.LocationPins
                .FirstOrDefaultAsync(p => p.AccountId == accountId && p.MeetId == null && p.Status == LocationPinStatus.Active, cancellationToken);
        }

        if (existingForMeet != null && location != null && existingForMeet.Location != null)
        {
            var distance = CalculateDistanceMeters(location, existingForMeet.Location);
            if (distance < TrailDistanceThresholdMeters)
            {
                existingForMeet.LocationName = locationName;
                existingForMeet.LocationAddress = locationAddress;
                existingForMeet.LastHeartbeatAt = now;
                await db.SaveChangesAsync(cancellationToken);
                
                await HydratePinAsync(existingForMeet, cancellationToken);
                
                if (meetId.HasValue)
                {
                    var meet = await db.Meets
                        .Include(m => m.Participants)
                        .FirstOrDefaultAsync(m => m.Id == meetId, cancellationToken);
                    if (meet != null)
                    {
                        existingForMeet.Meet = meet;
                        await meetSubscriptions.PublishAsync("pin_updated", meet, cancellationToken);
                        await ExtendMeetExpirationAsync(meetId.Value, cancellationToken);
                    }
                }
                else
                {
                    await subscriptions.PublishAsync("pin_updated", existingForMeet, cancellationToken);
                }
                
                logger.LogInformation("Pin {PinId} updated in place (distance {Distance}m < {Threshold}m)", existingForMeet.Id, distance, TrailDistanceThresholdMeters);
                return existingForMeet;
            }

            existingForMeet.Status = LocationPinStatus.Offline;
            existingForMeet.ExpiresAt = now + DefaultOfflineTtl;
            await db.SaveChangesAsync(cancellationToken);
        }

        var pin = new SnLocationPin
        {
            AccountId = accountId,
            DeviceId = deviceId,
            MeetId = meetId,
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

        await HydratePinAsync(pin, cancellationToken);

        if (meetId.HasValue)
        {
            var meet = await db.Meets
                .Include(m => m.Participants)
                .FirstOrDefaultAsync(m => m.Id == meetId, cancellationToken);
            if (meet != null)
            {
                pin.Meet = meet;
                await meetSubscriptions.PublishAsync("pin_created", meet, cancellationToken);
                await ExtendMeetExpirationAsync(meetId.Value, cancellationToken);
            }
        }
        else
        {
            await subscriptions.PublishAsync("pin_created", pin, cancellationToken);
        }

        logger.LogInformation("Created location pin {PinId} for account {AccountId} on meet {MeetId}", pin.Id, accountId, meetId);

        return pin;
    }

    public async Task<SnLocationPin?> UpdateLocationAsync(
        Guid accountId,
        string deviceId,
        Geometry? location,
        string? locationName,
        string? locationAddress,
        Guid? meetId = null,
        CancellationToken cancellationToken = default
    )
    {
        var now = SystemClock.Instance.GetCurrentInstant();

        if (!CanUpdate(accountId))
        {
            return null;
        }

        var pin = meetId.HasValue
            ? await db.LocationPins
                .FirstOrDefaultAsync(p => p.MeetId == meetId && p.AccountId == accountId && p.Status == LocationPinStatus.Active, cancellationToken)
            : await db.LocationPins
                .FirstOrDefaultAsync(p => p.AccountId == accountId && p.MeetId == null && p.Status == LocationPinStatus.Active, cancellationToken);

        if (pin == null)
        {
            return null;
        }

        if (location != null && pin.Location != null)
        {
            var distance = CalculateDistanceMeters(location, pin.Location);
            if (distance < TrailDistanceThresholdMeters)
            {
                pin.LocationName = locationName;
                pin.LocationAddress = locationAddress;
                pin.LastHeartbeatAt = now;
                await db.SaveChangesAsync(cancellationToken);
                
                await HydratePinAsync(pin, cancellationToken);
                
                if (meetId.HasValue)
                {
                    var meet = await db.Meets
                        .Include(m => m.Participants)
                        .FirstOrDefaultAsync(m => m.Id == meetId, cancellationToken);
                    if (meet != null)
                    {
                        pin.Meet = meet;
                        await meetSubscriptions.PublishAsync("pin_updated", meet, cancellationToken);
                    }
                }
                else
                {
                    await subscriptions.PublishAsync("pin_updated", pin, cancellationToken);
                }
                
                logger.LogInformation("Pin {PinId} updated in place (distance {Distance}m < {Threshold}m)", pin.Id, distance, TrailDistanceThresholdMeters);
                return pin;
            }

            pin.Status = LocationPinStatus.Offline;
            pin.ExpiresAt = now + DefaultOfflineTtl;
            await db.SaveChangesAsync(cancellationToken);
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

        await HydratePinAsync(pin, cancellationToken);

        if (meetId.HasValue)
        {
            var meet = await db.Meets
                .Include(m => m.Participants)
                .FirstOrDefaultAsync(m => m.Id == meetId, cancellationToken);
            if (meet != null)
            {
                pin.Meet = meet;
                await meetSubscriptions.PublishAsync("pin_updated", meet, cancellationToken);
                await ExtendMeetExpirationAsync(meetId.Value, cancellationToken);
            }
        }
        else
        {
            await subscriptions.PublishAsync("pin_updated", pin, cancellationToken);
        }

        return pin;
    }

    public async Task<bool> RemovePinAsync(Guid accountId, string deviceId, Guid? meetId = null, CancellationToken cancellationToken = default)
    {
        var pin = meetId.HasValue
            ? await db.LocationPins
                .FirstOrDefaultAsync(p => p.MeetId == meetId && p.AccountId == accountId && p.Status != LocationPinStatus.Removed, cancellationToken)
            : await db.LocationPins
                .FirstOrDefaultAsync(p => p.AccountId == accountId && p.DeviceId == deviceId && p.MeetId == null && p.Status == LocationPinStatus.Active, cancellationToken);

        if (pin == null)
        {
            return false;
        }

        var previousMeetId = pin.MeetId;
        _cache.TryRemove((accountId, deviceId), out _);
        _lastUpdateTimes.TryRemove(accountId, out _);

        await RemovePinInternalAsync(pin, cancellationToken);

        await eventBus.PublishAsync(new LocationPinRemovedEvent
        {
            PinId = pin.Id,
            AccountId = accountId
        }, cancellationToken);

        if (previousMeetId.HasValue)
        {
            var meet = await db.Meets
                .Include(m => m.Participants)
                .FirstOrDefaultAsync(m => m.Id == previousMeetId, cancellationToken);
            if (meet != null)
            {
                pin.Meet = meet;
                await meetSubscriptions.PublishAsync("pin_removed", meet, cancellationToken);
            }
        }
        else
        {
            await subscriptions.PublishAsync("pin_removed", pin, cancellationToken);
        }

        logger.LogInformation("Removed location pin {PinId} for account {AccountId}", pin.Id, accountId);

        return true;
    }

    public async Task DisconnectPinAsync(
        Guid accountId,
        string deviceId,
        bool keepOnDisconnect,
        Guid? meetId = null,
        CancellationToken cancellationToken = default
    )
    {
        var pin = meetId.HasValue
            ? await db.LocationPins
                .FirstOrDefaultAsync(p => p.MeetId == meetId && p.AccountId == accountId && p.Status != LocationPinStatus.Removed, cancellationToken)
            : await db.LocationPins
                .FirstOrDefaultAsync(p => p.AccountId == accountId && p.DeviceId == deviceId && p.MeetId == null && p.Status == LocationPinStatus.Active, cancellationToken);

        if (pin == null)
        {
            return;
        }

        await FlushPinToDbAsync(accountId, deviceId, cancellationToken);

        if (!keepOnDisconnect)
        {
            await RemovePinAsync(accountId, deviceId, meetId, cancellationToken);
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

        await HydratePinAsync(pin, cancellationToken);

        if (meetId.HasValue)
        {
            var meet = await db.Meets
                .Include(m => m.Participants)
                .FirstOrDefaultAsync(m => m.Id == meetId, cancellationToken);
            if (meet != null)
            {
                pin.Meet = meet;
                await meetSubscriptions.PublishAsync("pin_offline", meet, cancellationToken);
            }
        }
        else
        {
            await subscriptions.PublishAsync("pin_offline", pin, cancellationToken);
        }

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

        await HydratePinAsync(result, cancellationToken);

        return result;
    }

    public async Task<List<SnLocationPin>> ListMeetPinsAsync(
        Guid meetId,
        Guid requesterId,
        CancellationToken cancellationToken = default
    )
    {
        var pins = await db.LocationPins
            .Where(p => p.MeetId == meetId && p.Status == LocationPinStatus.Active)
            .ToListAsync(cancellationToken);

        var result = new List<SnLocationPin>();
        foreach (var pin in pins)
        {
            pin.Account = await accounts.GetAccount(pin.AccountId);
            result.Add(pin);
        }

        return result;
    }

    public async Task<List<SnLocationPin>> ListMeetTrailPinsAsync(
        Guid meetId,
        Guid requesterId,
        int offset = 0,
        int take = 50,
        CancellationToken cancellationToken = default
    )
    {
        var pins = await db.LocationPins
            .Where(p => p.MeetId == meetId && p.Status == LocationPinStatus.Offline)
            .OrderBy(p => p.LastHeartbeatAt)
            .Skip(offset)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(cancellationToken);

        var result = new List<SnLocationPin>();
        foreach (var pin in pins)
        {
            pin.Account = await accounts.GetAccount(pin.AccountId);
            result.Add(pin);
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

    private static double CalculateDistanceMeters(Geometry? from, Geometry? to)
    {
        if (from == null || to == null) return double.MaxValue;

        var lat1 = from.Coordinate?.Y ?? 0;
        var lon1 = from.Coordinate?.X ?? 0;
        var lat2 = to.Coordinate?.Y ?? 0;
        var lon2 = to.Coordinate?.X ?? 0;

        var dLat = lat2 - lat1;
        var dLon = (lon2 - lon1) * Math.Cos(lat1 * Math.PI / 180.0);

        var distanceDegrees = Math.Sqrt(dLat * dLat + dLon * dLon);
        return distanceDegrees * MetersPerDegreeLatitude;
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

    private async Task HydratePinAsync(SnLocationPin pin, CancellationToken cancellationToken)
    {
        pin.Account = await accounts.GetAccount(pin.AccountId);
    }

    private async Task HydratePinAsync(IEnumerable<SnLocationPin> pins, CancellationToken cancellationToken)
    {
        foreach (var pin in pins)
        {
            await HydratePinAsync(pin, cancellationToken);
        }
    }

    private async Task ExtendMeetExpirationAsync(Guid meetId, CancellationToken cancellationToken)
    {
        var meet = await db.Meets
            .FirstOrDefaultAsync(m => m.Id == meetId && m.Status == MeetStatus.Active, cancellationToken);
        
        if (meet is null)
            return;

        var now = SystemClock.Instance.GetCurrentInstant();
        var newExpiresAt = now + MeetActivityExtension;

        if (newExpiresAt > meet.ExpiresAt)
        {
            meet.ExpiresAt = newExpiresAt;
            await db.SaveChangesAsync(cancellationToken);
            expirationScheduler.Extend(meetId, newExpiresAt);
            
            logger.LogInformation("Extended meet {MeetId} expiration to {ExpiresAt} due to pin activity", meetId, newExpiresAt);
        }
    }
}
