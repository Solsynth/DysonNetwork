using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DysonNetwork.Shared.Auth
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class RequiredPermissionAttribute(string area, string key) : Attribute
    {
        public string Area { get; set; } = area;
        public string Key { get; } = key;
    }

    public class PermissionMiddleware(RequestDelegate next)
    {
        public async Task InvokeAsync(HttpContext httpContext, PermissionService.PermissionServiceClient permissionService, ILogger<PermissionMiddleware> logger)
        {
            var endpoint = httpContext.GetEndpoint();

            var attr = endpoint?.Metadata
                .OfType<RequiredPermissionAttribute>()
                .FirstOrDefault();

            if (attr != null)
            {
                if (httpContext.Items["CurrentUser"] is not Account currentUser)
                {
                    httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await httpContext.Response.WriteAsync("Unauthorized");
                    return;
                }

                // Assuming Account proto has a bool field 'is_superuser' which is generated as 'IsSuperuser'
                if (currentUser.IsSuperuser)
                {
                    // Bypass the permission check for performance
                    await next(httpContext);
                    return;
                }

                var actor = $"user:{currentUser.Id}";

                try
                {
                    var permResp = await permissionService.HasPermissionAsync(new HasPermissionRequest
                    {
                        Actor = actor,
                        Area = attr.Area,
                        Key = attr.Key
                    });

                    if (!permResp.HasPermission)
                    {
                        httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await httpContext.Response.WriteAsync($"Permission {attr.Area}/{attr.Key} was required.");
                        return;
                    }
                }
                catch (RpcException ex)
                {
                    logger.LogError(ex, "gRPC call to PermissionService failed while checking permission {Area}/{Key} for actor {Actor}", attr.Area, attr.Key, actor);
                    httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await httpContext.Response.WriteAsync("Error checking permissions.");
                    return;
                }
            }

            await next(httpContext);
        }
    }
}
