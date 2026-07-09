using DysonNetwork.Padlock.Models;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Padlock.Auth;

public class BoardAuthServiceGrpc(AppDatabase db) : DyAuthorizedAppService.DyAuthorizedAppServiceBase
{
    public override async Task<DyQueryAuthorizedBoardAppsResponse> QueryAuthorizedBoardApps(
        DyQueryAuthorizedBoardAppsRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.AccountId, out var accountId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid account ID format"));

        // Scopes is stored as jsonb — filter on persisted columns in SQL,
        // then refine client-side for the JSON-baked scope filter.
        var candidates = await db.AuthorizedApps
            .AsNoTracking()
            .Where(x => x.AccountId == accountId)
            .Where(x => x.Type == AuthorizedAppType.Oidc)
            .Where(x => x.DeletedAt == null)
            .ToListAsync();

        var filtered = candidates
            .Where(x => x.Scopes.Contains(PermissionKeys.AccountsProfileBoard, StringComparer.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(request.AppSlug))
            filtered = filtered.Where(x =>
                string.Equals(x.AppSlug, request.AppSlug, StringComparison.OrdinalIgnoreCase));

        var matched = filtered.ToList();
        var totalCount = matched.Count;

        var authorized = matched
            .Skip(request.Offset)
            .Take(request.Take > 0 ? request.Take : 20)
            .ToList();

        var response = new DyQueryAuthorizedBoardAppsResponse { TotalCount = totalCount };
        foreach (var auth in authorized)
        {
            response.Apps.Add(new DyAuthorizedBoardAppDto
            {
                AppId = auth.AppId.ToString(),
                AppSlug = auth.AppSlug ?? string.Empty,
                AppName = auth.AppName ?? string.Empty,
                PublisherName = string.Empty
            });
        }

        return response;
    }
}
