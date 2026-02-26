using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DysonNetwork.Shared.Auth;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class AskPermissionAttribute(string key, PermissionNodeActorType type = PermissionNodeActorType.Account)
    : Attribute
{
    public string Key { get; } = key;
    public PermissionNodeActorType Type { get; } = type;
}

public class RemotePermissionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext httpContext, PermissionService.PermissionServiceClient permissionService,
        ILogger<RemotePermissionMiddleware> logger)
    {
        var endpoint = httpContext.GetEndpoint();

        var attr = endpoint?.Metadata
            .OfType<AskPermissionAttribute>()
            .FirstOrDefault();

        if (attr != null)
        {
            if (httpContext.Items["CurrentUser"] is not DyAccount currentUser)
            {
                httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                await httpContext.Response.WriteAsync("Unauthorized");
                return;
            }

            // Superuser will bypass all the permission check
            if (currentUser.IsSuperuser)
            {
                await next(httpContext);
                return;
            }

            try
            {
                var permResp = await permissionService.HasPermissionAsync(new HasPermissionRequest
                {
                    Actor = currentUser.Id,
                    Key = attr.Key
                });

                if (!permResp.HasPermission)
                {
                    httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await httpContext.Response.WriteAsync($"Permission {attr.Key} was required.");
                    return;
                }
            }
            catch (RpcException ex)
            {
                logger.LogError(ex,
                    "gRPC call to PermissionService failed while checking permission {Key} for actor {Actor}", attr.Key,
                    currentUser.Id
                );
                httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await httpContext.Response.WriteAsync("Error checking permissions.");
                return;
            }
        }

        await next(httpContext);
    }
}