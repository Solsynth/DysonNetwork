using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Pass.Account;

public class ActionLogServiceGrpc(
    ActionLogService actionLogService,
    AppDatabase db,
    ILogger<ActionLogServiceGrpc> logger
)
    : DyActionLogService.DyActionLogServiceBase
{
    private readonly ActionLogService _actionLogService =
        actionLogService ?? throw new ArgumentNullException(nameof(actionLogService));

    private readonly AppDatabase _db = db ?? throw new ArgumentNullException(nameof(db));
    private readonly ILogger<ActionLogServiceGrpc> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public override async Task<DyCreateActionLogResponse> CreateActionLog(DyCreateActionLogRequest request,
        ServerCallContext context)
    {
        if (string.IsNullOrEmpty(request.AccountId) || !Guid.TryParse(request.AccountId, out var accountId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid account ID"));
        }

        try
        {
            var meta = request.Meta
                ?.Select(x => new KeyValuePair<string, object?>(x.Key, InfraObjectCoder.ConvertValueToObject(x.Value)))
                .ToDictionary() ?? new Dictionary<string, object?>();

            _actionLogService.CreateActionLog(
                accountId,
                request.Action,
                meta
            );

            await Task.CompletedTask;
            return new DyCreateActionLogResponse();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating action log");
            throw new RpcException(new Status(StatusCode.Internal, "Failed to create action log"));
        }
    }

    public override async Task<DyListActionLogsResponse> ListActionLogs(DyListActionLogsRequest request,
        ServerCallContext context)
    {
        if (string.IsNullOrEmpty(request.AccountId) || !Guid.TryParse(request.AccountId, out var accountId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid account ID"));
        }

        try
        {
            var query = _db.ActionLogs
                .AsNoTracking()
                .Where(log => log.AccountId == accountId);

            if (!string.IsNullOrEmpty(request.Action))
            {
                query = query.Where(log => log.Action == request.Action);
            }

            // Apply ordering (default to newest first)
            query = (request.OrderBy?.ToLower() ?? "createdat desc") switch
            {
                "createdat" => query.OrderBy(log => log.CreatedAt),
                "createdat desc" => query.OrderByDescending(log => log.CreatedAt),
                _ => query.OrderByDescending(log => log.CreatedAt)
            };

            // Apply pagination
            var pageSize = request.PageSize == 0 ? 50 : Math.Min(request.PageSize, 1000);
            var logs = await query
                .Take(pageSize + 1) // Fetch one extra to determine if there are more pages
                .ToListAsync();

            var hasMore = logs.Count > pageSize;
            if (hasMore)
            {
                logs.RemoveAt(logs.Count - 1);
            }

            var response = new DyListActionLogsResponse
            {
                TotalSize = await query.CountAsync()
            };

            if (hasMore)
            {
                // In a real implementation, you'd generate a proper page token
                response.NextPageToken = (logs.LastOrDefault()?.CreatedAt ?? SystemClock.Instance.GetCurrentInstant())
                    .ToString();
            }

            response.ActionLogs.AddRange(logs.Select(log => log.ToProtoValue()));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing action logs");
            throw new RpcException(new Status(StatusCode.Internal, "Failed to list action logs"));
        }
    }
}