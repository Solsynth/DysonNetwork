using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Shared.Registry;

public class RemoteActionLogService(DyActionLogService.DyActionLogServiceClient actionLogs)
{
    public sealed record ListActionLogsPageResult(
        List<DyActionLog> ActionLogs,
        string? NextPageToken,
        int TotalSize
    );
    
    public sealed record SearchActionLogsPageResult(
        List<DyActionLog> ActionLogs,
        string? NextPageToken
    );

    public void CreateActionLog(Guid accountId, string action, Dictionary<string, object> meta)
    {
        var request = new DyCreateActionLogRequest
        {
            AccountId = accountId.ToString(),
            Action = action
        };
        
        if (meta.Count > 0)
            request.Meta.Add(InfraObjectCoder.ConvertToValueMap(meta));
        
        _ = actionLogs.CreateActionLogAsync(request);
    }

    public void CreateActionLog(
        Guid accountId,
        string action,
        Dictionary<string, object> meta,
        string? userAgent = null,
        string? ipAddress = null,
        string? location = null,
        Guid? sessionId = null)
    {
        var request = new DyCreateActionLogRequest
        {
            AccountId = accountId.ToString(),
            Action = action,
            UserAgent = userAgent ?? string.Empty,
            IpAddress = ipAddress ?? string.Empty,
            Location = location ?? string.Empty
        };

        if (meta.Count > 0)
            request.Meta.Add(InfraObjectCoder.ConvertToValueMap(meta));

        if (sessionId.HasValue)
            request.SessionId = sessionId.Value.ToString();

        _ = actionLogs.CreateActionLogAsync(request);
    }

    public async Task<List<DyActionLog>> ListActionLogs(
        Guid accountId,
        string? action = null,
        int pageSize = 50,
        string? pageToken = null,
        string? orderBy = "createdat desc")
    {
        var page = await ListActionLogsPage(accountId, action, pageSize, pageToken, orderBy);
        return page.ActionLogs;
    }

    public async Task<ListActionLogsPageResult> ListActionLogsPage(
        Guid accountId,
        string? action = null,
        int pageSize = 50,
        string? pageToken = null,
        string? orderBy = "createdat desc")
    {
        var request = new DyListActionLogsRequest
        {
            AccountId = accountId.ToString(),
            PageSize = pageSize,
            OrderBy = orderBy ?? "createdat desc"
        };

        if (!string.IsNullOrEmpty(action))
            request.Action = action;
        if (!string.IsNullOrEmpty(pageToken))
            request.PageToken = pageToken;

        var response = await actionLogs.ListActionLogsAsync(request);
        return new ListActionLogsPageResult(
            response.ActionLogs.ToList(),
            string.IsNullOrWhiteSpace(response.NextPageToken) ? null : response.NextPageToken,
            response.TotalSize
        );
    }

    public async Task<List<DyActionLog>> ListAllActionLogs(
        Guid accountId,
        string? action = null,
        int pageSize = 500,
        string? orderBy = "createdat desc",
        int maxPages = 500)
    {
        var logs = new List<DyActionLog>();
        string? token = null;

        for (var i = 0; i < maxPages; i++)
        {
            var page = await ListActionLogsPage(accountId, action, pageSize, token, orderBy);
            logs.AddRange(page.ActionLogs);
            if (string.IsNullOrWhiteSpace(page.NextPageToken))
                break;

            token = page.NextPageToken;
        }

        return logs;
    }

    public async Task<SearchActionLogsPageResult> SearchActionLogsPage(
        List<string>? actions = null,
        Guid? accountId = null,
        Instant? createdAfter = null,
        Instant? createdBefore = null,
        int pageSize = 500,
        string? pageToken = null,
        string? orderBy = "createdat asc")
    {
        var request = new DySearchActionLogsRequest
        {
            PageSize = pageSize,
            PageToken = pageToken ?? string.Empty,
            OrderBy = orderBy ?? "createdat asc"
        };

        if (actions is { Count: > 0 })
            request.Actions.Add(actions);
        if (accountId.HasValue)
            request.AccountId = accountId.Value.ToString();
        if (createdAfter.HasValue)
            request.CreatedAfter = createdAfter.Value.ToTimestamp();
        if (createdBefore.HasValue)
            request.CreatedBefore = createdBefore.Value.ToTimestamp();

        var response = await actionLogs.SearchActionLogsAsync(request);
        return new SearchActionLogsPageResult(
            response.ActionLogs.ToList(),
            string.IsNullOrWhiteSpace(response.NextPageToken) ? null : response.NextPageToken
        );
    }

    public async Task<List<DyActionLog>> SearchAllActionLogs(
        List<string>? actions = null,
        Guid? accountId = null,
        Instant? createdAfter = null,
        Instant? createdBefore = null,
        int pageSize = 500,
        string? orderBy = "createdat asc",
        int maxPages = 500)
    {
        var logs = new List<DyActionLog>();
        string? token = null;

        for (var i = 0; i < maxPages; i++)
        {
            var page = await SearchActionLogsPage(actions, accountId, createdAfter, createdBefore, pageSize, token, orderBy);
            logs.AddRange(page.ActionLogs);
            if (string.IsNullOrWhiteSpace(page.NextPageToken))
                break;

            token = page.NextPageToken;
        }

        return logs;
    }
}
