namespace DysonNetwork.Pass.Permission;

using System;
using Microsoft.Extensions.Logging;
using DysonNetwork.Shared.Models;

[AttributeUsage(AttributeTargets.Method)]
public class RequiredPermissionAttribute(string area, string key) : Attribute
{
    public string Area { get; set; } = area;
    public string Key { get; } = key;
}

public class PermissionMiddleware(RequestDelegate next, ILogger<PermissionMiddleware> logger)
{
    private const string ForbiddenMessage = "Insufficient permissions";
    private const string UnauthorizedMessage = "Authentication required";

    public async Task InvokeAsync(HttpContext httpContext, PermissionService pm)
    {
        var endpoint = httpContext.GetEndpoint();

        var attr = endpoint?.Metadata
            .OfType<RequiredPermissionAttribute>()
            .FirstOrDefault();

        if (attr != null)
        {
            // Validate permission attributes
            if (string.IsNullOrWhiteSpace(attr.Area) || string.IsNullOrWhiteSpace(attr.Key))
            {
                logger.LogWarning("Invalid permission attribute: Area='{Area}', Key='{Key}'", attr.Area, attr.Key);
                httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await httpContext.Response.WriteAsync("Server configuration error");
                return;
            }

            if (httpContext.Items["CurrentUser"] is not SnAccount currentUser)
            {
                logger.LogWarning("Permission check failed: No authenticated user for {Area}/{Key}", attr.Area, attr.Key);
                httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await httpContext.Response.WriteAsync(UnauthorizedMessage);
                return;
            }

            if (currentUser.IsSuperuser)
            {
                // Bypass the permission check for performance
                logger.LogDebug("Superuser {UserId} bypassing permission check for {Area}/{Key}",
                    currentUser.Id, attr.Area, attr.Key);
                await next(httpContext);
                return;
            }

            var actor = $"user:{currentUser.Id}";
            try
            {
                var permNode = await pm.GetPermissionAsync<bool>(actor, attr.Area, attr.Key);

                if (!permNode)
                {
                    logger.LogWarning("Permission denied for user {UserId}: {Area}/{Key}",
                        currentUser.Id, attr.Area, attr.Key);
                    httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await httpContext.Response.WriteAsync(ForbiddenMessage);
                    return;
                }

                logger.LogDebug("Permission granted for user {UserId}: {Area}/{Key}",
                    currentUser.Id, attr.Area, attr.Key);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error checking permission for user {UserId}: {Area}/{Key}",
                    currentUser.Id, attr.Area, attr.Key);
                httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await httpContext.Response.WriteAsync("Permission check failed");
                return;
            }
        }

        await next(httpContext);
    }
}
