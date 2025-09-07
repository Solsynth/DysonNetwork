using DysonNetwork.Shared.Cache;
using Nager.Holiday;
using NodaTime;

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

    public async Task<NotableDay?> GetNextHoliday(string regionCode)
    {
        var currentDate = SystemClock.Instance.GetCurrentInstant();
        var currentYear = currentDate.InUtc().Year;

        // Get holidays for current year and next year to cover all possibilities
        var currentYearHolidays = await GetNotableDays(currentYear, regionCode);
        var nextYearHolidays = await GetNotableDays(currentYear + 1, regionCode);

        var allHolidays = currentYearHolidays.Concat(nextYearHolidays);

        // Find the first holiday that is today or in the future
        var nextHoliday = allHolidays
            .Where(day => day.Date >= currentDate)
            .OrderBy(day => day.Date)
            .FirstOrDefault();

        return nextHoliday;
    }
}
