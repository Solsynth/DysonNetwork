using Azure.Core;

namespace DysonNetwork.Sphere.Connection;

public class ClientTypeMiddleware(RequestDelegate next)
{
    public async Task Invoke(HttpContext context)
    {
        var headers = context.Request.Headers;
        bool isWebPage;

        // Priority 1: Check for custom header
        if (headers.TryGetValue("X-Client", out var clientType))
        {
            isWebPage = clientType.ToString().Length == 0;
        }
        else
        {
            var userAgent = headers["User-Agent"].ToString();
            var accept = headers["Accept"].ToString();

            // Priority 2: Check known app User-Agent (backward compatibility)
            if (!string.IsNullOrEmpty(userAgent) && userAgent.Contains("Solian"))
                isWebPage = false;
            // Priority 3: Accept header can help infer intent
            else if (!string.IsNullOrEmpty(accept) && accept.Contains("text/html"))
                isWebPage = true;
            else if (!string.IsNullOrEmpty(accept) && accept.Contains("application/json"))
                isWebPage = false;
            else
                isWebPage = true;
        }

        context.Items["IsWebPage"] = isWebPage;

        if (!isWebPage && !context.Request.Path.StartsWithSegments("/api"))
            context.Response.Redirect(
                $"/api{context.Request.Path.Value}{context.Request.QueryString.Value}",
                permanent: false
            );
        else
            await next(context);
    }
}