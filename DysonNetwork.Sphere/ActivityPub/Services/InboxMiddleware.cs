using System.Text;
using System.Text.Json;

namespace DysonNetwork.Sphere.ActivityPub.Services;

public class InboxValidationMiddleware(
    RequestDelegate next,
    AppDatabase db,
    ISignatureService signatureService,
    FediverseModerationService moderationService,
    ILogger<InboxValidationMiddleware> logger
)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        
        if (!IsInboxEndpoint(path))
        {
            await next(context);
            return;
        }

        logger.LogInformation("[Inbox] Incoming request to {Path} from {RemoteIp}", 
            path, context.Connection.RemoteIpAddress);

        if (!context.Request.Headers.TryGetValue("Signature", out var signatureHeader))
        {
            logger.LogWarning("[Inbox] Request missing Signature header. Path: {Path}", path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Missing signature" });
            return;
        }

        string actorUri;
        try
        {
            var header = signatureHeader.ToString();
            var parsed = HttpSignature.Parse(header);
            actorUri = parsed.KeyId.Split('#')[0];
            logger.LogDebug("[Inbox] Signature parsed. KeyId: {KeyId}, Algorithm: {Algorithm}, Headers: {Headers}",
                parsed.KeyId, parsed.Algorithm, string.Join(" ", parsed.Headers));
        }
        catch (HttpSignatureException ex)
        {
            logger.LogWarning("[Inbox] Invalid signature header: {Error}", ex.Message);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid signature" });
            return;
        }

        logger.LogInformation("[Inbox] Signature header parsed for actor: {ActorUri}", actorUri);
        context.Items["InboxActorUri"] = actorUri;

        await next(context);
    }

    private static bool IsInboxEndpoint(string path)
    {
        return path.Contains("/inbox", StringComparison.OrdinalIgnoreCase) &&
               (path.Contains("/activitypub/", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("/actors/", StringComparison.OrdinalIgnoreCase));
    }
}

public class InboxActivityMiddleware(
    RequestDelegate next,
    AppDatabase db,
    IActorDiscoveryService discoveryService,
    ILogger<InboxActivityMiddleware> logger
)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        
        if (!IsInboxEndpoint(path) || context.Request.Method != HttpMethod.Post.Method)
        {
            await next(context);
            return;
        }

        if (!context.Items.TryGetValue("InboxActorUri", out var actorUriObj) || actorUriObj is not string actorUri)
        {
            logger.LogWarning("[Inbox] Actor not validated before activity parsing");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Actor not validated" });
            return;
        }

        try
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;

            Dictionary<string, object>? activity;
            try
            {
                activity = JsonSerializer.Deserialize<Dictionary<string, object>>(body);
            }
            catch (JsonException ex)
            {
                logger.LogWarning("[Inbox] Invalid JSON from {Actor}: {Error}", actorUri, ex.Message);
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = "Invalid JSON" });
                return;
            }

            if (activity == null)
            {
                logger.LogWarning("[Inbox] Empty activity from {Actor}", actorUri);
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = "Empty activity" });
                return;
            }

            var activityType = activity.GetValueOrDefault("type")?.ToString();
            var activityId = activity.GetValueOrDefault("id")?.ToString();
            var objectUri = activity.GetValueOrDefault("object")?.ToString();
            
            logger.LogInformation("[Inbox] Parsed activity from {Actor}. Type: {Type}, Id: {Id}, Object: {Object}",
                actorUri, activityType, activityId, objectUri);

            context.Items["InboxActivity"] = activity;
            context.Items["InboxActivityType"] = activityType ?? "Unknown";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Inbox] Error parsing activity from {Actor}", actorUri);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = "Processing error" });
            return;
        }

        await next(context);
    }

    private static bool IsInboxEndpoint(string path)
    {
        return path.Contains("/inbox", StringComparison.OrdinalIgnoreCase);
    }
}

public class InboxRateLimitMiddleware(
    RequestDelegate next,
    ILogger<InboxRateLimitMiddleware> logger
)
{
    private static readonly Dictionary<string, DateTime> _lastRequest = new();
    private static readonly Dictionary<string, int> _requestCount = new();
    private static readonly TimeSpan _window = TimeSpan.FromMinutes(1);
    private const int MaxRequestsPerWindow = 100;

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        
        if (!path.Contains("/inbox", StringComparison.OrdinalIgnoreCase) || context.Request.Method != HttpMethod.Post.Method)
        {
            await next(context);
            return;
        }

        var actorUri = context.Items.TryGetValue("InboxActorUri", out var uriObj) ? uriObj?.ToString() : null;
        if (string.IsNullOrEmpty(actorUri))
        {
            await next(context);
            return;
        }

        var now = DateTime.UtcNow;
        var key = actorUri;

        lock (_lastRequest)
        {
            if (_lastRequest.TryGetValue(key, out var last) && now - last > _window)
            {
                _requestCount[key] = 0;
                _lastRequest[key] = now;
            }

            _requestCount.TryGetValue(key, out var count);
            if (count >= MaxRequestsPerWindow)
            {
                logger.LogWarning("[Inbox] Rate limit exceeded for actor: {Actor}. Count: {Count}/{Max}", 
                    actorUri, count, MaxRequestsPerWindow);
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers.Append("Retry-After", "60");
                return;
            }

            _requestCount[key] = count + 1;
            _lastRequest[key] = now;
        }

        await next(context);
    }
}

public static class InboxMiddlewareExtensions
{
    public static IApplicationBuilder UseInboxValidation(this IApplicationBuilder app)
    {
        return app.UseMiddleware<InboxValidationMiddleware>();
    }

    public static IApplicationBuilder UseInboxActivityParsing(this IApplicationBuilder app)
    {
        return app.UseMiddleware<InboxActivityMiddleware>();
    }

    public static IApplicationBuilder UseInboxRateLimiting(this IApplicationBuilder app)
    {
        return app.UseMiddleware<InboxRateLimitMiddleware>();
    }
}