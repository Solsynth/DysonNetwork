namespace DysonNetwork.Gateway.Health;

using Microsoft.AspNetCore.Http;

public sealed class GatewayReadinessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, GatewayReadinessStore store)
    {
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await next(context);
            return;
        }

        var readiness = store.Current;

        // Only core services participate in readiness gating
        var notReadyCoreServices = readiness.Services
            .Where(kv => GatewayConstant.CoreServiceNames.Contains(kv.Key))
            .Where(kv => !kv.Value.IsHealthy)
            .Select(kv => kv.Key)
            .ToArray();

        if (notReadyCoreServices.Length > 0)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            var unavailableServices = string.Join(", ", notReadyCoreServices);
            context.Response.Headers["X-NotReady"] = unavailableServices;
            await context.Response.WriteAsync("Solar Network is warming up. Try again later please.");
            return;
        }

        await next(context);
    }
}