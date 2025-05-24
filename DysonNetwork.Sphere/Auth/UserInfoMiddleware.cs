using DysonNetwork.Sphere.Account;
using DysonNetwork.Sphere.Storage;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Auth;

public class UserInfoMiddleware(RequestDelegate next, ICacheService cache)
{
    public async Task InvokeAsync(HttpContext context, AppDatabase db)
    {
        var sessionIdClaim = context.User.FindFirst("session_id")?.Value;
        if (sessionIdClaim is not null && Guid.TryParse(sessionIdClaim, out var sessionId))
        {
            var session = await cache.GetAsync<Session>($"Auth_{sessionId}");
            if (session is null)
            {
                session = await db.AuthSessions
                    .Where(e => e.Id == sessionId)
                    .Include(e => e.Challenge)
                    .Include(e => e.Account)
                    .ThenInclude(e => e.Profile)
                    .FirstOrDefaultAsync();

                if (session is not null)
                {
                    await cache.SetWithGroupsAsync($"Auth_{sessionId}", session,
                        [$"{AccountService.AccountCachePrefix}{session.Account.Id}"], TimeSpan.FromHours(1));
                }
            }

            if (session is not null)
            {
                context.Items["CurrentUser"] = session.Account;
                context.Items["CurrentSession"] = session;
            }
        }

        await next(context);
    }
}