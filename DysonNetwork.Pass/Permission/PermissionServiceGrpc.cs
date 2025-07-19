using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Shared.Proto;
using Google.Protobuf.WellKnownTypes;
using System.Text.Json;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Pass.Permission;

public class PermissionServiceGrpc(
    PermissionService permissionService,
    AppDatabase db
) : DysonNetwork.Shared.Proto.PermissionService.PermissionServiceBase
{
    public override async Task<HasPermissionResponse> HasPermission(HasPermissionRequest request, ServerCallContext context)
    {
        var hasPermission = await permissionService.HasPermissionAsync(request.Actor, request.Area, request.Key);
        return new HasPermissionResponse { HasPermission = hasPermission };
    }

    public override async Task<GetPermissionResponse> GetPermission(GetPermissionRequest request, ServerCallContext context)
    {
        var permissionValue = await permissionService.GetPermissionAsync<JsonDocument>(request.Actor, request.Area, request.Key);
        return new GetPermissionResponse { Value = permissionValue != null ? Value.Parser.ParseJson(permissionValue.RootElement.GetRawText()) : null };
    }

    public override async Task<AddPermissionNodeResponse> AddPermissionNode(AddPermissionNodeRequest request, ServerCallContext context)
    {
        var node = await permissionService.AddPermissionNode(
            request.Actor,
            request.Area,
            request.Key,
            JsonDocument.Parse(request.Value.ToString()), // Convert Value to JsonDocument
            request.ExpiredAt?.ToInstant(),
            request.AffectedAt?.ToInstant()
        );
        return new AddPermissionNodeResponse { Node = node.ToProtoValue() };
    }

    public override async Task<AddPermissionNodeToGroupResponse> AddPermissionNodeToGroup(AddPermissionNodeToGroupRequest request, ServerCallContext context)
    {
        var group = await db.PermissionGroups.FirstOrDefaultAsync(g => g.Id == Guid.Parse(request.Group.Id));
        if (group == null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Permission group not found."));
        }

        var node = await permissionService.AddPermissionNodeToGroup(
            group,
            request.Actor,
            request.Area,
            request.Key,
            JsonDocument.Parse(request.Value.ToString()), // Convert Value to JsonDocument
            request.ExpiredAt?.ToInstant(),
            request.AffectedAt?.ToInstant()
        );
        return new AddPermissionNodeToGroupResponse { Node = node.ToProtoValue() };
    }

    public override async Task<RemovePermissionNodeResponse> RemovePermissionNode(RemovePermissionNodeRequest request, ServerCallContext context)
    {
        await permissionService.RemovePermissionNode(request.Actor, request.Area, request.Key);
        return new RemovePermissionNodeResponse { Success = true };
    }

    public override async Task<RemovePermissionNodeFromGroupResponse> RemovePermissionNodeFromGroup(RemovePermissionNodeFromGroupRequest request, ServerCallContext context)
    {
        var group = await db.PermissionGroups.FirstOrDefaultAsync(g => g.Id == Guid.Parse(request.Group.Id));
        if (group == null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Permission group not found."));
        }

        await permissionService.RemovePermissionNodeFromGroup<JsonDocument>(group, request.Actor, request.Area, request.Key);
        return new RemovePermissionNodeFromGroupResponse { Success = true };
    }
}

public static class PermissionExtensions
{
    public static DysonNetwork.Shared.Proto.PermissionNode ToProtoValue(this PermissionNode node)
    {
        return new DysonNetwork.Shared.Proto.PermissionNode
        {
            Id = node.Id.ToString(),
            Actor = node.Actor,
            Area = node.Area,
            Key = node.Key,
            Value = Value.Parser.ParseJson(node.Value.RootElement.GetRawText()),
            ExpiredAt = node.ExpiredAt?.ToTimestamp(),
            AffectedAt = node.AffectedAt?.ToTimestamp(),
            GroupId = node.GroupId?.ToString() ?? string.Empty
        };
    }
}

