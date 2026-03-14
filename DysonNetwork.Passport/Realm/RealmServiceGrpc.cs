using DysonNetwork.Shared.Proto;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Localization;

namespace DysonNetwork.Passport.Realm;

public class RealmServiceGrpc(
    AppDatabase db,
    DyRingService.DyRingServiceClient pusher,
    ILocalizationService localizer,
    ICacheService cache,
    DyAccountService.DyAccountServiceClient accountGrpc,
    RealmService realmService
)
    : DyRealmService.DyRealmServiceBase
{
    private const string CacheKeyPrefix = "account:realms:";

    public override async Task<DyRealm> GetRealm(DyGetRealmRequest request, ServerCallContext context)
    {
        var realm = request.QueryCase switch
        {
            DyGetRealmRequest.QueryOneofCase.Id when !string.IsNullOrWhiteSpace(request.Id) => await db.Realms.FindAsync(
                Guid.Parse(request.Id)),
            DyGetRealmRequest.QueryOneofCase.Slug when !string.IsNullOrWhiteSpace(request.Slug) => await db.Realms
                .FirstOrDefaultAsync(r => r.Slug == request.Slug),
            _ => throw new RpcException(new Status(StatusCode.InvalidArgument, "Must provide either id or slug"))
        };

        if (realm == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Realm not found"));

        await realmService.RefreshBoostState(realm, context.CancellationToken);
        return realm.ToProtoValue();
    }

    public override async Task<DyGetRealmBatchResponse> GetRealmBatch(DyGetRealmBatchRequest request,
        ServerCallContext context)
    {
        var ids = request.Ids.Select(Guid.Parse).ToList();
        var realms = await db.Realms.Where(r => ids.Contains(r.Id)).ToListAsync();
        await realmService.RefreshBoostStates(realms, context.CancellationToken);
        var response = new DyGetRealmBatchResponse();
        response.Realms.AddRange(realms.Select(r => r.ToProtoValue()));
        return response;
    }

    public override async Task<DyGetUserRealmsResponse> GetUserRealms(DyGetUserRealmsRequest request,
        ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var cacheKey = $"{CacheKeyPrefix}{accountId}";
        var (found, cachedRealms) = await cache.GetAsyncWithStatus<List<Guid>>(cacheKey);
        if (found && cachedRealms != null)
            return new DyGetUserRealmsResponse { RealmIds = { cachedRealms.Select(g => g.ToString()) } };

        var realms = await db.RealmMembers
            .Include(m => m.Realm)
            .Where(m => m.AccountId == accountId)
            .Where(m => m.JoinedAt != null && m.LeaveAt == null)
            .Where(m => m.Realm != null)
            .Select(m => m.Realm!.Id)
            .ToListAsync();

        // Cache the result for 5 minutes
        await cache.SetAsync(cacheKey, realms, TimeSpan.FromMinutes(5));

        return new DyGetUserRealmsResponse { RealmIds = { realms.Select(g => g.ToString()) } };
    }

    public override async Task<DyGetPublicRealmsResponse> GetPublicRealms(
        DyGetPublicRealmsRequest request,
        ServerCallContext context
    )
    {
        var realmsQuery = db.Realms.AsQueryable();

        realmsQuery = request.IsCommunity ?
            realmsQuery.Where(r => r.IsCommunity) :
            realmsQuery.Where(r => r.IsPublic);

        realmsQuery = request.OrderBy switch
        {
            "random" => realmsQuery.OrderBy(_ => EF.Functions.Random()),
            "name" => realmsQuery.OrderBy(r => r.Name),
            "popularity" => realmsQuery.OrderByDescending(r => r.Members.Count),
            _ => realmsQuery.OrderByDescending(r => r.CreatedAt)
        };

        var realms = await realmsQuery.Take(request.Take).ToListAsync();
        await realmService.RefreshBoostStates(realms, context.CancellationToken);

        var response = new DyGetPublicRealmsResponse();
        response.Realms.AddRange(realms.Select(r => r.ToProtoValue()));
        return response;
    }

    public override async Task<DyGetPublicRealmsResponse> SearchRealms(DySearchRealmsRequest request,
        ServerCallContext context)
    {
        var realms = await db.Realms
            .Where(r => r.IsPublic)
            .Where(r => EF.Functions.Like(r.Slug, $"{request.Query}%") ||
                        EF.Functions.Like(r.Name, $"{request.Query}%"))
            .Take(request.Limit)
            .ToListAsync();
        await realmService.RefreshBoostStates(realms, context.CancellationToken);
        var response = new DyGetPublicRealmsResponse();
        response.Realms.AddRange(realms.Select(r => r.ToProtoValue()));
        return response;
    }

    public override async Task<Empty> SendInviteNotify(DySendInviteNotifyRequest request, ServerCallContext context)
    {
        var member = request.Member;
        DyAccount? account;
        try
        {
            account = await accountGrpc.GetAccountAsync(new DyGetAccountRequest { Id = member.AccountId });
        }
        catch
        {
            account = null;
        }

        if (account == null) throw new RpcException(new Status(StatusCode.NotFound, "Account not found"));

        await pusher.SendPushNotificationToUserAsync(
            new DySendPushNotificationToUserRequest
            {
                UserId = account.Id,
                Notification = new DyPushNotification
                {
                    Topic = "invites.realms",
                    Title = localizer.Get("realmInviteTitle", account.Language),
                    Body = localizer.Get("realmInviteBody", locale: account.Language, args: new { realmName = member?.Realm?.Name ?? "Unknown Realm" }),
                    ActionUri = "/realms",
                    IsSavable = true
                }
            }
        );

        return new Empty();
    }

    public override async Task<BoolValue> IsMemberWithRole(DyIsMemberWithRoleRequest request, ServerCallContext context)
    {
        if (request.RequiredRoles.Count == 0)
            return new BoolValue { Value = false };

        var maxRequiredRole = request.RequiredRoles.Max();
        var member = await db.RealmMembers
            .Where(m => m.RealmId == Guid.Parse(request.RealmId) && m.AccountId == Guid.Parse(request.AccountId) &&
                        m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();
        return new BoolValue { Value = member?.Role >= maxRequiredRole };
    }

    public override async Task<DyRealmMember> LoadMemberAccount(DyLoadMemberAccountRequest request,
        ServerCallContext context)
    {
        var member = request.Member;
        if (!string.IsNullOrWhiteSpace(member.RealmId) && !string.IsNullOrWhiteSpace(member.AccountId))
        {
            var found = await db.RealmMembers
                .Include(m => m.Label)
                .Include(m => m.Realm)
                .Where(m => m.RealmId == Guid.Parse(member.RealmId) && m.AccountId == Guid.Parse(member.AccountId))
                .Where(m => m.JoinedAt != null && m.LeaveAt == null)
                .FirstOrDefaultAsync();
            if (found is not null)
                member = found.ToProtoValue();
        }
        DyAccount? account = null;
        try
        {
            account = await accountGrpc.GetAccountAsync(new DyGetAccountRequest { Id = member.AccountId });
        }
        catch
        {
            // ignored
        }

        var response = new DyRealmMember(member) { Account = account };
        return response;
    }

    public override async Task<DyLoadMemberAccountsResponse> LoadMemberAccounts(DyLoadMemberAccountsRequest request,
        ServerCallContext context)
    {
        var queriedMembers = request.Members.ToList();
        var realmIds = queriedMembers
            .Where(m => Guid.TryParse(m.RealmId, out _))
            .Select(m => Guid.Parse(m.RealmId))
            .Distinct()
            .ToList();
        var accountIds = queriedMembers.Select(m => Guid.Parse(m.AccountId)).Distinct().ToList();

        var actualMembers = await db.RealmMembers
            .Include(m => m.Label)
            .Include(m => m.Realm)
            .Where(m => realmIds.Contains(m.RealmId) && accountIds.Contains(m.AccountId))
            .Where(m => m.JoinedAt != null && m.LeaveAt == null)
            .ToDictionaryAsync(m => (m.RealmId, m.AccountId), m => m, context.CancellationToken);

        var accounts = (await accountGrpc.GetAccountBatchAsync(new DyGetAccountBatchRequest
        {
            Id = { accountIds.Select(x => x.ToString()) }
        })).Accounts
            .ToDictionary(a => Guid.Parse(a.Id), a => a);

        var response = new DyLoadMemberAccountsResponse();
        foreach (var requestedMember in queriedMembers)
        {
            var updatedMember = actualMembers.TryGetValue((Guid.Parse(requestedMember.RealmId), Guid.Parse(requestedMember.AccountId)), out var actualMember)
                ? actualMember.ToProtoValue()
                : new DyRealmMember(requestedMember);
            if (accounts.TryGetValue(Guid.Parse(requestedMember.AccountId), out var account))
            {
                updatedMember.Account = account;
            }

            response.Members.Add(updatedMember);
        }

        return response;
    }
}
