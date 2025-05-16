using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NodaTime;

namespace DysonNetwork.Sphere.Account;

public class RelationshipService(AppDatabase db, IMemoryCache cache)
{
    public async Task<bool> HasExistingRelationship(Guid accountId, Guid relatedId)
    {
        var count = await db.AccountRelationships
            .Where(r => (r.AccountId == accountId && r.RelatedId == relatedId) ||
                        (r.AccountId == relatedId && r.AccountId == accountId))
            .CountAsync();
        return count > 0;
    }

    public async Task<Relationship?> GetRelationship(
        Guid accountId,
        Guid relatedId,
        RelationshipStatus? status = null,
        bool ignoreExpired = false
    )
    {
        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
        var queries = db.AccountRelationships.AsQueryable()
            .Where(r => r.AccountId == accountId && r.RelatedId == relatedId);
        if (!ignoreExpired) queries = queries.Where(r => r.ExpiredAt > now);
        if (status is not null) queries = queries.Where(r => r.Status == status);
        var relationship = await queries.FirstOrDefaultAsync();
        return relationship;
    }

    public async Task<Relationship> CreateRelationship(Account sender, Account target, RelationshipStatus status)
    {
        if (status == RelationshipStatus.Pending)
            throw new InvalidOperationException(
                "Cannot create relationship with pending status, use SendFriendRequest instead.");
        if (await HasExistingRelationship(sender.Id, target.Id))
            throw new InvalidOperationException("Found existing relationship between you and target user.");

        var relationship = new Relationship
        {
            AccountId = sender.Id,
            RelatedId = target.Id,
            Status = status
        };

        db.AccountRelationships.Add(relationship);
        await db.SaveChangesAsync();

        cache.Remove($"UserFriends_{relationship.AccountId}");
        cache.Remove($"UserFriends_{relationship.RelatedId}");

        return relationship;
    }

    public async Task<Relationship> BlockAccount(Account sender, Account target)
    {
        if (await HasExistingRelationship(sender.Id, target.Id))
            return await UpdateRelationship(sender.Id, target.Id, RelationshipStatus.Blocked);
        return await CreateRelationship(sender, target, RelationshipStatus.Blocked);
    }

    public async Task<Relationship> SendFriendRequest(Account sender, Account target)
    {
        if (await HasExistingRelationship(sender.Id, target.Id))
            throw new InvalidOperationException("Found existing relationship between you and target user.");

        var relationship = new Relationship
        {
            AccountId = sender.Id,
            RelatedId = target.Id,
            Status = RelationshipStatus.Pending,
            ExpiredAt = Instant.FromDateTimeUtc(DateTime.UtcNow.AddDays(7))
        };

        db.AccountRelationships.Add(relationship);
        await db.SaveChangesAsync();

        return relationship;
    }

    public async Task<Relationship> AcceptFriendRelationship(
        Relationship relationship,
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

        var relationshipBackward = new Relationship
        {
            AccountId = relationship.RelatedId,
            RelatedId = relationship.AccountId,
            Status = status
        };
        db.AccountRelationships.Add(relationshipBackward);

        await db.SaveChangesAsync();

        cache.Remove($"UserFriends_{relationship.AccountId}");
        cache.Remove($"UserFriends_{relationship.RelatedId}");

        return relationshipBackward;
    }

    public async Task<Relationship> UpdateRelationship(Guid accountId, Guid relatedId, RelationshipStatus status)
    {
        var relationship = await GetRelationship(accountId, relatedId, status);
        if (relationship is null) throw new ArgumentException("There is no relationship between you and the user.");
        if (relationship.Status == status) return relationship;
        relationship.Status = status;
        db.Update(relationship);
        await db.SaveChangesAsync();
        cache.Remove($"UserFriends_{accountId}");
        cache.Remove($"UserFriends_{relatedId}");
        return relationship;
    }

    public async Task<List<Guid>> ListAccountFriends(Account account)
    {
        if (!cache.TryGetValue($"UserFriends_{account.Id}", out List<Guid>? friends))
        {
            friends = await db.AccountRelationships
                .Where(r => r.RelatedId == account.Id)
                .Where(r => r.Status == RelationshipStatus.Friends)
                .Select(r => r.AccountId)
                .ToListAsync();
            cache.Set($"UserFriends_{account.Id}", friends, TimeSpan.FromHours(1));
        }

        return friends ?? [];
    }

    public async Task<bool> HasRelationshipWithStatus(Guid accountId, Guid relatedId,
        RelationshipStatus status = RelationshipStatus.Friends)
    {
        var relationship = await GetRelationship(accountId, relatedId, status);
        return relationship is not null;
    }
}