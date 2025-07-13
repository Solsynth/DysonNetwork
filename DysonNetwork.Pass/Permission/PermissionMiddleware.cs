namespace DysonNetwork.Pass.Permission;

using System;

[AttributeUsage(AttributeTargets.Method)]
public class RequiredPermissionAttribute(string area, string key) : Attribute
{
    public string Area { get; set; } = area;
    public string Key { get; } = key;
}

public class PermissionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext httpContext, PermissionService pm)
    {
        var endpoint = httpContext.GetEndpoint();

        var attr = endpoint?.Metadata
            .OfType<RequiredPermissionAttribute>()
            .FirstOrDefault();

        if (attr != null)
        {
            if (httpContext.Items["CurrentUser"] is not Account.Account currentUser)
            {
                httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                await httpContext.Response.WriteAsync("Unauthorized");
                return;
            }

            if (currentUser.IsSuperuser)
            {
                // Bypass the permission check for performance
                await next(httpContext);
                return;
            }

            var actor = $"user:{currentUser.Id}";
            var permNode = await pm.GetPermissionAsync<bool>(actor, attr.Area, attr.Key);

            if (!permNode)
            {
                httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                await httpContext.Response.WriteAsync($"Permission {attr.Area}/{attr.Key} = {true} was required.");
                return;
            }
        }

        await next(httpContext);
    } 
}