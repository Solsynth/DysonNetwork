using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Models;
using Google.Protobuf.WellKnownTypes;
using System.Text.Json;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Pass.Permission;

public class PermissionServiceGrpc(
    PermissionService psv,
    AppDatabase db,
    ILogger<PermissionServiceGrpc> logger
) : DysonNetwork.Shared.Proto.PermissionService.PermissionServiceBase
{
    public override async Task<HasPermissionResponse> HasPermission(HasPermissionRequest request, ServerCallContext context)
    {
        var type = SnPermissionNode.ConvertProtoActorType(request.Type);
        try
        {
            var hasPermission = await psv.HasPermissionAsync(request.Actor, request.Key, type);
            return new HasPermissionResponse { HasPermission = hasPermission };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking permission for {Type}:{Area}:{Key}",
                type, request.Actor, request.Key);
            throw new RpcException(new Status(StatusCode.Internal, "Permission check failed"));
        }
    }

    public override async Task<GetPermissionResponse> GetPermission(GetPermissionRequest request, ServerCallContext context)
    {
        var type = SnPermissionNode.ConvertProtoActorType(request.Type);
        try
        {
            var permissionValue = await psv.GetPermissionAsync<JsonDocument>(request.Actor, request.Key, type);
            return new GetPermissionResponse
            {
                Value = permissionValue != null ? Value.Parser.ParseJson(permissionValue.RootElement.GetRawText()) : null
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting permission for {Type}:{Area}:{Key}",
                type, request.Actor, request.Key);
            throw new RpcException(new Status(StatusCode.Internal, "Failed to retrieve permission"));
        }
    }

    public override async Task<AddPermissionNodeResponse> AddPermissionNode(AddPermissionNodeRequest request, ServerCallContext context)
    {
        var type = SnPermissionNode.ConvertProtoActorType(request.Type);
        try
        {
            JsonDocument jsonValue;
            try
            {
                jsonValue = JsonDocument.Parse(request.Value.ToString());
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "Invalid JSON in permission value for {Type}:{Area}:{Key}",
                    type, request.Actor, request.Key);
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid permission value format"));
            }

            var node = await psv.AddPermissionNode(
                request.Actor,
                request.Key,
                jsonValue,
                request.ExpiredAt?.ToInstant(),
                request.AffectedAt?.ToInstant(),
                type
            );
            return new AddPermissionNodeResponse { Node = node.ToProtoValue() };
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error adding permission for {Type}:{Area}:{Key}",
                type, request.Actor, request.Key);
            throw new RpcException(new Status(StatusCode.Internal, "Failed to add permission node"));
        }
    }

    public override async Task<AddPermissionNodeToGroupResponse> AddPermissionNodeToGroup(AddPermissionNodeToGroupRequest request, ServerCallContext context)
    {
        var type = SnPermissionNode.ConvertProtoActorType(request.Type);
        try
        {
            var group = await FindPermissionGroupAsync(request.Group.Id);
            if (group == null)
            {
                throw new RpcException(new Status(StatusCode.NotFound, "Permission group not found"));
            }

            JsonDocument jsonValue;
            try
            {
                jsonValue = JsonDocument.Parse(request.Value.ToString());
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "Invalid JSON in permission value for {Type}:{Area}:{Key}",
                    type, request.Actor, request.Key);
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid permission value format"));
            }

            var node = await psv.AddPermissionNodeToGroup(
                group,
                request.Actor,
                request.Key,
                jsonValue,
                request.ExpiredAt?.ToInstant(),
                request.AffectedAt?.ToInstant(),
                type
            );
            return new AddPermissionNodeToGroupResponse { Node = node.ToProtoValue() };
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error adding permission for {Type}:{Area}:{Key}",
                type, request.Actor, request.Key);
            throw new RpcException(new Status(StatusCode.Internal, "Failed to add permission node to group"));
        }
    }

    public override async Task<RemovePermissionNodeResponse> RemovePermissionNode(RemovePermissionNodeRequest request, ServerCallContext context)
    {
        var type = SnPermissionNode.ConvertProtoActorType(request.Type);
        try
        {
            await psv.RemovePermissionNode(request.Actor, request.Key, type);
            return new RemovePermissionNodeResponse { Success = true };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error removing permission for {Type}:{Area}:{Key}",
                type, request.Actor, request.Key);
            throw new RpcException(new Status(StatusCode.Internal, "Failed to remove permission node"));
        }
    }

    public override async Task<RemovePermissionNodeFromGroupResponse> RemovePermissionNodeFromGroup(RemovePermissionNodeFromGroupRequest request, ServerCallContext context)
    {
        try
        {
            var group = await FindPermissionGroupAsync(request.Group.Id);
            if (group == null)
            {
                throw new RpcException(new Status(StatusCode.NotFound, "Permission group not found"));
            }

            await psv.RemovePermissionNodeFromGroup<JsonDocument>(group, request.Actor, request.Key);
            return new RemovePermissionNodeFromGroupResponse { Success = true };
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error removing permission from group for {Area}:{Key}",
                request.Actor, request.Key);
            throw new RpcException(new Status(StatusCode.Internal, "Failed to remove permission node from group"));
        }
    }

    private async Task<SnPermissionGroup?> FindPermissionGroupAsync(string groupId)
    {
        if (Guid.TryParse(groupId, out var guid))
            return await db.PermissionGroups.FirstOrDefaultAsync(g => g.Id == guid);
        logger.LogWarning("Invalid GUID format for group ID: {GroupId}", groupId);
        return null;

    }
}
