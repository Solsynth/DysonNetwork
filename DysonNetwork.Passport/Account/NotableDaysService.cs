using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using Nager.Holiday;
using NodaTime;

namespace DysonNetwork.Passport.Account;

public class InvalidRegionCodeException : Exception
{
    public InvalidRegionCodeException(string regionCode) : base($"Invalid or unknown region code: {regionCode}") { }
}

public static class NotableDayExtensions
{
    public static DysonNetwork.Shared.Models.NotableDay FromNagerHoliday(PublicHoliday holiday)
    {
        return new DysonNetwork.Shared.Models.NotableDay()
        {
            Date = Instant.FromDateTimeUtc(holiday.Date.ToUniversalTime()),
            LocalName = holiday.LocalName,
            GlobalName = holiday.Name,
            CountryCode = holiday.CountryCode,
            Holidays = holiday.Types?.Select(x => x switch
            {
                PublicHolidayType.Public => DysonNetwork.Shared.Models.NotableHolidayType.Public,
                PublicHolidayType.Bank => DysonNetwork.Shared.Models.NotableHolidayType.Bank,
                PublicHolidayType.School => DysonNetwork.Shared.Models.NotableHolidayType.School,
                PublicHolidayType.Authorities => DysonNetwork.Shared.Models.NotableHolidayType.Authorities,
                PublicHolidayType.Optional => DysonNetwork.Shared.Models.NotableHolidayType.Optional,
                _ => DysonNetwork.Shared.Models.NotableHolidayType.Observance
            }).ToArray() ?? [],
        };
    }
}

public class NotableDaysService(ICacheService cache)
{
    private const string NotableDaysCacheKeyPrefix = "notable:";

    public async Task<List<DysonNetwork.Shared.Models.NotableDay>> GetNotableDays(int? year, string regionCode)
    {
        year ??= DateTime.UtcNow.Year;

        // Generate cache key using year and region code
        var cacheKey = $"{NotableDaysCacheKeyPrefix}:{year}:{regionCode}";

        // Try to get from cache first
        var (found, cachedDays) = await cache.GetAsyncWithStatus<List<DysonNetwork.Shared.Models.NotableDay>>(cacheKey);
        if (found && cachedDays != null)
        {
            return cachedDays;
        }

        // If not in cache, fetch from API
        List<DysonNetwork.Shared.Models.NotableDay> days = [];
        try
        {
            using var holidayClient = new HolidayClient();
            var holidays = await holidayClient.GetHolidaysAsync(year.Value, regionCode);
            days = holidays?.Select(NotableDayExtensions.FromNagerHoliday).ToList() ?? [];
        }
        catch (HolidayClientException)
        {
            // Invalid or unknown region code - just use global holidays
            days = [];
        }

        // Add global holidays that are available for all regions
        var globalDays = GetGlobalHolidays(year.Value);
        foreach (
            var globalDay in globalDays.Where(globalDay =>
                !days.Any(d =>
                    d.Date.Equals(globalDay.Date) && d.GlobalName == globalDay.GlobalName
                )
            )
        )
        {
            days.Add(globalDay);
        }

        // Cache the result for 1 day (holiday data doesn't change frequently)
        await cache.SetAsync(cacheKey, days, TimeSpan.FromDays(1));

        return days;
    }

