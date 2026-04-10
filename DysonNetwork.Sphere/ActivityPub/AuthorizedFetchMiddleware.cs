using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.ActivityPub;

public class AuthorizedFetchMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AppDatabase _db;
    private readonly ILogger<AuthorizedFetchMiddleware> _logger;
    private readonly bool _enabled;
    private readonly string _domain;

    private static readonly string[] _protectedPaths = 
    {
        "/activitypub/actors/",
        "/posts/"
    };

    private static readonly string[] _excludedPaths = 
    {
        "/activitypub/actors/",
        "/activitypub/realms/"
    };

    public AuthorizedFetchMiddleware(
        RequestDelegate next,
        AppDatabase db,
        ILogger<AuthorizedFetchMiddleware> logger,
        IConfiguration configuration
    )
    {
        _next = next;
        _db = db;
        _logger = logger;
        _enabled = configuration.GetValue<bool>("ActivityPub:AuthorizedFetch", false);
        _domain = configuration["ActivityPub:Domain"] ?? "localhost";
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_enabled)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? "";
        var method = context.Request.Method;

        if (method != "GET" && method != "HEAD")
        {
            await _next(context);
            return;
        }

        if (!ShouldProtect(path))
        {
            await _next(context);
            return;
        }

        if (context.Request.Headers.ContainsKey("Signature"))
        {
            _logger.LogDebug("Request to {Path} has signature, allowing", path);
            context.Items["AuthorizedFetch"] = true;
            await _next(context);
            return;
        }

        if (IsLocalRequest(context))
        {
            _logger.LogDebug("Request to {Path} is local, allowing", path);
            context.Items["AuthorizedFetch"] = true;
            await _next(context);
            return;
        }

        if (await IsPublicResourceAsync(path))
        {
            _logger.LogDebug("Request to {Path} is public resource, allowing", path);
            context.Items["AuthorizedFetch"] = false;
            await _next(context);
            return;
        }

        _logger.LogWarning("Unauthorized fetch attempt to {Path}", path);
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "Unauthorized",
            message = "This server requires HTTP signatures for fetching remote resources (Authorized Fetch)"
        });
    }

    private bool ShouldProtect(string path)
    {
        return _protectedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
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