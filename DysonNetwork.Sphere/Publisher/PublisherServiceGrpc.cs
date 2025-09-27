using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using PublisherMemberRole = DysonNetwork.Shared.Models.PublisherMemberRole;

namespace DysonNetwork.Sphere.Publisher;

public class PublisherServiceGrpc(PublisherService service, AppDatabase db)
    : Shared.Proto.PublisherService.PublisherServiceBase
{
    public override async Task<GetPublisherResponse> GetPublisher(
        GetPublisherRequest request,
        ServerCallContext context
    )
    {
        SnPublisher? p = null;
        switch (request.QueryCase)
        {
            case GetPublisherRequest.QueryOneofCase.Id:
                if (!string.IsNullOrWhiteSpace(request.Id) && Guid.TryParse(request.Id, out var id))
                    p = await db.Publishers.FirstOrDefaultAsync(x => x.Id == id);
                break;
            case GetPublisherRequest.QueryOneofCase.Name:
                if (!string.IsNullOrWhiteSpace(request.Name))
                    p = await service.GetPublisherByName(request.Name);
                break;
        }

        if (p is null) throw new RpcException(new Status(StatusCode.NotFound, "Publisher not found"));
        return new GetPublisherResponse { Publisher = p.ToProto() };
    }

    public override async Task<ListPublishersResponse> GetPublisherBatch(
        GetPublisherBatchRequest request,
        ServerCallContext context
    )
    {
        var ids = request.Ids
            .Where(s => !string.IsNullOrWhiteSpace(s) && Guid.TryParse(s, out _))
            .Select(Guid.Parse)
            .ToList();
        if (ids.Count == 0) return new ListPublishersResponse();
        var list = await db.Publishers.Where(p => ids.Contains(p.Id)).ToListAsync();
        var resp = new ListPublishersResponse();
        resp.Publishers.AddRange(list.Select(p => p.ToProto()));
        return resp;
    }

    public override async Task<ListPublishersResponse> ListPublishers(
        ListPublishersRequest request,
        ServerCallContext context
    )
    {
        var query = db.Publishers.AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.AccountId) && Guid.TryParse(request.AccountId, out var aid))
        {
            var ids = await db.PublisherMembers.Where(m => m.AccountId == aid).Select(m => m.PublisherId).ToListAsync();
            query = query.Where(p => ids.Contains(p.Id));
        }

        if (!string.IsNullOrWhiteSpace(request.RealmId) && Guid.TryParse(request.RealmId, out var rid))
            query = query.Where(p => p.RealmId == rid);

        var list = await query.ToListAsync();
        var resp = new ListPublishersResponse();
        resp.Publishers.AddRange(list.Select(p => p.ToProto()));
        return resp;
    }

    public override async Task<ListPublisherMembersResponse> ListPublisherMembers(ListPublisherMembersRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.PublisherId, out var pid))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid publisher_id"));
        var members = await service.GetPublisherMembers(pid);
        var resp = new ListPublisherMembersResponse();
        resp.Members.AddRange(members.Select(m => m.ToProto()));
        return resp;
    }

    public override async Task<Google.Protobuf.WellKnownTypes.StringValue> SetPublisherFeatureFlag(
        SetPublisherFeatureFlagRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.PublisherId, out var pid))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid publisher_id"));
        await service.SetFeatureFlag(pid, request.Flag);
        return new Google.Protobuf.WellKnownTypes.StringValue { Value = request.Flag };
    }

    public override async Task<HasPublisherFeatureResponse> HasPublisherFeature(HasPublisherFeatureRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.PublisherId, out var pid))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid publisher_id"));
        var enabled = await service.HasFeature(pid, request.Flag);
        return new HasPublisherFeatureResponse { Enabled = enabled };
    }

    public override async Task<IsPublisherMemberResponse> IsPublisherMember(IsPublisherMemberRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.PublisherId, out var pid))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid publisher_id"));
        if (!Guid.TryParse(request.AccountId, out var aid))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid account_id"));
        var requiredRole = request.Role switch
        {
            Shared.Proto.PublisherMemberRole.Owner => PublisherMemberRole.Owner,
            Shared.Proto.PublisherMemberRole.Manager => PublisherMemberRole.Manager,
            Shared.Proto.PublisherMemberRole.Editor => PublisherMemberRole.Editor,
            _ => PublisherMemberRole.Viewer
        };
        var valid = await service.IsMemberWithRole(pid, aid, requiredRole);
        return new IsPublisherMemberResponse { Valid = valid };
    }
}