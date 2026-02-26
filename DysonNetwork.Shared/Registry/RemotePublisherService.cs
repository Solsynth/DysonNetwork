using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;

namespace DysonNetwork.Shared.Registry;

public class RemotePublisherService(DyPublisherService.DyPublisherServiceClient publishers)
{
    public async Task<SnPublisher> GetPublisher(string? name = null, string? id = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(id))
            throw new ArgumentException("Either name or id must be provided.");
        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(id))
            throw new ArgumentException("Only one of name or id can be provided.");

        var request = new DyGetPublisherRequest();
        if (!string.IsNullOrEmpty(name))
            request.Name = name;
        else
            request.Id = id;

        var response = await publishers.GetPublisherAsync(request, cancellationToken: cancellationToken);
        return SnPublisher.FromProtoValue(response.Publisher);
    }

    public async Task<List<SnPublisher>> GetPublishersBatch(List<string> ids,
        CancellationToken cancellationToken = default)
    {
        var request = new DyGetPublisherBatchRequest();
        request.Ids.AddRange(ids);
        var response = await publishers.GetPublisherBatchAsync(request, cancellationToken: cancellationToken);
        return response.Publishers.Select(SnPublisher.FromProtoValue).ToList();
    }

    public async Task<List<SnPublisher>> ListPublishers(string? accountId = null, string? realmId = null,
        CancellationToken cancellationToken = default)
    {
        var request = new DyListPublishersRequest
        {
            AccountId = accountId ?? "",
            RealmId = realmId ?? ""
        };
        var response = await publishers.ListPublishersAsync(request, cancellationToken: cancellationToken);
        return response.Publishers.Select(SnPublisher.FromProtoValue).ToList();
    }

    public async Task<List<SnPublisherMember>> ListPublisherMembers(string publisherId,
        CancellationToken cancellationToken = default)
    {
        var request = new DyListPublisherMembersRequest { PublisherId = publisherId };
        var response = await publishers.ListPublisherMembersAsync(request, cancellationToken: cancellationToken);
        return response.Members.Select(SnPublisherMember.FromProtoValue).ToList();
    }

    public async Task<string?> SetPublisherFeatureFlag(string publisherId, string flag,
        CancellationToken cancellationToken = default)
    {
        var request = new DySetPublisherFeatureFlagRequest
        {
            PublisherId = publisherId,
            Flag = flag
        };
        var response = await publishers.SetPublisherFeatureFlagAsync(request, cancellationToken: cancellationToken);
        return response.Value;
    }

    public async Task<bool> HasPublisherFeature(string publisherId, string flag,
        CancellationToken cancellationToken = default)
    {
        var request = new DyHasPublisherFeatureRequest
        {
            PublisherId = publisherId,
            Flag = flag
        };
        var response = await publishers.HasPublisherFeatureAsync(request, cancellationToken: cancellationToken);
        return response.Enabled;
    }

    public async Task<bool> IsPublisherMember(string publisherId, string accountId,
        DyPublisherMemberRole? role = null, CancellationToken cancellationToken = default)
    {
        var request = new DyIsPublisherMemberRequest
        {
            PublisherId = publisherId,
            AccountId = accountId,
        };
        if (role.HasValue) request.Role = role.Value;
        var response = await publishers.IsPublisherMemberAsync(request, cancellationToken: cancellationToken);
        return response.Valid;
    }

    public async Task<List<SnPublisher>> GetUserPublishers(Guid accountId,
        CancellationToken cancellationToken = default)
    {
        return await ListPublishers(accountId: accountId.ToString(), realmId: null, cancellationToken);
    }

    public async Task<bool> IsMemberWithRole(Guid publisherId, Guid accountId, Models.PublisherMemberRole role,
        CancellationToken cancellationToken = default)
    {
        var protoRole = role switch
        {
            PublisherMemberRole.Owner => DyPublisherMemberRole.DyOwner,
            PublisherMemberRole.Manager => DyPublisherMemberRole.DyManager,
            PublisherMemberRole.Editor => DyPublisherMemberRole.DyEditor,
            _ => DyPublisherMemberRole.DyViewer
        };
        return await IsPublisherMember(publisherId.ToString(), accountId.ToString(), protoRole, cancellationToken);
    }

    public async Task<SnPublisher?> GetPublisherByName(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetPublisher(name: name, cancellationToken: cancellationToken);
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
        {
            return null;
        }
    }
}
