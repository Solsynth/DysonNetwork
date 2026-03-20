using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Padlock.Account;

public class ActionLogServiceGrpc(
    ActionLogService actionLogService,
    AppDatabase db,
    ILogger<ActionLogServiceGrpc> logger
) : DyActionLogService.DyActionLogServiceBase
{
    public override async Task<DyCreateActionLogResponse> CreateActionLog(
        DyCreateActionLogRequest request,
        ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.AccountId) || !Guid.TryParse(request.AccountId, out var accountId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid account ID"));

        try
        {
            var meta = request.Meta
                .Select(x => new KeyValuePair<string, object?>(x.Key, InfraObjectCoder.ConvertValueToObject(x.Value)))
                .Where(x => x.Value is not null)
                .ToDictionary(x => x.Key, x => x.Value!);

            Guid? sessionId = null;
            if (!string.IsNullOrWhiteSpace(request.SessionId) && Guid.TryParse(request.SessionId, out var parsedSessionId))
                sessionId = parsedSessionId;

            await actionLogService.CreateActionLogAsync(
                accountId,
                request.Action,
                meta,
                string.IsNullOrWhiteSpace(request.UserAgent) ? null : request.UserAgent,
                string.IsNullOrWhiteSpace(request.IpAddress) ? null : request.IpAddress,
                string.IsNullOrWhiteSpace(request.Location) ? null : request.Location,
                sessionId
            );
            return new DyCreateActionLogResponse();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating action log for account {AccountId}", accountId);
            throw new RpcException(new Status(StatusCode.Internal, "Failed to create action log"));
        }
    }

    public override async Task<DyListActionLogsResponse> ListActionLogs(
        DyListActionLogsRequest request,
        ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.AccountId) || !Guid.TryParse(request.AccountId, out var accountId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid account ID"));

        try
        {
            var query = db.ActionLogs
                .AsNoTracking()
                .Where(log => log.AccountId == accountId);

            if (!string.IsNullOrWhiteSpace(request.Action))
                query = query.Where(log => log.Action == request.Action);

            query = (request.OrderBy?.ToLowerInvariant() ?? "createdat desc") switch
            {
                "createdat" => query.OrderBy(log => log.CreatedAt),
                _ => query.OrderByDescending(log => log.CreatedAt)
            };

            var total = await query.CountAsync();
            var pageSize = request.PageSize <= 0 ? 50 : Math.Min(request.PageSize, 1000);
            var offset = int.TryParse(request.PageToken, out var parsedOffset) ? Math.Max(0, parsedOffset) : 0;

            var logs = await query
                .Skip(offset)
                .Take(pageSize + 1)
                .ToListAsync();

            var hasMore = logs.Count > pageSize;
            if (hasMore)
                logs.RemoveAt(logs.Count - 1);

            var response = new DyListActionLogsResponse
            {
                TotalSize = total
            };
            response.ActionLogs.AddRange(logs.Select(log => log.ToProtoValue()));

            if (hasMore)
                response.NextPageToken = (offset + pageSize).ToString();

            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing action logs for account {AccountId}", accountId);
            throw new RpcException(new Status(StatusCode.Internal, "Failed to list action logs"));
        }
    }

    public override async Task<DySearchActionLogsResponse> SearchActionLogs(
        DySearchActionLogsRequest request,
        ServerCallContext context)
    {
        try
        {
            var query = db.ActionLogs.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(request.AccountId))
            {
                if (!Guid.TryParse(request.AccountId, out var accountId))
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid account ID"));

                query = query.Where(log => log.AccountId == accountId);
            }

            if (request.Actions.Count > 0)
            {
                var actions = request.Actions
                    .Where(action => !string.IsNullOrWhiteSpace(action))
                    .Distinct()
                    .ToList();
                if (actions.Count > 0)
                    query = query.Where(log => actions.Contains(log.Action));
            }

            if (request.CreatedAfter is not null)
            {
                var createdAfter = request.CreatedAfter.ToInstant();
                query = query.Where(log => log.CreatedAt >= createdAfter);
            }

            if (request.CreatedBefore is not null)
            {
                var createdBefore = request.CreatedBefore.ToInstant();
                query = query.Where(log => log.CreatedAt <= createdBefore);
            }

            query = (request.OrderBy?.ToLowerInvariant() ?? "createdat asc") switch
            {
                "createdat desc" => query.OrderByDescending(log => log.CreatedAt).ThenByDescending(log => log.Id),
                "createdat" => query.OrderBy(log => log.CreatedAt).ThenBy(log => log.Id),
                _ => query.OrderBy(log => log.CreatedAt).ThenBy(log => log.Id)
            };

            var pageSize = request.PageSize <= 0 ? 100 : Math.Min(request.PageSize, 1000);
            var offset = int.TryParse(request.PageToken, out var parsedOffset) ? Math.Max(0, parsedOffset) : 0;

            var logs = await query
                .Skip(offset)
                .Take(pageSize + 1)
                .ToListAsync();

            var hasMore = logs.Count > pageSize;
            if (hasMore)
                logs.RemoveAt(logs.Count - 1);

            var response = new DySearchActionLogsResponse();
            response.ActionLogs.AddRange(logs.Select(log => log.ToProtoValue()));

            if (hasMore)
                response.NextPageToken = (offset + pageSize).ToString();

            return response;
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error searching action logs");
            throw new RpcException(new Status(StatusCode.Internal, "Failed to search action logs"));
        }
    }
}
