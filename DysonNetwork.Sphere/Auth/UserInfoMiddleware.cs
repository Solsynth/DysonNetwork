using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DysonNetwork.Sphere.Auth;

public class UserInfoMiddleware(RequestDelegate next, IMemoryCache cache)
{
    public async Task InvokeAsync(HttpContext context, AppDatabase db)
    {
        var sessionIdClaim = context.User.FindFirst("session_id")?.Value;
        if (sessionIdClaim is not null && Guid.TryParse(sessionIdClaim, out var sessionId))
        {
            if (!cache.TryGetValue($"dyn_auth_{sessionId}", out Session? session))
            {
                session = await db.AuthSessions
                    .Include(e => e.Challenge)
                    .Include(e => e.Account)
                    .Include(e => e.Account.Profile)
                    .Where(e => e.Id == sessionId)
                    .FirstOrDefaultAsync();

                if (session is not null)
                {
                    cache.Set($"dyn_auth_{sessionId}", session, TimeSpan.FromHours(1));
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