    private static List<DysonNetwork.Shared.Models.NotableDay> GetGlobalHolidays(int year)
    {
        var globalDays = new List<DysonNetwork.Shared.Models.NotableDay>();

        // Christmas Day - December 25
        globalDays.Add(new DysonNetwork.Shared.Models.NotableDay
        {
            Date = Instant.FromDateTimeUtc(new DateTime(year, 12, 25, 0, 0, 0, DateTimeKind.Utc)),
            LocalName = "Christmas",
            GlobalName = "Christmas",
            LocalizableKey = "Christmas",
            CountryCode = null,
            Holidays = [DysonNetwork.Shared.Models.NotableHolidayType.Public],
        });

        // New Year's Day - January 1
        globalDays.Add(new DysonNetwork.Shared.Models.NotableDay
        {
            Date = Instant.FromDateTimeUtc(new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            LocalName = "New Year's Day",
            GlobalName = "New Year's Day",
            LocalizableKey = "NewYear",
            CountryCode = null,
            Holidays = [DysonNetwork.Shared.Models.NotableHolidayType.Public],
        });

        // April Fools' Day - April 1
        globalDays.Add(new DysonNetwork.Shared.Models.NotableDay
        {
            Date = Instant.FromDateTimeUtc(new DateTime(year, 4, 1, 0, 0, 0, DateTimeKind.Utc)),
            LocalName = "April Fools' Day",
            GlobalName = "April Fools' Day",
            LocalizableKey = "AprilFoolsDay",
            CountryCode = null,
            Holidays = [DysonNetwork.Shared.Models.NotableHolidayType.Observance],
        });

        // International Workers' Day - May 1
        globalDays.Add(new DysonNetwork.Shared.Models.NotableDay
        {
            Date = Instant.FromDateTimeUtc(new DateTime(year, 5, 1, 0, 0, 0, DateTimeKind.Utc)),
            LocalName = "International Workers' Day",
            GlobalName = "International Workers' Day",
            LocalizableKey = "WorkersDay",
            CountryCode = null,
            Holidays = [DysonNetwork.Shared.Models.NotableHolidayType.Public],
        });

        // Children's Day - June 1
        globalDays.Add(new DysonNetwork.Shared.Models.NotableDay
        {
            Date = Instant.FromDateTimeUtc(new DateTime(year, 6, 1, 0, 0, 0, DateTimeKind.Utc)),
            LocalName = "Children's Day",
            GlobalName = "Children's Day",
            LocalizableKey = "ChildrenDay",
            CountryCode = null,
            Holidays = [DysonNetwork.Shared.Models.NotableHolidayType.Public],
        });

        // World Environment Day - June 5
        globalDays.Add(new DysonNetwork.Shared.Models.NotableDay
        {
            Date = Instant.FromDateTimeUtc(new DateTime(year, 6, 5, 0, 0, 0, DateTimeKind.Utc)),
            LocalName = "World Environment Day",
            GlobalName = "World Environment Day",
            LocalizableKey = "EnvironmentDay",
            CountryCode = null,
            Holidays = [DysonNetwork.Shared.Models.NotableHolidayType.Observance],
        });

        // Halloween - October 31
        globalDays.Add(new DysonNetwork.Shared.Models.NotableDay
        {
            Date = Instant.FromDateTimeUtc(new DateTime(year, 10, 31, 0, 0, 0, DateTimeKind.Utc)),
            LocalName = "Halloween",
            GlobalName = "Halloween",
            LocalizableKey = "Halloween",
            CountryCode = null,
            Holidays = [DysonNetwork.Shared.Models.NotableHolidayType.Observance],
        });

        var anniversaryNumber = year - 2024;
        var anniversaryNumberSuffixes = new[] { "st", "nd", "rd" };
        var anniversaryNumberSuffix = anniversaryNumberSuffixes.ElementAtOrDefault(anniversaryNumber % 10 + 1);
        globalDays.Add(new DysonNetwork.Shared.Models.NotableDay
        {
            Date = Instant.FromDateTimeUtc(new DateTime(year, 3, 16, 0, 0, 0, DateTimeKind.Utc)),
            LocalName = $"Solar Network {anniversaryNumber} 周年",
            GlobalName = $"Solar Network {anniversaryNumber}{anniversaryNumberSuffix ?? "th"} anniversary",
            LocalizableKey = "Anniversary",
            CountryCode = null,
            Holidays = [],
        });

        return globalDays;
    }

    public async Task<DysonNetwork.Shared.Models.NotableDay?> GetNextHoliday(string regionCode)
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

    public async Task<DysonNetwork.Shared.Models.NotableDay?> GetCurrentHoliday(string regionCode)
    {
        var currentDate = SystemClock.Instance.GetCurrentInstant();
        var currentYear = currentDate.InUtc().Year;

        var currentYearHolidays = await GetNotableDays(currentYear, regionCode);

        // Find the holiday that is today
        var todayHoliday = currentYearHolidays.FirstOrDefault(day =>
            day.Date.InUtc().Date == currentDate.InUtc().Date
        );

        return todayHoliday;
    }

    public async Task<List<DysonNetwork.Shared.Models.NotableDay>> GetCurrentAndNextHoliday(string regionCode)
    {
        var result = new List<DysonNetwork.Shared.Models.NotableDay>();

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
