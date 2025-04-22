using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DysonNetwork.Sphere.Auth;

public class UserInfoMiddleware(RequestDelegate next, IMemoryCache cache)
{
    public async Task InvokeAsync(HttpContext context, AppDatabase db)
    {
        var userIdClaim = context.User.FindFirst("user_id")?.Value;
        if (userIdClaim is not null && long.TryParse(userIdClaim, out var userId))
        {
            if (!cache.TryGetValue($"user_{userId}", out Account.Account? user))
            {
                user = await db.Accounts
                    .Include(e => e.Profile)
                    .Include(e => e.Profile.Picture)
                    .Include(e => e.Profile.Background)
                    .Where(e => e.Id == userId)
                    .FirstOrDefaultAsync();

                if (user is not null)
                {
                    cache.Set($"user_{userId}", user, TimeSpan.FromMinutes(10));
                }
            }

            if (user is not null)
            {
                context.Items["CurrentUser"] = user;
                var prefix = user.IsSuperuser ? "super:" : "";
                context.Items["CurrentIdentity"] = $"{prefix}{userId}";
            }
        }

        await next(context);
    }
}