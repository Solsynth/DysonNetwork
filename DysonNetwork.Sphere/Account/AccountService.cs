using Casbin;
using DysonNetwork.Sphere.Permission;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Account;

public class AccountService(AppDatabase db, PermissionService pm)
{
    public async Task<Account?> LookupAccount(string probe)
    {
        var account = await db.Accounts.Where(a => a.Name == probe).FirstOrDefaultAsync();
        if (account is not null) return account;

        var contact = await db.AccountContacts
            .Where(c => c.Content == probe)
            .Include(c => c.Account)
            .FirstOrDefaultAsync();
        if (contact is not null) return contact.Account;

        return null;
    }

    public async Task<bool> HasExistingRelationship(Account userA, Account userB)
    {
        var count = await db.AccountRelationships
            .Where(r => (r.AccountId == userA.Id && r.AccountId == userB.Id) ||
                        (r.AccountId == userB.Id && r.AccountId == userA.Id))
            .CountAsync();
        return count > 0;
    }

    public async Task<Relationship?> GetRelationship(
        Account account,
        Account related,
        RelationshipStatus? status,
        bool ignoreExpired = false
    )
    {
        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
        var queries = db.AccountRelationships
            .Where(r => r.AccountId == account.Id && r.AccountId == related.Id);
        if (ignoreExpired) queries = queries.Where(r => r.ExpiredAt > now);
        if (status is not null) queries = queries.Where(r => r.Status == status);
        var relationship = await queries.FirstOrDefaultAsync();
        return relationship;
    }

    public async Task<Relationship> CreateRelationship(Account sender, Account target, RelationshipStatus status)
    {
        if (status == RelationshipStatus.Pending)
            throw new InvalidOperationException(
                "Cannot create relationship with pending status, use SendFriendRequest instead.");
        if (await HasExistingRelationship(sender, target))
            throw new InvalidOperationException("Found existing relationship between you and target user.");

        var relationship = new Relationship
        {
            Account = sender,
            AccountId = sender.Id,
            Related = target,
            RelatedId = target.Id,
            Status = status
        };

        db.AccountRelationships.Add(relationship);
        await db.SaveChangesAsync();
        await ApplyRelationshipPermissions(relationship);

        return relationship;
    }

    public async Task<Relationship> SendFriendRequest(Account sender, Account target)
    {
        if (await HasExistingRelationship(sender, target))
            throw new InvalidOperationException("Found existing relationship between you and target user.");

        var relationship = new Relationship
        {
            Account = sender,
            AccountId = sender.Id,
            Related = target,
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
        if (relationship.Status == RelationshipStatus.Pending)
            throw new ArgumentException("Cannot accept friend request by setting the new status to pending.");

        // Whatever the receiver decides to apply which status to the relationship,
        // the sender should always see the user as a friend since the sender ask for it
        relationship.Status = RelationshipStatus.Friends;
        relationship.ExpiredAt = null;
        db.Update(relationship);

        var relationshipBackward = new Relationship
        {
            Account = relationship.Related,
            AccountId = relationship.RelatedId,
            Related = relationship.Account,
            RelatedId = relationship.AccountId,
            Status = status
        };
        db.AccountRelationships.Add(relationshipBackward);

        await db.SaveChangesAsync();

        await Task.WhenAll(
            ApplyRelationshipPermissions(relationship),
            ApplyRelationshipPermissions(relationshipBackward)
        );

        return relationshipBackward;
    }

    public async Task<Relationship> UpdateRelationship(Account account, Account related, RelationshipStatus status)
    {
        var relationship = await GetRelationship(account, related, status);
        if (relationship is null) throw new ArgumentException("There is no relationship between you and the user.");
        if (relationship.Status == status) return relationship;
        relationship.Status = status;
        db.Update(relationship);
        await db.SaveChangesAsync();
        await ApplyRelationshipPermissions(relationship);
        return relationship;
    }

    private async Task ApplyRelationshipPermissions(Relationship relationship)
    {
        // Apply the relationship permissions to casbin enforcer
        // domain: the user
        // status is friends: all permissions are allowed by default, expect specially specified
        // status is blocked: all permissions are disallowed by default, expect specially specified
        // others: use the default permissions by design

        var domain = $"user:{relationship.AccountId.ToString()}";
        var target = $"user:{relationship.RelatedId.ToString()}";

        await pm.RemovePermissionNode(target, domain, "*");

        bool? value = relationship.Status switch
        {
            RelationshipStatus.Friends => true,
            RelationshipStatus.Blocked => false,
            _ => null,
        };
        if (value is null) return;

        await pm.AddPermissionNode(target, domain, "*", value);
    }
}