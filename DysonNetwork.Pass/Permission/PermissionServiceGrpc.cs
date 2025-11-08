using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Models;
using Google.Protobuf.WellKnownTypes;
using System.Text.Json;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Pass.Permission;

public class PermissionServiceGrpc(
    PermissionService permissionService,
    AppDatabase db,
    ILogger<PermissionServiceGrpc> logger
) : DysonNetwork.Shared.Proto.PermissionService.PermissionServiceBase
{
    public override async Task<HasPermissionResponse> HasPermission(HasPermissionRequest request, ServerCallContext context)
    {
        try
        {
            var hasPermission = await permissionService.HasPermissionAsync(request.Actor, request.Area, request.Key);
            return new HasPermissionResponse { HasPermission = hasPermission };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking permission for actor {Actor}, area {Area}, key {Key}",
                request.Actor, request.Area, request.Key);
            throw new RpcException(new Status(StatusCode.Internal, "Permission check failed"));
        }
    }

    public override async Task<GetPermissionResponse> GetPermission(GetPermissionRequest request, ServerCallContext context)
    {
        try
        {
            var permissionValue = await permissionService.GetPermissionAsync<JsonDocument>(request.Actor, request.Area, request.Key);
            return new GetPermissionResponse
            {
                Value = permissionValue != null ? Value.Parser.ParseJson(permissionValue.RootElement.GetRawText()) : null
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting permission for actor {Actor}, area {Area}, key {Key}",
                request.Actor, request.Area, request.Key);
            throw new RpcException(new Status(StatusCode.Internal, "Failed to retrieve permission"));
        }
    }

    public override async Task<AddPermissionNodeResponse> AddPermissionNode(AddPermissionNodeRequest request, ServerCallContext context)
    {
        try
        {
            JsonDocument jsonValue;
            try
            {
                jsonValue = JsonDocument.Parse(request.Value.ToString());
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Invalid JSON in permission value for actor {Actor}, area {Area}, key {Key}",
                    request.Actor, request.Area, request.Key);
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid permission value format"));
            }

            var node = await permissionService.AddPermissionNode(
                request.Actor,
                request.Area,
                request.Key,
                jsonValue,
                request.ExpiredAt?.ToInstant(),
                request.AffectedAt?.ToInstant()
            );
            return new AddPermissionNodeResponse { Node = node.ToProtoValue() };
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error adding permission node for actor {Actor}, area {Area}, key {Key}",
                request.Actor, request.Area, request.Key);
            throw new RpcException(new Status(StatusCode.Internal, "Failed to add permission node"));
        }
    }

    public override async Task<AddPermissionNodeToGroupResponse> AddPermissionNodeToGroup(AddPermissionNodeToGroupRequest request, ServerCallContext context)
    {
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
                logger.LogWarning(ex, "Invalid JSON in permission value for group {GroupId}, actor {Actor}, area {Area}, key {Key}",
                    request.Group.Id, request.Actor, request.Area, request.Key);
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid permission value format"));
            }

            var node = await permissionService.AddPermissionNodeToGroup(
                group,
                request.Actor,
                request.Area,
                request.Key,
                jsonValue,
                request.ExpiredAt?.ToInstant(),
                request.AffectedAt?.ToInstant()
            );
            return new AddPermissionNodeToGroupResponse { Node = node.ToProtoValue() };
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error adding permission node to group {GroupId} for actor {Actor}, area {Area}, key {Key}",
                request.Group.Id, request.Actor, request.Area, request.Key);
            throw new RpcException(new Status(StatusCode.Internal, "Failed to add permission node to group"));
        }
    }

    public override async Task<RemovePermissionNodeResponse> RemovePermissionNode(RemovePermissionNodeRequest request, ServerCallContext context)
    {
        try
        {
            await permissionService.RemovePermissionNode(request.Actor, request.Area, request.Key);
            return new RemovePermissionNodeResponse { Success = true };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error removing permission node for actor {Actor}, area {Area}, key {Key}",
                request.Actor, request.Area, request.Key);
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

            await permissionService.RemovePermissionNodeFromGroup<JsonDocument>(group, request.Actor, request.Area, request.Key);
            return new RemovePermissionNodeFromGroupResponse { Success = true };
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error removing permission node from group {GroupId} for actor {Actor}, area {Area}, key {Key}",
                request.Group.Id, request.Actor, request.Area, request.Key);
            throw new RpcException(new Status(StatusCode.Internal, "Failed to remove permission node from group"));
        }
    }

    private async Task<SnPermissionGroup?> FindPermissionGroupAsync(string groupId)
    {
        if (!Guid.TryParse(groupId, out var guid))
        {
            logger.LogWarning("Invalid GUID format for group ID: {GroupId}", groupId);
            return null;
        }

        return await db.PermissionGroups.FirstOrDefaultAsync(g => g.Id == guid);
    }
}
