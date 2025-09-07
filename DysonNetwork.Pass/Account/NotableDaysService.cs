using DysonNetwork.Shared.Cache;
using Nager.Holiday;

namespace DysonNetwork.Pass.Account;

public class NotableDaysService(ICacheService cache)
{
    private const string NotableDaysCacheKeyPrefix = "notable:";

    public async Task<List<NotableDay>> GetNotableDays(int? year, string regionCode)
    {
        year ??= DateTime.UtcNow.Year;

        // Generate cache key using year and region code
        var cacheKey = $"{NotableDaysCacheKeyPrefix}:{year}:{regionCode}";

        // Try to get from cache first
        var (found, cachedDays) = await cache.GetAsyncWithStatus<List<NotableDay>>(cacheKey);
        if (found && cachedDays != null)
        {
            return cachedDays;
        }

        // If not in cache, fetch from API
        using var holidayClient = new HolidayClient();
        var holidays = await holidayClient.GetHolidaysAsync(year.Value, regionCode);
        var days = holidays?.Select(NotableDay.FromNagerHoliday).ToList() ?? [];

        // Cache the result for 1 day (holiday data doesn't change frequently)
        await cache.SetAsync(cacheKey, days, TimeSpan.FromDays(1));

        return days;
    }
}
