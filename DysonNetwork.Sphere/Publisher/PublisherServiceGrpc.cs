using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using PublisherMemberRole = DysonNetwork.Shared.Models.PublisherMemberRole;

namespace DysonNetwork.Sphere.Publisher;

public class PublisherServiceGrpc(PublisherService service, AppDatabase db)
    : DyPublisherService.DyPublisherServiceBase
{
    public override async Task<DyGetPublisherResponse> GetPublisher(
        DyGetPublisherRequest request,
        ServerCallContext context
    )
    {
        SnPublisher? p = null;
        switch (request.QueryCase)
        {
            case DyGetPublisherRequest.QueryOneofCase.Id:
                if (!string.IsNullOrWhiteSpace(request.Id) && Guid.TryParse(request.Id, out var id))
                    p = await db.Publishers.FirstOrDefaultAsync(x => x.Id == id);
                break;
            case DyGetPublisherRequest.QueryOneofCase.Name:
                if (!string.IsNullOrWhiteSpace(request.Name))
                    p = await service.GetPublisherByName(request.Name);
                break;
        }

        if (p is null) throw new RpcException(new Status(StatusCode.NotFound, "Publisher not found"));
        return new DyGetPublisherResponse { Publisher = p.ToProtoValue() };
    }

    public override async Task<DyListPublishersResponse> GetPublisherBatch(
        DyGetPublisherBatchRequest request,
        ServerCallContext context
    )
    {
        var ids = request.Ids
            .Where(s => !string.IsNullOrWhiteSpace(s) && Guid.TryParse(s, out _))
            .Select(Guid.Parse)
            .ToList();
        if (ids.Count == 0) return new DyListPublishersResponse();
        var list = await db.Publishers.Where(p => ids.Contains(p.Id)).ToListAsync();
        var resp = new DyListPublishersResponse();
        resp.Publishers.AddRange(list.Select(p => p.ToProtoValue()));
        return resp;
    }

    public override async Task<DyListPublishersResponse> ListPublishers(
        DyListPublishersRequest request,
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
        var resp = new DyListPublishersResponse();
        resp.Publishers.AddRange(list.Select(p => p.ToProtoValue()));
        return resp;
    }

    public override async Task<DyListPublisherMembersResponse> ListPublisherMembers(DyListPublisherMembersRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.PublisherId, out var pid))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid publisher_id"));
        var members = await service.GetPublisherMembers(pid);
        var resp = new DyListPublisherMembersResponse();
        resp.Members.AddRange(members.Select(m => m.ToProto()));
        return resp;
    }

    public override async Task<Google.Protobuf.WellKnownTypes.StringValue> SetPublisherFeatureFlag(
        DySetPublisherFeatureFlagRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.PublisherId, out var pid))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid publisher_id"));
        await service.SetFeatureFlag(pid, request.Flag);
        return new Google.Protobuf.WellKnownTypes.StringValue { Value = request.Flag };
    }

    public override async Task<DyHasPublisherFeatureResponse> HasPublisherFeature(DyHasPublisherFeatureRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.PublisherId, out var pid))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid publisher_id"));
        var enabled = await service.HasFeature(pid, request.Flag);
        return new DyHasPublisherFeatureResponse { Enabled = enabled };
    }

    public override async Task<DyIsPublisherMemberResponse> IsPublisherMember(DyIsPublisherMemberRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.PublisherId, out var pid))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid publisher_id"));
        if (!Guid.TryParse(request.AccountId, out var aid))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid account_id"));
        var requiredRole = request.Role switch
        {
            DyPublisherMemberRole.DyOwner => PublisherMemberRole.Owner,
            DyPublisherMemberRole.DyManager => PublisherMemberRole.Manager,
            DyPublisherMemberRole.DyEditor => PublisherMemberRole.Editor,
            _ =>PublisherMemberRole.Viewer
        };
        var valid = await service.IsMemberWithRole(pid, aid, requiredRole);
        return new DyIsPublisherMemberResponse { Valid = valid };
    }
}