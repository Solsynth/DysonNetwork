using DysonNetwork.Sphere.Activity;
using DysonNetwork.Sphere.Connection;
using DysonNetwork.Sphere.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using NodaTime;
using NodaTime;

namespace DysonNetwork.Sphere.Account;

public class AccountEventService(
    AppDatabase db,
    AccountService acc,
    ActivityService act,
    WebSocketService ws,
    IMemoryCache cache
)
{
    private static readonly Random Random = new();
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

    private const int FortuneTipCount = 7; // This will be the max index for each type (positive/negative)
    private const string CaptchaCacheKey = "checkin_captcha_";
    private const int CaptchaProbabilityPercent = 20;

    public bool CheckInDailyDoAskCaptcha(Account user)
    {
        var cacheKey = $"{CaptchaCacheKey}{user.Id}";
        if (cache.TryGetValue(cacheKey, out bool? needsCaptcha))
            return needsCaptcha!.Value;

        var result = Random.Next(100) < CaptchaProbabilityPercent;
        cache.Set(cacheKey, result, TimeSpan.FromHours(24));
        return result;
    }

    public async Task<bool> CheckInDailyIsAvailable(Account user)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var lastCheckIn = await db.AccountCheckInResults
            .Where(x => x.AccountId == user.Id)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();

        if (lastCheckIn == null)
            return true;

        var lastDate = lastCheckIn.CreatedAt.InUtc().Date;
        var currentDate = now.InUtc().Date;

        return lastDate < currentDate;
    }

    public async Task<CheckInResult> CheckInDaily(Account user)
    {
        var localizer = AccountService.GetEventLocalizer(user.Language);

        // Generate 2 positive tips
        var positiveIndices = Enumerable.Range(1, FortuneTipCount)
            .OrderBy(_ => Random.Next())
            .Take(2)
            .ToList();
        var tips = positiveIndices.Select(index => new FortuneTip
        {
            IsPositive = true, Title = localizer[$"FortuneTipPositiveTitle_{index}"].Value,
            Content = localizer[$"FortuneTipPositiveContent_{index}"].Value
        }).ToList();

        // Generate 2 negative tips
        var negativeIndices = Enumerable.Range(1, FortuneTipCount)
            .Except(positiveIndices)
            .OrderBy(_ => Random.Next())
            .Take(2)
            .ToList();
        tips.AddRange(negativeIndices.Select(index => new FortuneTip
        {
            IsPositive = false, Title = localizer[$"FortuneTipNegativeTitle_{index}"].Value,
            Content = localizer[$"FortuneTipNegativeContent_{index}"].Value
        }));

        var result = new CheckInResult
        {
            Tips = tips,
            Level = (CheckInResultLevel)Random.Next(Enum.GetValues<CheckInResultLevel>().Length),
            AccountId = user.Id
        };

        db.AccountCheckInResults.Add(result);
        await db.SaveChangesAsync();

        await act.CreateActivity(
            user,
            "accounts.check-in",
            $"account.check-in/{result.Id}",
            ActivityVisibility.Friends
        );

        return result;
    }
}