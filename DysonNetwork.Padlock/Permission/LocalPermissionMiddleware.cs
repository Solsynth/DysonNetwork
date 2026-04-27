using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;

namespace DysonNetwork.Padlock.Permission;

public class LocalPermissionMiddleware(RequestDelegate next, ILogger<LocalPermissionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext httpContext, PermissionService pm)
    {
        var endpoint = httpContext.GetEndpoint();

        var attr = endpoint?.Metadata
            .OfType<AskPermissionAttribute>()
            .FirstOrDefault();

        if (attr != null)
        {
            if (httpContext.Items["CurrentUser"] is not SnAccount currentUser)
            {
                await next(httpContext);
                return;
            }

            if (string.IsNullOrWhiteSpace(attr.Key))
            {
                logger.LogWarning("Invalid permission attribute: Key='{Key}'", attr.Key);
                httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await httpContext.Response.WriteAsync("Server configuration error");
                return;
            }

            var currentSession = httpContext.Items["CurrentSession"] as SnAuthSession;

            if (PermissionScopeGate.HasFullScope(currentSession))
            {
                await next(httpContext);
                return;
            }

            if (PermissionScopeGate.ShouldEnforcePermissionScope(currentSession) &&
                !PermissionScopeGate.IsPermissionEnabled(currentSession!.Scopes, attr.Key))
            {
                logger.LogWarning(
                    "Permission omitted by token scope for user {UserId}: required_key={RequiredKey}, matched_scope={MatchedScope}",
                    currentUser.Id,
                    attr.Key,
                    PermissionScopeGate.GetMatchedPermissionScope(currentSession.Scopes, attr.Key) ?? "<none>"
                );
                httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                await httpContext.Response.WriteAsync("Permission omitted by token scope");
                return;
            }

            if (currentUser.IsSuperuser)
            {
                // Bypass the permission check for performance
                logger.LogDebug("Superuser {UserId} bypassing permission check for {Key}", currentUser.Id, attr.Key);
                await next(httpContext);
                return;
            }

            var actor = currentUser.Id.ToString();
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
                    await httpContext.Response.WriteAsync("Insufficient permissions");
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

        await next(httpContext);
    }
}
