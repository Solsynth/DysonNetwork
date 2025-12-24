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

        // Add global holidays that are available for all regions
        var globalDays = GetGlobalHolidays(year.Value);
        foreach (var globalDay in globalDays.Where(globalDay =>
                     !days.Any(d => d.Date.Equals(globalDay.Date) && d.GlobalName == globalDay.GlobalName)))
        {
            days.Add(globalDay);
        }

        // Cache the result for 1 day (holiday data doesn't change frequently)
        await cache.SetAsync(cacheKey, days, TimeSpan.FromDays(1));

        return days;
    }

    private static List<NotableDay> GetGlobalHolidays(int year)
    {
        var globalDays = new List<NotableDay>();

        // Christmas Day - December 25
        var christmas = new NotableDay
        {
            Date = Instant.FromDateTimeUtc(new DateTime(year, 12, 25, 0, 0, 0, DateTimeKind.Utc)),
            LocalName = "Christmas",
            GlobalName = "Christmas",
            LocalizableKey = "Christmas",
            CountryCode = null,
            Holidays = [NotableHolidayType.Public]
        };
        globalDays.Add(christmas);

        // New Year's Day - January 1
        var newYear = new NotableDay
        {
            Date = Instant.FromDateTimeUtc(new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            LocalName = "New Year's Day",
            GlobalName = "New Year's Day",
            LocalizableKey = "NewYear",
            CountryCode = null,
            Holidays = [NotableHolidayType.Public]
        };
        globalDays.Add(newYear);

        // Valentine's Day - February 14
        var valentine = new NotableDay
        {
            Date = Instant.FromDateTimeUtc(new DateTime(year, 2, 14, 0, 0, 0, DateTimeKind.Utc)),
            LocalName = "Valentine's Day",
            GlobalName = "Valentine's Day",
            LocalizableKey = "ValentineDay",
            CountryCode = null,
            Holidays = [NotableHolidayType.Observance]
        };
        globalDays.Add(valentine);

        // April Fools' Day - April 1
        var aprilFools = new NotableDay
        {
            Date = Instant.FromDateTimeUtc(new DateTime(year, 4, 1, 0, 0, 0, DateTimeKind.Utc)),
            LocalName = "April Fools' Day",
            GlobalName = "April Fools' Day",
            LocalizableKey = "AprilFoolsDay",
            CountryCode = null,
            Holidays = [NotableHolidayType.Observance]
        };
        globalDays.Add(aprilFools);

        // International Workers' Day - May 1
        var workersDay = new NotableDay
        {
            Date = Instant.FromDateTimeUtc(new DateTime(year, 5, 1, 0, 0, 0, DateTimeKind.Utc)),
            LocalName = "International Workers' Day",
            GlobalName = "International Workers' Day",
            LocalizableKey = "WorkersDay",
            CountryCode = null,
            Holidays = [NotableHolidayType.Public]
        };
        globalDays.Add(workersDay);

        // Children's Day - June 1
        var childrenDay = new NotableDay
        {
            Date = Instant.FromDateTimeUtc(new DateTime(year, 6, 1, 0, 0, 0, DateTimeKind.Utc)),
            LocalName = "Children's Day",
            GlobalName = "Children's Day",
            LocalizableKey = "ChildrenDay",
            CountryCode = null,
            Holidays = [NotableHolidayType.Public]
        };
        globalDays.Add(childrenDay);

        // World Environment Day - June 5
        var environmentDay = new NotableDay
        {
            Date = Instant.FromDateTimeUtc(new DateTime(year, 6, 5, 0, 0, 0, DateTimeKind.Utc)),
            LocalName = "World Environment Day",
            GlobalName = "World Environment Day",
            LocalizableKey = "EnvironmentDay",
            CountryCode = null,
            Holidays = [NotableHolidayType.Observance]
        };
        globalDays.Add(environmentDay);

        // Halloween - October 31
        var halloween = new NotableDay
        {
            Date = Instant.FromDateTimeUtc(new DateTime(year, 10, 31, 0, 0, 0, DateTimeKind.Utc)),
            LocalName = "Halloween",
            GlobalName = "Halloween",
            LocalizableKey = "Halloween",
            CountryCode = null,
            Holidays = [NotableHolidayType.Observance]
        };
        globalDays.Add(halloween);

        return globalDays;
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

    public async Task<NotableDay?> GetCurrentHoliday(string regionCode)
    {
        var currentDate = SystemClock.Instance.GetCurrentInstant();
        var currentYear = currentDate.InUtc().Year;

        var currentYearHolidays = await GetNotableDays(currentYear, regionCode);

        // Find the holiday that is today
        var todayHoliday = currentYearHolidays
            .FirstOrDefault(day => day.Date.InUtc().Date == currentDate.InUtc().Date);

        return todayHoliday;
    }

    public async Task<List<NotableDay>> GetCurrentAndNextHoliday(string regionCode)
    {
        var result = new List<NotableDay>();

        var current = await GetCurrentHoliday(regionCode);
        if (current != null)
        {
            result.Add(current);
        }

        var next = await GetNextHoliday(regionCode);
        if (next != null && (current == null || !next.Date.Equals(current.Date)))
        {
            result.Add(next);
        }

        return result;
    }
}
