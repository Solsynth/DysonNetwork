using DysonNetwork.Pass.Localization;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using NodaTime;

namespace DysonNetwork.Pass.Account;

public class RelationshipService(
    AppDatabase db,
    ICacheService cache,
    RingService.RingServiceClient pusher,
    IStringLocalizer<NotificationResource> localizer
)
{
    private const string UserFriendsCacheKeyPrefix = "accounts:friends:";
    private const string UserBlockedCacheKeyPrefix = "accounts:blocked:";
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromHours(1);

    public async Task<bool> HasExistingRelationship(Guid accountId, Guid relatedId)
    {
        if (accountId == Guid.Empty || relatedId == Guid.Empty)
            throw new ArgumentException("Account IDs cannot be empty.");
        if (accountId == relatedId)
            return false; // Prevent self-relationships

        var count = await db.AccountRelationships
            .Where(r => (r.AccountId == accountId && r.RelatedId == relatedId) ||
                        (r.AccountId == relatedId && r.RelatedId == accountId))
            .CountAsync();
        return count > 0;
    }

    public async Task<SnAccountRelationship?> GetRelationship(
        Guid accountId,
        Guid relatedId,
        RelationshipStatus? status = null,
        bool ignoreExpired = false
    )
    {
        if (accountId == Guid.Empty || relatedId == Guid.Empty)
            throw new ArgumentException("Account IDs cannot be empty.");

        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
        var queries = db.AccountRelationships.AsQueryable()
            .Where(r => r.AccountId == accountId && r.RelatedId == relatedId);
        if (!ignoreExpired) queries = queries.Where(r => r.ExpiredAt == null || r.ExpiredAt > now);
        if (status is not null) queries = queries.Where(r => r.Status == status);
        var relationship = await queries.FirstOrDefaultAsync();
        return relationship;
    }

    public async Task<SnAccountRelationship> CreateRelationship(SnAccount sender, SnAccount target, RelationshipStatus status)
    {
        if (status == RelationshipStatus.Pending)
            throw new InvalidOperationException(
                "Cannot create relationship with pending status, use SendFriendRequest instead.");
        if (await HasExistingRelationship(sender.Id, target.Id))
            throw new InvalidOperationException("Found existing relationship between you and target user.");

        var relationship = new SnAccountRelationship
        {
            AccountId = sender.Id,
            RelatedId = target.Id,
            Status = status
        };

        db.AccountRelationships.Add(relationship);
        await db.SaveChangesAsync();

        await PurgeRelationshipCache(sender.Id, target.Id, status);

        return relationship;
    }

    public async Task<SnAccountRelationship> BlockAccount(SnAccount sender, SnAccount target)
    {
        if (await HasExistingRelationship(sender.Id, target.Id))
            return await UpdateRelationship(sender.Id, target.Id, RelationshipStatus.Blocked);
        return await CreateRelationship(sender, target, RelationshipStatus.Blocked);
    }

    public async Task<SnAccountRelationship> UnblockAccount(SnAccount sender, SnAccount target)
    {
        var relationship = await GetRelationship(sender.Id, target.Id, RelationshipStatus.Blocked);
        if (relationship is null) throw new ArgumentException("There is no relationship between you and the user.");
        db.Remove(relationship);
        await db.SaveChangesAsync();

        await PurgeRelationshipCache(sender.Id, target.Id, RelationshipStatus.Blocked);

        return relationship;
    }

    public async Task<SnAccountRelationship> SendFriendRequest(SnAccount sender, SnAccount target)
    {
        if (await HasExistingRelationship(sender.Id, target.Id))
            throw new InvalidOperationException("Found existing relationship between you and target user.");

        var relationship = new SnAccountRelationship
        {
            AccountId = sender.Id,
            RelatedId = target.Id,
            Status = RelationshipStatus.Pending,
            ExpiredAt = Instant.FromDateTimeUtc(DateTime.UtcNow.AddDays(7))
        };

        db.AccountRelationships.Add(relationship);
        await db.SaveChangesAsync();

        await pusher.SendPushNotificationToUserAsync(new SendPushNotificationToUserRequest
        {
            UserId = target.Id.ToString(),
            Notification = new PushNotification
            {
                Topic = "relationships.friends.request",
                Title = localizer["FriendRequestTitle", sender.Nick],
                Body = localizer["FriendRequestBody"],
                ActionUri = "/account/relationships",
                IsSavable = true
            }
        });

        await PurgeRelationshipCache(sender.Id, target.Id, RelationshipStatus.Pending);

        return relationship;
    }

    public async Task DeleteFriendRequest(Guid accountId, Guid relatedId)
    {
        if (accountId == Guid.Empty || relatedId == Guid.Empty)
            throw new ArgumentException("Account IDs cannot be empty.");

        var affectedRows = await db.AccountRelationships
            .Where(r => r.AccountId == accountId && r.RelatedId == relatedId && r.Status == RelationshipStatus.Pending)
            .ExecuteDeleteAsync();

        if (affectedRows == 0)
            throw new ArgumentException("Friend request was not found.");

        await PurgeRelationshipCache(accountId, relatedId, RelationshipStatus.Pending);
    }

    public async Task<SnAccountRelationship> AcceptFriendRelationship(
        SnAccountRelationship relationship,
        RelationshipStatus status = RelationshipStatus.Friends
    )
    {
        if (relationship.Status != RelationshipStatus.Pending)
            throw new ArgumentException("Cannot accept friend request that not in pending status.");
        if (status == RelationshipStatus.Pending)
            throw new ArgumentException("Cannot accept friend request by setting the new status to pending.");

        // Whatever the receiver decides to apply which status to the relationship,
        // the sender should always see the user as a friend since the sender ask for it
        relationship.Status = RelationshipStatus.Friends;
        relationship.ExpiredAt = null;
        db.Update(relationship);

        var relationshipBackward = new SnAccountRelationship
        {
            AccountId = relationship.RelatedId,
            RelatedId = relationship.AccountId,
            Status = status
        };
        db.AccountRelationships.Add(relationshipBackward);

        await db.SaveChangesAsync();

        await PurgeRelationshipCache(relationship.AccountId, relationship.RelatedId, RelationshipStatus.Friends, status);

        return relationshipBackward;
    }

    public async Task<SnAccountRelationship> UpdateRelationship(Guid accountId, Guid relatedId, RelationshipStatus status)
    {
        var relationship = await GetRelationship(accountId, relatedId);
        if (relationship is null) throw new ArgumentException("There is no relationship between you and the user.");
        if (relationship.Status == status) return relationship;
        var oldStatus = relationship.Status;
        relationship.Status = status;
        db.Update(relationship);
        await db.SaveChangesAsync();

        await PurgeRelationshipCache(accountId, relatedId, oldStatus, status);

        return relationship;
    }

    public async Task<List<Guid>> ListAccountFriends(SnAccount account)
    {
        return await ListAccountFriends(account.Id);
    }

    public async Task<List<Guid>> ListAccountFriends(Guid accountId)
    {
        return await GetCachedRelationships(accountId, RelationshipStatus.Friends, UserFriendsCacheKeyPrefix);
    }

    public async Task<List<Guid>> ListAccountBlocked(SnAccount account)
    {
        return await ListAccountBlocked(account.Id);
    }

    public async Task<List<Guid>> ListAccountBlocked(Guid accountId)
    {
        return await GetCachedRelationships(accountId, RelationshipStatus.Blocked, UserBlockedCacheKeyPrefix);
    }

    public async Task<bool> HasRelationshipWithStatus(Guid accountId, Guid relatedId,
        RelationshipStatus status = RelationshipStatus.Friends)
    {
        var relationship = await GetRelationship(accountId, relatedId, status);
        return relationship is not null;
    }

    private async Task<List<Guid>> GetCachedRelationships(Guid accountId, RelationshipStatus status, string cachePrefix)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID cannot be empty.");

        var cacheKey = $"{cachePrefix}{accountId}";
        var relationships = await cache.GetAsync<List<Guid>>(cacheKey);

        if (relationships == null)
        {
            var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
            relationships = await db.AccountRelationships
                .Where(r => r.RelatedId == accountId)
                .Where(r => r.Status == status)
                .Where(r => r.ExpiredAt == null || r.ExpiredAt > now)
                .Select(r => r.AccountId)
                .ToListAsync();

            await cache.SetAsync(cacheKey, relationships, CacheExpiration);
        }

        return relationships ?? new List<Guid>();
    }

    private async Task PurgeRelationshipCache(Guid accountId, Guid relatedId, params RelationshipStatus[] statuses)
    {
        if (statuses.Length == 0)
        {
            statuses = Enum.GetValues<RelationshipStatus>();
        }

        var keysToRemove = new List<string>();

        if (statuses.Contains(RelationshipStatus.Friends) || statuses.Contains(RelationshipStatus.Pending))
        {
            keysToRemove.Add($"{UserFriendsCacheKeyPrefix}{accountId}");
            keysToRemove.Add($"{UserFriendsCacheKeyPrefix}{relatedId}");
        }

        if (statuses.Contains(RelationshipStatus.Blocked))
        {
            keysToRemove.Add($"{UserBlockedCacheKeyPrefix}{accountId}");
            keysToRemove.Add($"{UserBlockedCacheKeyPrefix}{relatedId}");
        }

        var removeTasks = keysToRemove.Select(key => cache.RemoveAsync(key));
        await Task.WhenAll(removeTasks);
    }
}
