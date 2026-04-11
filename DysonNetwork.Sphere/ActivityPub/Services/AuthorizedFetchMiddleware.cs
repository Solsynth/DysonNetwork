namespace DysonNetwork.Sphere.ActivityPub.Services;

public class AuthorizedFetchMiddleware(
    RequestDelegate next,
    AppDatabase db,
    ILogger<AuthorizedFetchMiddleware> logger,
    IConfiguration configuration)
{
    private readonly AppDatabase _db = db;
    private readonly bool _enabled = configuration.GetValue<bool>("ActivityPub:AuthorizedFetch", false);
    private readonly string _domain = configuration["ActivityPub:Domain"] ?? "localhost";

    private static readonly string[] ProtectedPaths = 
    {
        "/activitypub/actors/",
        "/posts/"
    };

    private static readonly string[] ExcludedPaths = 
    {
        "/activitypub/actors/",
        "/activitypub/realms/"
    };

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_enabled)
        {
            await next(context);
            return;
        }

        var path = context.Request.Path.Value ?? "";
        var method = context.Request.Method;

        if (method != "GET" && method != "HEAD")
        {
            await next(context);
            return;
        }

        if (!ShouldProtect(path))
        {
            await next(context);
            return;
        }

        if (context.Request.Headers.ContainsKey("Signature"))
        {
            logger.LogDebug("Request to {Path} has signature, allowing", path);
            context.Items["AuthorizedFetch"] = true;
            await next(context);
            return;
        }

        if (IsLocalRequest(context))
        {
            logger.LogDebug("Request to {Path} is local, allowing", path);
            context.Items["AuthorizedFetch"] = true;
            await next(context);
            return;
        }

        if (await IsPublicResourceAsync(path))
        {
            logger.LogDebug("Request to {Path} is public resource, allowing", path);
            context.Items["AuthorizedFetch"] = false;
            await next(context);
            return;
        }

        logger.LogWarning("Unauthorized fetch attempt to {Path}", path);
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "Unauthorized",
            message = "This server requires HTTP signatures for fetching remote resources (Authorized Fetch)"
        });
    }

    private bool ShouldProtect(string path)
    {
        return ProtectedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsLocalRequest(HttpContext context)
    {
        var host = context.Request.Host.Host;
        return host.Equals(_domain, StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith($".{_domain}", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> IsPublicResourceAsync(string path)
    {
        if (path.Contains("/followers", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/following", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/outbox", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/featured", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (path.Contains("/inbox", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return await Task.FromResult(false);
    }
}

public class AuthorizedFetchOptions
{
    public const string SectionName = "ActivityPub:AuthorizedFetch";

    public bool Enabled { get; set; } = false;

    public bool RequireSignaturesForPublicPosts { get; set; } = false;

    public bool RequireSignaturesForActors { get; set; } = true;

    public string[] ExcludedPaths { get; set; } = [];
}

public static class AuthorizedFetchMiddlewareExtensions
{
    public static IApplicationBuilder UseAuthorizedFetch(this IApplicationBuilder app)
    {
        return app.UseMiddleware<AuthorizedFetchMiddleware>();
    }
}

public static class AuthorizedFetchConfiguration
{
    public static void ConfigureAuthorizedFetch(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(AuthorizedFetchOptions.SectionName).Get<AuthorizedFetchOptions>()
                      ?? new AuthorizedFetchOptions();

        services.Configure<AuthorizedFetchOptions>(configuration.GetSection(AuthorizedFetchOptions.SectionName));
    }
}