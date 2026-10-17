using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;

namespace DysonNetwork.Passport.Account;

/// <summary>
/// Relationship reads. The <c>account_relationships</c> table is owned by
/// Stargate now, so this service is a thin client over the
/// <see cref="StargateProfileClient"/> instead of talking to Passport's own
/// database. The six <c>DyProfileService</c> relationship RPCs carry the same
/// semantics the local queries used to implement.
/// </summary>
public class RelationshipService(StargateProfileClient stargateProfiles)
{
    public async Task<bool> HasExistingRelationship(Guid accountId, Guid relatedId)
    {
        if (accountId == Guid.Empty || relatedId == Guid.Empty)
            throw new ArgumentException("Account IDs cannot be empty.");
        if (accountId == relatedId)
            return false; // Prevent self-relationships

        var response = await stargateProfiles.HasRelationshipAsync(new DyGetRelationshipRequest
        {
            AccountId = accountId.ToString(),
            RelatedId = relatedId.ToString()
        });
        return response.Value;
    }

    public async Task<bool> HasRelationshipWithStatus(Guid accountId, Guid relatedId,
        RelationshipStatus status = RelationshipStatus.Friends)
    {
        if (accountId == Guid.Empty || relatedId == Guid.Empty)
            throw new ArgumentException("Account IDs cannot be empty.");

        var response = await stargateProfiles.HasRelationshipAsync(new DyGetRelationshipRequest
        {
            AccountId = accountId.ToString(),
            RelatedId = relatedId.ToString(),
            Status = (int)status
        });
        return response.Value;
    }

    public async Task<bool> IsBlockedEitherDirection(Guid userId, Guid otherId)
    {
        if (userId == Guid.Empty || otherId == Guid.Empty)
            return false;
        if (userId == otherId)
            return false;

        var response = await stargateProfiles.HasRelationshipAsync(new DyGetRelationshipRequest
        {
            AccountId = userId.ToString(),
            RelatedId = otherId.ToString(),
            Status = (int)RelationshipStatus.Blocked,
            EitherDirection = true
        });
        return response.Value;
    }

    public async Task<List<Guid>> ListAccountFriends(SnAccount account, bool isRelated = false)
    {
        return await ListAccountFriends(account.Id, isRelated);
    }

    public async Task<List<Guid>> ListAccountFriends(Guid accountId, bool isRelated = false)
    {
        return await ListRelationshipsAsync(
            accountId,
            isRelated,
            request => stargateProfiles.ListFriendsAsync(request).ResponseAsync
        );
    }

    public async Task<List<Guid>> ListAccountBlocked(SnAccount account, bool isRelated = false)
    {
        return await ListAccountBlocked(account.Id, isRelated);
    }

    public async Task<List<Guid>> ListAccountBlocked(Guid accountId, bool isRelated = false)
    {
        return await ListRelationshipsAsync(
            accountId,
            isRelated,
            request => stargateProfiles.ListBlockedAsync(request).ResponseAsync
        );
    }

    public async Task<List<Guid>> ListAllBlockedAccountIds(Guid accountId)
    {
        var outgoing = await ListAccountBlocked(accountId);
        var incoming = await ListAccountBlocked(accountId, true);
        return outgoing.Concat(incoming).Distinct().ToList();
    }

    public async Task<List<Guid>> ListAccountMuted(Guid accountId)
    {
        return await ListRelationshipsAsync(
            accountId,
            isRelated: false,
            request => stargateProfiles.ListMutedAsync(request).ResponseAsync
        );
    }

    public async Task<List<Guid>> ListCloseFriends(Guid accountId)
    {
        return await ListRelationshipsAsync(
            accountId,
            isRelated: false,
            request => stargateProfiles.ListCloseFriendsAsync(request).ResponseAsync
        );
    }

    public async Task<bool> IsCloseFriend(Guid userId, Guid otherId)
    {
        if (userId == Guid.Empty || otherId == Guid.Empty)
            return false;
        if (userId == otherId)
            return false;

        return await HasRelationshipWithStatus(userId, otherId, RelationshipStatus.CloseFriend);
    }

    /// <summary>
    /// Calls one of the relationship list RPCs. <paramref name="isRelated"/> selects
    /// the reverse direction (rows where the account is the related side).
    /// </summary>
    private static async Task<List<Guid>> ListRelationshipsAsync(
        Guid accountId,
        bool isRelated,
        Func<DyListRelationshipSimpleRequest, Task<DyListRelationshipSimpleResponse>> call
    )
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID cannot be empty.");

        var request = new DyListRelationshipSimpleRequest();
        if (isRelated)
            request.RelatedId = accountId.ToString();
        else
            request.AccountId = accountId.ToString();

        var response = await call(request);
        return response.AccountsId.Select(Guid.Parse).ToList();
    }
}