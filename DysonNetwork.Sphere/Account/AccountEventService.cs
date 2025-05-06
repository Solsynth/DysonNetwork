using DysonNetwork.Sphere.Activity;
using DysonNetwork.Sphere.Connection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NodaTime;
using NodaTime;

namespace DysonNetwork.Sphere.Account;

public class AccountEventService(AppDatabase db, ActivityService act, WebSocketService ws, IMemoryCache cache)
{
    private const string StatusCacheKey = "account_status_";

    public async Task<Status> GetStatus(long userId)
    {
        var cacheKey = $"{StatusCacheKey}{userId}";
        if (cache.TryGetValue(cacheKey, out Status? cachedStatus))
            return cachedStatus!;

        var now = SystemClock.Instance.GetCurrentInstant();
        var status = await db.AccountStatuses
            .Where(e => e.AccountId == userId)
            .Where(e => e.ClearedAt == null || e.ClearedAt > now)
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync();
        if (status is not null)
        {
            cache.Set(cacheKey, status, TimeSpan.FromMinutes(5));
            return status;
        }

        var isOnline = ws.GetAccountIsConnected(userId);
        if (isOnline)
        {
            return new Status
            {
                Attitude = StatusAttitude.Neutral,
                IsOnline = true,
                Label = "Online",
                AccountId = userId,
            };
        }

        return new Status
        {
            Attitude = StatusAttitude.Neutral,
            IsOnline = false,
            Label = "Offline",
            AccountId = userId,
        };
    }

    public async Task<Status> CreateStatus(Account user, Status status)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        await db.AccountStatuses
            .Where(x => x.AccountId == user.Id && (x.ClearedAt == null || x.ClearedAt > now))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ClearedAt, now));

        db.AccountStatuses.Add(status);
        await db.SaveChangesAsync();

        await act.CreateActivity(
            user,
            "accounts.status",
            $"account.statuses/{status.Id}",
            ActivityVisibility.Friends
        );

        return status;
    }

    public async Task ClearStatus(Account user, Status status)
    {
        status.ClearedAt = SystemClock.Instance.GetCurrentInstant();
        db.Update(status);
        await db.SaveChangesAsync();
    }
}