using System.Security.Cryptography;
using System.Text;
using DysonNetwork.Passport.Account;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Passport.Nearby;

public class NearbyPresenceTokenResult
{
    public long Slot { get; set; }
    public string Token { get; set; } = string.Empty;
    public Instant ValidFrom { get; set; }
    public Instant ValidTo { get; set; }
}

public class NearbyResolvedPeer
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public SnCloudFileReferenceObject? Avatar { get; set; }
    public bool IsFriend { get; set; }
    public bool CanInvite { get; set; }
    public string Visibility { get; set; } = "friend_only";
    public Instant LastSeenAt { get; set; }
}

public class NearbyObservation
{
    public string Token { get; set; } = string.Empty;
    public long Slot { get; set; }
    public int AvgRssi { get; set; }
    public int SeenCount { get; set; }
    public long DurationMs { get; set; }
    public Instant? FirstSeenAt { get; set; }
    public Instant? LastSeenAt { get; set; }
}

public class NearbyService(
    AppDatabase db,
    IConfiguration configuration,
    AccountService accounts,
    RelationshipService relationships,
    ILogger<NearbyService> logger
)
{
    public const string DefaultServiceUuid = "FFF0";
    public const int DefaultSlotDurationSec = 30;
    public const int DefaultPrefetchSlots = 10;
    public const int MaxPrefetchSlots = 30;
    public const int TokenBytes = 16;
    private static readonly Duration AllowedClockSkew = Duration.FromMinutes(2);

    private string ServiceUuid => configuration["Nearby:ServiceUuid"] ?? DefaultServiceUuid;
    private int SlotDurationSec => Math.Clamp(configuration.GetValue<int?>("Nearby:SlotDurationSec") ?? DefaultSlotDurationSec, 15, 300);
    private string TokenSecret => configuration["Nearby:PresenceTokenSecret"] ?? "dev-nearby-secret-change-me";

    public async Task<List<NearbyPresenceTokenResult>> IssuePresenceTokensAsync(
        Guid userId,
        string deviceId,
        bool discoverable,
        bool friendOnly,
        int capabilities,
        int prefetchSlots,
        CancellationToken cancellationToken = default
    )
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var normalizedPrefetch = Math.Clamp(prefetchSlots, 1, MaxPrefetchSlots);
        var currentSlot = GetSlot(now);

        var device = await db.NearbyDevices
            .FirstOrDefaultAsync(d => d.UserId == userId && d.DeviceId == deviceId, cancellationToken);

        if (device is null)
        {
            device = new SnNearbyDevice
            {
                UserId = userId,
                DeviceId = deviceId
            };
            db.NearbyDevices.Add(device);
        }

        var settingsChanged = device.Discoverable != discoverable
            || device.FriendOnly != friendOnly
            || device.Capabilities != capabilities
            || device.Status != NearbyDeviceStatus.Active;

        device.Discoverable = discoverable;
        device.FriendOnly = friendOnly;
        device.Capabilities = capabilities;
        device.Status = NearbyDeviceStatus.Active;
        device.LastHeartbeatAt = now;
        device.LastTokenIssuedAt = now;

        if (settingsChanged)
        {
            await db.NearbyPresenceTokens
                .Where(t => t.DeviceId == device.Id && t.Slot >= currentSlot)
                .ExecuteDeleteAsync(cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        var existingSlots = await db.NearbyPresenceTokens
            .Where(t => t.DeviceId == device.Id && t.Slot >= currentSlot && t.Slot < currentSlot + normalizedPrefetch)
            .Select(t => t.Slot)
            .ToListAsync(cancellationToken);
        var existingSet = existingSlots.ToHashSet();

        var results = new List<NearbyPresenceTokenResult>();
        for (var i = 0; i < normalizedPrefetch; i++)
        {
            var slot = currentSlot + i;
            var validFrom = Instant.FromUnixTimeSeconds(slot * SlotDurationSec);
            var validTo = validFrom + Duration.FromSeconds(SlotDurationSec);
            var token = GenerateToken(userId, deviceId, slot, friendOnly, discoverable, capabilities);
            var tokenHash = ComputeHash(token);

            if (!existingSet.Contains(slot))
            {
                db.NearbyPresenceTokens.Add(new SnNearbyPresenceToken
                {
                    DeviceId = device.Id,
                    Slot = slot,
                    TokenHash = tokenHash,
                    ValidFrom = validFrom,
                    ValidTo = validTo,
                    Discoverable = discoverable,
                    FriendOnly = friendOnly,
                    Capabilities = capabilities
                });
            }

            results.Add(new NearbyPresenceTokenResult
            {
                Slot = slot,
                Token = token,
                ValidFrom = validFrom,
                ValidTo = validTo
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        return results;
    }

    public async Task<List<NearbyResolvedPeer>> ResolveAsync(
        Guid observerUserId,
        List<NearbyObservation> observations,
        CancellationToken cancellationToken = default
    )
    {
        if (observations.Count == 0) return [];

        var now = SystemClock.Instance.GetCurrentInstant();
        var distinctObservations = observations
            .Where(o => !string.IsNullOrWhiteSpace(o.Token))
            .GroupBy(o => $"{o.Slot}:{o.Token}", StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(x => x.SeenCount).ThenByDescending(x => x.LastSeenAt ?? now).First())
            .ToList();

        var tokenHashes = distinctObservations.Select(o => ComputeHash(o.Token)).Distinct().ToList();
        var slots = distinctObservations.Select(o => o.Slot).Distinct().ToList();

        var matches = await db.NearbyPresenceTokens
            .Include(t => t.Device)
            .Where(t => tokenHashes.Contains(t.TokenHash))
            .Where(t => slots.Contains(t.Slot))
            .Where(t => t.ValidFrom <= now + AllowedClockSkew && t.ValidTo >= now - AllowedClockSkew)
            .Where(t => t.Device.Status == NearbyDeviceStatus.Active && t.Device.Discoverable)
            .ToListAsync(cancellationToken);

        var result = new List<(NearbyResolvedPeer Peer, int Rssi)>();
        foreach (var observation in distinctObservations)
        {
            var tokenHash = ComputeHash(observation.Token);
            var match = matches.FirstOrDefault(t => t.TokenHash == tokenHash && t.Slot == observation.Slot);
            if (match is null) continue;
            if (match.Device.UserId == observerUserId) continue;

            var isFriend = await relationships.HasRelationshipWithStatus(observerUserId, match.Device.UserId);
            if (match.FriendOnly && !isFriend)
                continue;

            var blocked = await relationships.HasRelationshipWithStatus(observerUserId, match.Device.UserId, RelationshipStatus.Blocked)
                || await relationships.HasRelationshipWithStatus(match.Device.UserId, observerUserId, RelationshipStatus.Blocked);
            if (blocked)
                continue;

            var account = await accounts.GetAccount(match.Device.UserId);
            if (account is null)
                continue;

            result.Add((new NearbyResolvedPeer
            {
                UserId = account.Id,
                DisplayName = string.IsNullOrWhiteSpace(account.Nick) ? account.Name : account.Nick,
                Avatar = account.Profile?.Picture,
                IsFriend = isFriend,
                CanInvite = true,
                Visibility = match.FriendOnly ? "friend_only" : "public",
                LastSeenAt = observation.LastSeenAt ?? now
            }, observation.AvgRssi));
        }

        return result
            .GroupBy(x => x.Peer.UserId)
            .Select(g => g.OrderByDescending(x => x.Rssi).First().Peer)
            .OrderByDescending(x => x.LastSeenAt)
            .ToList();
    }

    public string GetServiceUuid() => ServiceUuid;
    public int GetSlotDurationSec() => SlotDurationSec;

    private long GetSlot(Instant instant) => instant.ToUnixTimeSeconds() / SlotDurationSec;

    private string GenerateToken(Guid userId, string deviceId, long slot, bool friendOnly, bool discoverable, int capabilities)
    {
        var material = $"{userId:N}:{deviceId}:{slot}:{(friendOnly ? 1 : 0)}:{(discoverable ? 1 : 0)}:{capabilities}";
        var secret = Encoding.UTF8.GetBytes(TokenSecret);
        using var hmac = new HMACSHA256(secret);
        var full = hmac.ComputeHash(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(full[..TokenBytes]);
    }

    private static string ComputeHash(string token)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(token.Trim())));
    }
}
