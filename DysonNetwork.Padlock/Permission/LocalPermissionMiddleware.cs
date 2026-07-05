using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;

namespace DysonNetwork.Padlock.Permission;

public class LocalPermissionMiddleware(RequestDelegate next, ILogger<LocalPermissionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext httpContext, PermissionService pm)
    {
        var endpoint = httpContext.GetEndpoint();

        var attrs = endpoint?.Metadata
            .OfType<AskPermissionAttribute>()
            .ToList();

        if (attrs is { Count: > 0 })
        {
            if (httpContext.Items["CurrentUser"] is not SnAccount currentUser)
            {
                httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                await httpContext.Response.WriteAsync("Unauthorized");
                return;
            }

            foreach (var attr in attrs)
            {
                if (string.IsNullOrWhiteSpace(attr.Key))
                {
                    logger.LogWarning("Invalid permission attribute: Key='{Key}'", attr.Key);
                    httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await httpContext.Response.WriteAsync("Server configuration error");
                    return;
                }
            }

            var currentSession = httpContext.Items["CurrentSession"] as SnAuthSession;

            if (PermissionScopeGate.HasFullScope(currentSession))
            {
                await next(httpContext);
                return;
            }

            if (PermissionScopeGate.ShouldEnforcePermissionScope(currentSession))
            {
                foreach (var attr in attrs)
                {
                    if (PermissionScopeGate.IsPermissionEnabled(currentSession!.Scopes, attr.Key))
                        continue;

                    logger.LogWarning(
                        "Permission omitted by token scope for user {UserId}: required_key={RequiredKey}, matched_scope={MatchedScope}",
                        currentUser.Id,
                        attr.Key,
                        PermissionScopeGate.GetMatchedPermissionScope(currentSession.Scopes, attr.Key) ?? "<none>"
                    );
                    httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await httpContext.Response.WriteAsync($"Permission {attr.Key} was omitted by token scope.");
                    return;
                }
            }

            if (currentUser.IsSuperuser)
            {
                logger.LogDebug("Superuser {UserId} bypassing permission checks", currentUser.Id);
                await next(httpContext);
                return;
            }

            var actor = currentUser.Id.ToString();
            foreach (var attr in attrs)
            {
                try
                {
                    var permNode = await pm.GetPermissionAsync<bool>(actor, attr.Key);

                    if (!permNode)
                    {
                        logger.LogWarning(
                            "Permission denied for user {UserId}: required_key={RequiredKey}, matched_scope={MatchedScope}",
                            currentUser.Id,
                            attr.Key,
                            currentSession is not null
                                ? PermissionScopeGate.GetMatchedPermissionScope(currentSession.Scopes, attr.Key) ?? "<none>"
                                : "<session-unavailable>"
                        );
                        httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await httpContext.Response.WriteAsync($"Permission {attr.Key} was required.");
                        return;
                    }

                    logger.LogDebug("Permission granted for user {UserId}: {Key}", currentUser.Id, attr.Key);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error checking permission for user {UserId}: {Key}", currentUser.Id, attr.Key);
                    httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await httpContext.Response.WriteAsync("Permission check failed");
                    return;
                }
            }
        }

        await next(httpContext);
    }
}
