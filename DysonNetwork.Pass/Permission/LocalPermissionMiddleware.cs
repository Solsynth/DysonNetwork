using DysonNetwork.Shared.Auth;

namespace DysonNetwork.Pass.Permission;

using System;
using Microsoft.Extensions.Logging;
using Shared.Models;

public class LocalPermissionMiddleware(RequestDelegate next, ILogger<LocalPermissionMiddleware> logger)
{
    private const string ForbiddenMessage = "Insufficient permissions";
    private const string UnauthorizedMessage = "Authentication required";

    public async Task InvokeAsync(HttpContext httpContext, PermissionService pm)
    {
        var endpoint = httpContext.GetEndpoint();

        var attr = endpoint?.Metadata
            .OfType<AskPermissionAttribute>()
            .FirstOrDefault();

        if (attr != null)
        {
            // Validate permission attributes
            if (string.IsNullOrWhiteSpace(attr.Key))
            {
                logger.LogWarning("Invalid permission attribute: Key='{Key}'", attr.Key);
                httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await httpContext.Response.WriteAsync("Server configuration error");
                return;
            }

            if (httpContext.Items["CurrentUser"] is not SnAccount currentUser)
            {
                logger.LogWarning("Permission check failed: No authenticated user for {Key}", attr.Key);
                httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await httpContext.Response.WriteAsync(UnauthorizedMessage);
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
                    logger.LogWarning("Permission denied for user {UserId}: {Key}", currentUser.Id, attr.Key);
                    httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await httpContext.Response.WriteAsync(ForbiddenMessage);
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
