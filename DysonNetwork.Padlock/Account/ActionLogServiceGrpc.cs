using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

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
}
