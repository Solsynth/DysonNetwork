using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Publisher;

public class PublisherServiceGrpc(PublisherService service, AppDatabase db)
    : Shared.Proto.PublisherService.PublisherServiceBase
{
    public override async Task<GetPublisherResponse> GetPublisher(GetPublisherRequest request,
        ServerCallContext context)
    {
        var p = await service.GetPublisherByName(request.Name);
        if (p is null) throw new RpcException(new Status(StatusCode.NotFound, "publisher not found"));
        return new GetPublisherResponse { Publisher = p.ToProto(db) };
    }

    public override async Task<ListPublishersResponse> ListPublishers(ListPublishersRequest request,
        ServerCallContext context)
    {
        IQueryable<Publisher> query = db.Publishers.AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.AccountId) && Guid.TryParse(request.AccountId, out var aid))
        {
            var ids = await db.PublisherMembers.Where(m => m.AccountId == aid).Select(m => m.PublisherId).ToListAsync();
            query = query.Where(p => ids.Contains(p.Id));
        }

        if (!string.IsNullOrWhiteSpace(request.RealmId) && Guid.TryParse(request.RealmId, out var rid))
        {
            query = query.Where(p => p.RealmId == rid);
        }

        var list = await query.Take(request.PageSize > 0 ? request.PageSize : 100).ToListAsync();
        var resp = new ListPublishersResponse();
        resp.Publishers.AddRange(list.Select(p => p.ToProto(db)));
        resp.TotalSize = list.Count;
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
}