using DysonNetwork.Shared.Proto;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Pass.Localization;
using DysonNetwork.Shared;
using DysonNetwork.Shared.Cache;
using Microsoft.Extensions.Localization;

namespace DysonNetwork.Pass.Realm;

public class RealmServiceGrpc(
    AppDatabase db,
    RingService.RingServiceClient pusher,
    IStringLocalizer<NotificationResource> localizer,
    ICacheService cache
)
    : Shared.Proto.RealmService.RealmServiceBase
{
    private const string CacheKeyPrefix = "account:realms:";

    public override async Task<Shared.Proto.Realm> GetRealm(GetRealmRequest request, ServerCallContext context)
    {
        var realm = request.QueryCase switch
        {
            GetRealmRequest.QueryOneofCase.Id when !string.IsNullOrWhiteSpace(request.Id) => await db.Realms.FindAsync(
                Guid.Parse(request.Id)),
            GetRealmRequest.QueryOneofCase.Slug when !string.IsNullOrWhiteSpace(request.Slug) => await db.Realms
                .FirstOrDefaultAsync(r => r.Slug == request.Slug),
            _ => throw new RpcException(new Status(StatusCode.InvalidArgument, "Must provide either id or slug"))
        };

        return realm == null
            ? throw new RpcException(new Status(StatusCode.NotFound, "Realm not found"))
            : realm.ToProtoValue();
    }

    public override async Task<GetUserRealmsResponse> GetUserRealms(GetUserRealmsRequest request,
        ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var cacheKey = $"{CacheKeyPrefix}{accountId}";
        var (found, cachedRealms) = await cache.GetAsyncWithStatus<List<Guid>>(cacheKey);
        if (found && cachedRealms != null)
            return new GetUserRealmsResponse { RealmIds = { cachedRealms.Select(g => g.ToString()) } };

        var realms = await db.RealmMembers
            .Include(m => m.Realm)
            .Where(m => m.AccountId == accountId)
            .Where(m => m.JoinedAt != null && m.LeaveAt == null)
            .Select(m => m.Realm!.Id)
            .ToListAsync();

        // Cache the result for 5 minutes
        await cache.SetAsync(cacheKey, realms, TimeSpan.FromMinutes(5));

        return new GetUserRealmsResponse { RealmIds = { realms.Select(g => g.ToString()) } };
    }

    public override async Task<Empty> SendInviteNotify(SendInviteNotifyRequest request, ServerCallContext context)
    {
        var member = request.Member;
        var account = await db.Accounts
            .AsNoTracking()
            .Include(a => a.Profile)
            .FirstOrDefaultAsync(a => a.Id == Guid.Parse(member.AccountId));
        
        if (account == null) throw new RpcException(new Status(StatusCode.NotFound, "Account not found"));
        
        CultureService.SetCultureInfo(account.Language);

        await pusher.SendPushNotificationToUserAsync(
            new SendPushNotificationToUserRequest
            {
                UserId = account.Id.ToString(),
                Notification = new PushNotification
                {
                    Topic = "invites.realms",
                    Title = localizer["RealmInviteTitle"],
                    Body = localizer["RealmInviteBody", member.Realm?.Name ?? "Unknown Realm"],
                    ActionUri = "/realms",
                    IsSavable = true
                }
            }
        );

        return new Empty();
    }

    public override async Task<BoolValue> IsMemberWithRole(IsMemberWithRoleRequest request, ServerCallContext context)
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

    public override async Task<RealmMember> LoadMemberAccount(LoadMemberAccountRequest request,
        ServerCallContext context)
    {
        var member = request.Member;
        var account = await db.Accounts
            .AsNoTracking()
            .Include(a => a.Profile)
            .FirstOrDefaultAsync(a => a.Id == Guid.Parse(member.AccountId));
        
        var response = new RealmMember(member) { Account = account?.ToProtoValue() };
        return response;
    }

    public override async Task<LoadMemberAccountsResponse> LoadMemberAccounts(LoadMemberAccountsRequest request,
        ServerCallContext context)
    {
        var accountIds = request.Members.Select(m => Guid.Parse(m.AccountId)).ToList();
        var accounts = await db.Accounts
            .AsNoTracking()
            .Include(a => a.Profile)
            .Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.ToProtoValue());

        var response = new LoadMemberAccountsResponse();
        foreach (var member in request.Members)
        {
            var updatedMember = new RealmMember(member);
            if (accounts.TryGetValue(Guid.Parse(member.AccountId), out var account))
            {
                updatedMember.Account = account;
            }

            response.Members.Add(updatedMember);
        }

        return response;
    }
}
