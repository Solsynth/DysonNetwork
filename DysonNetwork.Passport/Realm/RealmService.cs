using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using NodaTime;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Shared.Localization;

namespace DysonNetwork.Passport.Realm;

public class RealmService(
    AppDatabase db,
    DyRingService.DyRingServiceClient pusher,
    ILocalizationService localizer,
    ICacheService cache,
    DyAccountService.DyAccountServiceClient accountGrpc
)
{
    private const string CacheKeyPrefix = "account:realms:";

    public async Task<SnRealm?> GetBySlug(string slug)
    {
        var realm = await db.Realms.FirstOrDefaultAsync(r => r.Slug == slug);
        if (realm is null) return null;

        await RefreshBoostState(realm);
        return realm;
    }

    public async Task RefreshBoostState(SnRealm realm, CancellationToken cancellationToken = default)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var cutoff = RealmBoostPolicy.GetActiveCutoff(now);
        var activeContributions = await db.RealmBoostContributions
            .Where(c => c.RealmId == realm.Id && c.CreatedAt >= cutoff)
            .ToListAsync(cancellationToken);
        var activeBoostPoints = activeContributions.Sum(c => c.Shares);

        realm.BoostPoints = activeBoostPoints;
    }

    public async Task RefreshBoostStates(ICollection<SnRealm> realms, CancellationToken cancellationToken = default)
    {
        if (realms.Count == 0) return;

        var realmIds = realms.Select(r => r.Id).Distinct().ToList();
        var cutoff = RealmBoostPolicy.GetActiveCutoff(SystemClock.Instance.GetCurrentInstant());
        var activeContributions = await db.RealmBoostContributions
            .Where(c => realmIds.Contains(c.RealmId) && c.CreatedAt >= cutoff)
            .ToListAsync(cancellationToken);
        var boostPoints = activeContributions
            .GroupBy(c => c.RealmId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Shares));

        foreach (var realm in realms)
            realm.BoostPoints = boostPoints.GetValueOrDefault(realm.Id, 0m);
    }

    public async Task<SnRealmMember?> GetActiveMember(Guid realmId, Guid accountId)
    {
        return await db.RealmMembers
            .Include(m => m.Label)
            .Include(m => m.Realm)
            .Where(m => m.RealmId == realmId && m.AccountId == accountId && m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();
    }

    public void EnsureBoostUnlocked(SnRealm realm, int requiredLevel, string message)
    {
        if (realm.BoostLevel < requiredLevel)
            throw new InvalidOperationException(message);
    }

    public async Task<int> GetRealmLabelCount(Guid realmId)
    {
        return await db.RealmLabels.Where(l => l.RealmId == realmId).CountAsync();
    }

    public async Task<List<Guid>> GetUserRealms(Guid accountId)
    {
        var cacheKey = $"{CacheKeyPrefix}{accountId}";
        var (found, cachedRealms) = await cache.GetAsyncWithStatus<List<Guid>>(cacheKey);
        if (found && cachedRealms != null)
            return cachedRealms;

        var realms = await db.RealmMembers
            .Include(m => m.Realm)
            .Where(m => m.AccountId == accountId)
            .Where(m => m.JoinedAt != null && m.LeaveAt == null)
            .Select(m => m.Realm!.Id)
            .ToListAsync();

        // Cache the result for 5 minutes
        await cache.SetAsync(cacheKey, realms, TimeSpan.FromMinutes(5));

        return realms;
    }

    public async Task SendInviteNotify(SnRealmMember member)
    {
        var account = await accountGrpc.GetAccountAsync(new DyGetAccountRequest { Id = member.AccountId.ToString() });
        var modelAccount = SnAccount.FromProtoValue(account);

        if (modelAccount == null) throw new InvalidOperationException("Account not found");

        await pusher.SendPushNotificationToUserAsync(
            new DySendPushNotificationToUserRequest
            {
                UserId = modelAccount.Id.ToString(),
                Notification = new DyPushNotification
                {
                    Topic = "invites.realms",
                    Title = localizer.Get("realmInviteTitle", modelAccount.Language),
                    Body = localizer.Get("realmInviteBody", locale: modelAccount.Language, args: new { realmName = member.Realm.Name }),
                    ActionUri = "/realms",
                    IsSavable = true
                }
            }
        );
    }

    public async Task<bool> IsMemberWithRole(Guid realmId, Guid accountId, params int[] requiredRoles)
    {
        if (requiredRoles.Length == 0)
            return false;

        var maxRequiredRole = requiredRoles.Max();
        var member = await db.RealmMembers
            .Where(m => m.RealmId == realmId && m.AccountId == accountId && m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();
        return member?.Role >= maxRequiredRole;
    }

    public async Task<SnRealmMember> LoadMemberAccount(SnRealmMember member)
    {
        if (member.JoinedAt != null && member.LeaveAt == null)
        {
            var actualMember = await GetActiveMember(member.RealmId, member.AccountId);
            if (actualMember is not null)
                member = actualMember;
        }
        else if (member.JoinedAt == null && member.LeaveAt == null && member.Role == 0)
        {
            var actualMember = await GetActiveMember(member.RealmId, member.AccountId);
            if (actualMember is not null)
                member = actualMember;
        }

        try
        {
            var account = SnAccount.FromProtoValue(
                await accountGrpc.GetAccountAsync(new DyGetAccountRequest { Id = member.AccountId.ToString() })
            );
            account.Profile = await db.AccountProfiles.FirstOrDefaultAsync(p => p.AccountId == member.AccountId);
            member.Account = account;
        }
        catch
        {
            // Keep member without account payload if remote account no longer exists.
        }

        return member;
    }

    public async Task<List<SnRealmMember>> LoadMemberAccounts(ICollection<SnRealmMember> members)
    {
        var incomingMembers = members.ToList();
        if (incomingMembers.Count == 0) return [];

        var groupedRealmIds = incomingMembers.Select(m => m.RealmId).Distinct().ToList();
        var accountIds = incomingMembers.Select(m => m.AccountId).Distinct().ToList();

        var dbMembers = await db.RealmMembers
            .Include(m => m.Label)
            .Include(m => m.Realm)
            .Where(m => groupedRealmIds.Contains(m.RealmId))
            .Where(m => accountIds.Contains(m.AccountId))
            .Where(m => m.LeaveAt == null)
            .ToListAsync();
        var dbMemberMap = dbMembers.ToDictionary(m => (m.RealmId, m.AccountId), m => m);

        var accounts = await accountGrpc.GetAccountBatchAsync(new DyGetAccountBatchRequest
        {
            Id = { accountIds.Select(x => x.ToString()) }
        });
        var accountsDict = accounts.Accounts
            .Select(SnAccount.FromProtoValue)
            .ToDictionary(a => a.Id, a => a);
        var profiles = await db.AccountProfiles
            .Where(p => accountIds.Contains(p.AccountId))
            .ToDictionaryAsync(p => p.AccountId, p => p);

        return incomingMembers.Select(m =>
        {
            if (dbMemberMap.TryGetValue((m.RealmId, m.AccountId), out var dbMember))
            {
                m = dbMember;
            }
            if (accountsDict.TryGetValue(m.AccountId, out var account))
            {
                if (profiles.TryGetValue(m.AccountId, out var profile))
                    account.Profile = profile;
                m.Account = account;
            }
            return m;
        }).ToList();
    }

    #region Permission Methods

    public async Task<bool> HasPermission(Guid realmId, Guid accountId, string permission)
    {
        // Check user-specific override first
        var userPermission = await db.RealmUserPermissions
            .FirstOrDefaultAsync(p => p.RealmId == realmId && p.AccountId == accountId && p.DeletedAt == null);
        
        if (userPermission != null)
        {
            var userValue = permission switch
            {
                "chat.send" => userPermission.CanChat,
                "post.create" => userPermission.CanPost,
                "post.comment" => userPermission.CanComment,
                "media.upload" => userPermission.CanUploadMedia,
                "post.moderate" => userPermission.CanModeratePosts,
                "chat.moderate" => userPermission.CanModerateChat,
                "members.manage" => userPermission.CanManageMembers,
                "realm.manage" => userPermission.CanManageRealm,
                _ => (bool?)null
            };
            
            if (userValue.HasValue)
                return userValue.Value;
        }
        
        return await GetRolePermission(realmId, accountId, permission);
    }

    private async Task<bool> GetRolePermission(Guid realmId, Guid accountId, string permission)
    {
        var member = await GetActiveMember(realmId, accountId);
        if (member == null) return false;
        
        var rolePermission = await db.RealmRolePermissions
            .FirstOrDefaultAsync(p => p.RealmId == realmId && p.RoleLevel == member.Role && p.DeletedAt == null);
        
        if (rolePermission != null)
        {
            return permission switch
            {
                "chat.send" => rolePermission.CanChat,
                "post.create" => rolePermission.CanPost,
                "post.comment" => rolePermission.CanComment,
                "media.upload" => rolePermission.CanUploadMedia,
                "post.moderate" => rolePermission.CanModeratePosts,
                "chat.moderate" => rolePermission.CanModerateChat,
                "members.manage" => rolePermission.CanManageMembers,
                "realm.manage" => rolePermission.CanManageRealm,
                _ => false
            };
        }
        
        return GetDefaultPermission(member.Role, permission);
    }

    public bool GetDefaultPermission(int role, string permission)
    {
        // Default permissions for each role
        return role switch
        {
            RealmMemberRole.Owner => true,
            RealmMemberRole.Moderator => permission switch
            {
                "chat.send" => true,
                "post.create" => true,
                "post.comment" => true,
                "media.upload" => true,
                "post.moderate" => true,
                "chat.moderate" => true,
                "members.manage" => true,
                "realm.manage" => false,
                _ => false
            },
            RealmMemberRole.Normal => permission switch
            {
                "chat.send" => true,
                "post.create" => true,
                "post.comment" => true,
                "media.upload" => true,
                "post.moderate" => false,
                "chat.moderate" => false,
                "members.manage" => false,
                "realm.manage" => false,
                _ => false
            },
            _ => false
        };
    }

    public async Task<List<SnRealmRolePermission>> GetRolePermissions(Guid realmId)
    {
        return await db.RealmRolePermissions
            .Where(p => p.RealmId == realmId && p.DeletedAt == null)
            .OrderBy(p => p.RoleLevel)
            .ToListAsync();
    }

    public async Task<SnRealmRolePermission?> GetRolePermission(Guid realmId, int roleLevel)
    {
        return await db.RealmRolePermissions
            .FirstOrDefaultAsync(p => p.RealmId == realmId && p.RoleLevel == roleLevel && p.DeletedAt == null);
    }

    public async Task<SnRealmRolePermission> UpdateRolePermission(Guid realmId, int roleLevel, SnRealmRolePermission permission)
    {
        var existing = await db.RealmRolePermissions
            .FirstOrDefaultAsync(p => p.RealmId == realmId && p.RoleLevel == roleLevel && p.DeletedAt == null);
        
        if (existing != null)
        {
            existing.CanChat = permission.CanChat;
            existing.CanPost = permission.CanPost;
            existing.CanComment = permission.CanComment;
            existing.CanUploadMedia = permission.CanUploadMedia;
            existing.CanModeratePosts = permission.CanModeratePosts;
            existing.CanModerateChat = permission.CanModerateChat;
            existing.CanManageMembers = permission.CanManageMembers;
            existing.CanManageRealm = permission.CanManageRealm;
            
            db.RealmRolePermissions.Update(existing);
            await db.SaveChangesAsync();
            
            return existing;
        }
        
        permission.RealmId = realmId;
        permission.RoleLevel = roleLevel;
        
        db.RealmRolePermissions.Add(permission);
        await db.SaveChangesAsync();
        
        return permission;
    }

    public async Task<SnRealmUserPermission?> GetUserPermission(Guid realmId, Guid accountId)
    {
        return await db.RealmUserPermissions
            .FirstOrDefaultAsync(p => p.RealmId == realmId && p.AccountId == accountId && p.DeletedAt == null);
    }

    public async Task<SnRealmUserPermission> UpdateUserPermission(Guid realmId, Guid accountId, SnRealmUserPermission permission)
    {
        var existing = await db.RealmUserPermissions
            .FirstOrDefaultAsync(p => p.RealmId == realmId && p.AccountId == accountId && p.DeletedAt == null);
        
        if (existing != null)
        {
            existing.CanChat = permission.CanChat;
            existing.CanPost = permission.CanPost;
            existing.CanComment = permission.CanComment;
            existing.CanUploadMedia = permission.CanUploadMedia;
            existing.CanModeratePosts = permission.CanModeratePosts;
            existing.CanModerateChat = permission.CanModerateChat;
            existing.CanManageMembers = permission.CanManageMembers;
            existing.CanManageRealm = permission.CanManageRealm;
            
            db.RealmUserPermissions.Update(existing);
            await db.SaveChangesAsync();
            
            return existing;
        }
        
        permission.RealmId = realmId;
        permission.AccountId = accountId;
        
        db.RealmUserPermissions.Add(permission);
        await db.SaveChangesAsync();
        
        return permission;
    }

    public async Task<SnRealmPostModerationLog> ModeratePost(Guid realmId, Guid postId, Guid moderatorAccountId, string? reason)
    {
        // Check if post is already moderated
        var existingLog = await db.RealmPostModerationLogs
            .FirstOrDefaultAsync(l => l.PostId == postId && l.RealmId == realmId && l.DeletedAt == null);
        
        if (existingLog != null)
            throw new InvalidOperationException("This post has already been removed from the realm.");
        
        var log = new SnRealmPostModerationLog
        {
            RealmId = realmId,
            PostId = postId,
            ModeratorAccountId = moderatorAccountId,
            Reason = reason
        };
        
        db.RealmPostModerationLogs.Add(log);
        await db.SaveChangesAsync();
        
        return log;
    }

    public async Task<bool> IsPostModerated(Guid realmId, Guid postId)
    {
        return await db.RealmPostModerationLogs
            .AnyAsync(l => l.PostId == postId && l.RealmId == realmId && l.DeletedAt == null);
    }

    public async Task<List<SnRealmPostModerationLog>> GetModerationLogs(Guid realmId, int offset = 0, int take = 20)
    {
        return await db.RealmPostModerationLogs
            .Where(l => l.RealmId == realmId && l.DeletedAt == null)
            .OrderByDescending(l => l.ModeratedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();
    }

    public async Task<int> GetModerationLogsCount(Guid realmId)
    {
        return await db.RealmPostModerationLogs
            .CountAsync(l => l.RealmId == realmId && l.DeletedAt == null);
    }

    #endregion
}
