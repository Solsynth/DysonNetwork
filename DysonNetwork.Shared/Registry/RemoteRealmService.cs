using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Google.Protobuf.WellKnownTypes;

namespace DysonNetwork.Shared.Registry;

public class RemoteRealmService(DyRealmService.DyRealmServiceClient realms)
{
    public async Task<SnRealm> GetRealm(string id)
    {
        var request = new DyGetRealmRequest { Id = id };
        var response = await realms.GetRealmAsync(request);
        return SnRealm.FromProtoValue(response);
    }

    public async Task<SnRealm> GetRealmBySlug(string slug)
    {
        var request = new DyGetRealmRequest { Slug = slug };
        var response = await realms.GetRealmAsync(request);
        return SnRealm.FromProtoValue(response);
    }

    public async Task<List<Guid>> GetUserRealms(Guid accountId)
    {
        var request = new DyGetUserRealmsRequest { AccountId = accountId.ToString() };
        var response = await realms.GetUserRealmsAsync(request);
        return response.RealmIds.Select(Guid.Parse).ToList();
    }

    public async Task<List<SnRealm>> GetPublicRealms(string orderBy = "date", int take = 20)
    {
        var response = await realms.GetPublicRealmsAsync(new DyGetPublicRealmsRequest
        {
            OrderBy = orderBy,
            Take = take
        });
        return response.Realms.Select(SnRealm.FromProtoValue).ToList();
    }

    public async Task<List<SnRealm>> SearchRealms(string query, int limit)
    {
        var request = new DySearchRealmsRequest { Query = query, Limit = limit };
        var response = await realms.SearchRealmsAsync(request);
        return response.Realms.Select(SnRealm.FromProtoValue).ToList();
    }

    public async Task<List<SnRealm>> GetRealmBatch(List<string> ids)
    {
        var request = new DyGetRealmBatchRequest();
        request.Ids.AddRange(ids);
        var response = await realms.GetRealmBatchAsync(request);
        return response.Realms.Select(SnRealm.FromProtoValue).ToList();
    }

    public async Task SendInviteNotify(SnRealmMember member)
    {
        var protoMember = member.ToProtoValue();
        var request = new DySendInviteNotifyRequest { Member = protoMember };
        await realms.SendInviteNotifyAsync(request);
    }

    public async Task<bool> IsMemberWithRole(Guid realmId, Guid accountId, List<int> requiredRoles)
    {
        var request = new DyIsMemberWithRoleRequest { RealmId = realmId.ToString(), AccountId = accountId.ToString() };
        request.RequiredRoles.AddRange(requiredRoles);
        var response = await realms.IsMemberWithRoleAsync(request);
        return response.Value;
    }

    public async Task<SnRealmMember> LoadMemberAccount(SnRealmMember member)
    {
        var protoMember = member.ToProtoValue();
        var request = new DyLoadMemberAccountRequest { Member = protoMember };
        var response = await realms.LoadMemberAccountAsync(request);
        return SnRealmMember.FromProtoValue(response);
    }

    public async Task<List<SnRealmMember>> LoadMemberAccounts(List<SnRealmMember> members)
    {
        var protoMembers = members.Select(m => m.ToProtoValue()).ToList();
        var request = new DyLoadMemberAccountsRequest();
        request.Members.AddRange(protoMembers);
        var response = await realms.LoadMemberAccountsAsync(request);
        return response.Members.Select(SnRealmMember.FromProtoValue).ToList();
    }
}
