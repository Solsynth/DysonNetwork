using System.Globalization;
using DysonNetwork.Pass;
using DysonNetwork.Pass.Connection;
using DysonNetwork.Pass.Storage;
using DysonNetwork.Pass.Wallet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Localization;
using NodaTime;
using Org.BouncyCastle.Asn1.X509;

namespace DysonNetwork.Pass.Account;

public class AccountEventService(
    AppDatabase db,
    WebSocketService ws,
    ICacheService cache,
    PaymentService payment,
    IStringLocalizer<Localization.AccountEventResource> localizer
)
{
    private static readonly Random Random = new();
    private const string StatusCacheKey = "AccountStatus_";

    public void PurgeStatusCache(Guid userId)
    {
        var cacheKey = $"{StatusCacheKey}{userId}";
        cache.RemoveAsync(cacheKey);
    }

    public async Task<Status> GetStatus(Guid userId)
    {
        var cacheKey = $"{StatusCacheKey}{userId}";
        var cachedStatus = await cache.GetAsync<Status>(cacheKey);
        if (cachedStatus is not null)
        {
            cachedStatus!.IsOnline = !cachedStatus.IsInvisible && ws.GetAccountIsConnected(userId);
            return cachedStatus;
        }

        var now = SystemClock.Instance.GetCurrentInstant();
        var status = await db.AccountStatuses
            .Where(e => e.AccountId == userId)
            .Where(e => e.ClearedAt == null || e.ClearedAt > now)
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync();
        var isOnline = ws.GetAccountIsConnected(userId);
        if (status is not null)
        {
            status.IsOnline = !status.IsInvisible && isOnline;
            await cache.SetWithGroupsAsync(cacheKey, status, [$"{AccountService.AccountCachePrefix}{status.AccountId}"],
                TimeSpan.FromMinutes(5));
            return status;
        }

        if (isOnline)
        {
            return new Status
            {
                Attitude = StatusAttitude.Neutral,
                IsOnline = true,
                IsCustomized = false,
                Label = "Online",
                AccountId = userId,
            };
        }

                return new Status
        {
            Attitude = StatusAttitude.Neutral,
            IsOnline = false,
            IsCustomized = false,
            Label = "Offline",
            AccountId = userId,
        };
    }

    public async Task<Dictionary<Guid, Status>> GetStatuses(List<Guid> userIds)
    {
        var results = new Dictionary<Guid, Status>();
        var cacheMissUserIds = new List<Guid>();

        foreach (var userId in userIds)
        {
            var cacheKey = $"{StatusCacheKey}{userId}";
            var cachedStatus = await cache.GetAsync<Status>(cacheKey);
            if (cachedStatus != null)
            {
                cachedStatus.IsOnline = !cachedStatus.IsInvisible && ws.GetAccountIsConnected(userId);
                results[userId] = cachedStatus;
            }
            else
            {
                cacheMissUserIds.Add(userId);
            }
        }

        if (cacheMissUserIds.Any())
        {
            var now = SystemClock.Instance.GetCurrentInstant();
            var statusesFromDb = await db.AccountStatuses
                .Where(e => cacheMissUserIds.Contains(e.AccountId))
                .Where(e => e.ClearedAt == null || e.ClearedAt > now)
                .GroupBy(e => e.AccountId)
                .Select(g => g.OrderByDescending(e => e.CreatedAt).First())
                .ToListAsync();

            var foundUserIds = new HashSet<Guid>();

            foreach (var status in statusesFromDb)
            {
                var isOnline = ws.GetAccountIsConnected(status.AccountId);
                status.IsOnline = !status.IsInvisible && isOnline;
                results[status.AccountId] = status;
                var cacheKey = $"{StatusCacheKey}{status.AccountId}";
                await cache.SetAsync(cacheKey, status, TimeSpan.FromMinutes(5));
                foundUserIds.Add(status.AccountId);
            }

            var usersWithoutStatus = cacheMissUserIds.Except(foundUserIds).ToList();
            if (usersWithoutStatus.Any())
            {
                foreach (var userId in usersWithoutStatus)
                {
                    var isOnline = ws.GetAccountIsConnected(userId);
                    var defaultStatus = new Status
                    {
                        Attitude = StatusAttitude.Neutral,
                        IsOnline = isOnline,
                        IsCustomized = false,
                        Label = isOnline ? "Online" : "Offline",
                        AccountId = userId,
                    };
                    results[userId] = defaultStatus;
                }
            }
        }

        return results;
    }

    public async Task<Status> CreateStatus(Account user, Status status)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        await db.AccountStatuses
            .Where(x => x.AccountId == user.Id && (x.ClearedAt == null || x.ClearedAt > now))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ClearedAt, now));

        db.AccountStatuses.Add(status);
        await db.SaveChangesAsync();

        return status;
    }

    public async Task ClearStatus(Account user, Status status)
    {
        status.ClearedAt = SystemClock.Instance.GetCurrentInstant();
        db.Update(status);
        await db.SaveChangesAsync();
        PurgeStatusCache(user.Id);
    }

    private const int FortuneTipCount = 7; // This will be the max index for each type (positive/negative)
    private const string CaptchaCacheKey = "CheckInCaptcha_";
    private const int CaptchaProbabilityPercent = 20;

    public async Task<bool> CheckInDailyDoAskCaptcha(Account user)
    {
        var cacheKey = $"{CaptchaCacheKey}{user.Id}";
        var needsCaptcha = await cache.GetAsync<bool?>(cacheKey);
        if (needsCaptcha is not null)
            return needsCaptcha!.Value;

        var result = Random.Next(100) < CaptchaProbabilityPercent;
        await cache.SetAsync(cacheKey, result, TimeSpan.FromHours(24));
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

    public const string CheckInLockKey = "CheckInLock_";

    public async Task<CheckInResult> CheckInDaily(Account user)
    {
        var lockKey = $"{CheckInLockKey}{user.Id}";
        
        try
        {
            var lk = await cache.AcquireLockAsync(lockKey, TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(100));
            
            if (lk != null)
                await lk.ReleaseAsync();
        }
        catch
        {
            // Ignore errors from this pre-check
        }
        
        // Now try to acquire the lock properly
        await using var lockObj = await cache.AcquireLockAsync(lockKey, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5));
        if (lockObj is null) throw new InvalidOperationException("Check-in was in progress.");

        var cultureInfo = new CultureInfo(user.Language, false);
        CultureInfo.CurrentCulture = cultureInfo;
        CultureInfo.CurrentUICulture = cultureInfo;

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
            AccountId = user.Id,
            RewardExperience = 100,
            RewardPoints = 10,
        };

        var now = SystemClock.Instance.GetCurrentInstant().InUtc().Date;
        try
        {
            if (result.RewardPoints.HasValue)
                await payment.CreateTransactionWithAccountAsync(
                    null,
                    user.Id,
                    WalletCurrency.SourcePoint,
                    result.RewardPoints.Value,
                    $"Check-in reward on {now:yyyy/MM/dd}"
                );
        }
        catch
        {
            result.RewardPoints = null;
        }

        await db.AccountProfiles
            .Where(p => p.AccountId == user.Id)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(b => b.Experience, b => b.Experience + result.RewardExperience)
            );
        db.AccountCheckInResults.Add(result);
        await db.SaveChangesAsync();  // Don't forget to save changes to the database

        // The lock will be automatically released by the await using statement
        return result;
    }

    public async Task<List<DailyEventResponse>> GetEventCalendar(Account user, int month, int year = 0,
        bool replaceInvisible = false)
    {
        if (year == 0)
            year = SystemClock.Instance.GetCurrentInstant().InUtc().Date.Year;

        // Create start and end dates for the specified month
        var startOfMonth = new LocalDate(year, month, 1).AtStartOfDayInZone(DateTimeZone.Utc).ToInstant();
        var endOfMonth = startOfMonth.Plus(Duration.FromDays(DateTime.DaysInMonth(year, month)));

        var statuses = await db.AccountStatuses
            .AsNoTracking()
            .TagWith("GetEventCalendar_Statuses")
            .Where(x => x.AccountId == user.Id && x.CreatedAt >= startOfMonth && x.CreatedAt < endOfMonth)
            .Select(x => new Status
            {
                Id = x.Id,
                Attitude = x.Attitude,
                IsInvisible = !replaceInvisible && x.IsInvisible,
                IsNotDisturb = x.IsNotDisturb,
                Label = x.Label,
                ClearedAt = x.ClearedAt,
                AccountId = x.AccountId,
                CreatedAt = x.CreatedAt
            })
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();

        var checkIn = await db.AccountCheckInResults
            .AsNoTracking()
            .TagWith("GetEventCalendar_CheckIn")
            .Where(x => x.AccountId == user.Id && x.CreatedAt >= startOfMonth && x.CreatedAt < endOfMonth)
            .ToListAsync();

        var dates = Enumerable.Range(1, DateTime.DaysInMonth(year, month))
            .Select(day => new LocalDate(year, month, day).AtStartOfDayInZone(DateTimeZone.Utc).ToInstant())
            .ToList();

        var statusesByDate = statuses
            .GroupBy(s => s.CreatedAt.InUtc().Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        var checkInByDate = checkIn
            .ToDictionary(c => c.CreatedAt.InUtc().Date);

        return dates.Select(date =>
        {
            var utcDate = date.InUtc().Date;
            return new DailyEventResponse
            {
                Date = date,
                CheckInResult = checkInByDate.GetValueOrDefault(utcDate),
                Statuses = statusesByDate.GetValueOrDefault(utcDate, new List<Status>())
            };
        }).ToList();
    }
}