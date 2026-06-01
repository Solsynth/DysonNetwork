using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Passport.Account;

public class NotableDaysService(AppDatabase db, ICacheService cache)
{
    private const string NotableDaysCacheKeyPrefix = "notable:";

    public async Task<List<NotableDay>> GetNotableDays(int? year, string regionCode, NotableDayTag? tag = null)
    {
        year ??= DateTime.UtcNow.Year;

        var cacheKey = $"{NotableDaysCacheKeyPrefix}:{year}:{regionCode}:{tag}";
        var (found, cachedDays) = await cache.GetAsyncWithStatus<List<NotableDay>>(cacheKey);
        if (found && cachedDays != null)
        {
            return cachedDays;
        }

        var startOfYear = Instant.FromDateTimeUtc(new DateTime(year.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var endOfYear = Instant.FromDateTimeUtc(new DateTime(year.Value + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var query = db.NotableDays
            .AsNoTracking()
            .Where(n => n.DeletedAt == null
                && n.Region == regionCode
                && n.StartDate < endOfYear
                && n.EndDate >= startOfYear);

        if (tag.HasValue)
        {
            query = query.Where(n => n.Tags.Contains(tag.Value));
        }

        var notableDays = await query
            .OrderBy(n => n.DisplayOrder ?? 999)
            .ThenBy(n => n.StartDate)
            .ToListAsync();

        var days = new List<NotableDay>();

        foreach (var notableDay in notableDays)
        {
            if (notableDay.IsPeriod && notableDay.IsRecurring)
            {
                // For recurring period holidays (like Labour Day), generate each day in the period
                var periodDays = GeneratePeriodDays(notableDay, year.Value);
                days.AddRange(periodDays);
            }
            else if (notableDay.IsRecurring)
            {
                // For recurring single-day events
                var recurringDay = GenerateRecurringDay(notableDay, year.Value);
                if (recurringDay != null)
                    days.Add(recurringDay);
            }
            else
            {
                // For non-recurring events, just add if in the year
                if (notableDay.StartDate.InUtc().Year == year.Value)
                {
                    days.Add(notableDay.ToNotableDay());
                }
            }
        }

        // Add global holidays
        var globalDays = GetGlobalHolidays(year.Value);
        foreach (var globalDay in globalDays)
        {
            if (!days.Any(d => d.Date.Equals(globalDay.Date) && d.GlobalName == globalDay.GlobalName))
            {
                days.Add(globalDay);
            }
        }

        await cache.SetAsync(cacheKey, days, TimeSpan.FromHours(12));
        return days;
    }

    private List<NotableDay> GeneratePeriodDays(SnNotableDay notableDay, int year)
    {
        var days = new List<NotableDay>();
        var startDate = notableDay.StartDate.InUtc();
        var endDate = notableDay.EndDate.InUtc();

        // Adjust year for recurring events
        var adjustedStart = new LocalDateTime(year, startDate.Month, startDate.Day, 0, 0, 0)
            .InZoneLeniently(DateTimeZone.Utc).ToInstant();
        var adjustedEnd = new LocalDateTime(year, endDate.Month, endDate.Day, 0, 0, 0)
            .InZoneLeniently(DateTimeZone.Utc).ToInstant();

        // If end is before start, it crosses year boundary
        if (adjustedEnd < adjustedStart)
        {
            adjustedEnd = new LocalDateTime(year + 1, endDate.Month, endDate.Day, 0, 0, 0)
                .InZoneLeniently(DateTimeZone.Utc).ToInstant();
        }

        var current = adjustedStart;
        while (current < adjustedEnd)
        {
            var isHolidayDay = notableDay.HolidayDays == null
                || notableDay.HolidayDays.Count == 0
                || notableDay.HolidayDays.Contains(current.InUtc().Date.ToString("MM-dd", null));

            days.Add(new NotableDay
            {
                Date = current,
                LocalName = notableDay.LocalName ?? notableDay.Name,
                GlobalName = notableDay.Name,
                LocalizableKey = notableDay.LocalizableKey,
                CountryCode = notableDay.Region,
                Holidays = isHolidayDay ? [NotableHolidayType.Public] : [],
            });

            current = current.Plus(Duration.FromDays(1));
        }

        return days;
    }

    private NotableDay? GenerateRecurringDay(SnNotableDay notableDay, int year)
    {
        var originalDate = notableDay.StartDate.InUtc();
        var adjustedDate = new LocalDateTime(year, originalDate.Month, originalDate.Day, 0, 0, 0)
            .InZoneLeniently(DateTimeZone.Utc).ToInstant();

        return new NotableDay
        {
            Date = adjustedDate,
            LocalName = notableDay.LocalName ?? notableDay.Name,
            GlobalName = notableDay.Name,
            LocalizableKey = notableDay.LocalizableKey,
            CountryCode = notableDay.Region,
            Holidays = notableDay.Tags.Contains(NotableDayTag.Holiday)
                ? [NotableHolidayType.Public]
                : [],
        };
    }

    private static List<NotableDay> GetGlobalHolidays(int year)
    {
        var globalDays = new List<NotableDay>();

        globalDays.Add(new NotableDay
        {
            Date = Instant.FromDateTimeUtc(new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            LocalName = "New Year's Day",
            GlobalName = "New Year's Day",
            LocalizableKey = "NewYear",
            CountryCode = null,
            Holidays = [NotableHolidayType.Public],
        });

        globalDays.Add(new NotableDay
        {
            Date = Instant.FromDateTimeUtc(new DateTime(year, 12, 25, 0, 0, 0, DateTimeKind.Utc)),
            LocalName = "Christmas",
            GlobalName = "Christmas",
            LocalizableKey = "Christmas",
            CountryCode = null,
            Holidays = [NotableHolidayType.Public],
        });

        var anniversaryNumber = year - 2024;
        globalDays.Add(new NotableDay
        {
            Date = Instant.FromDateTimeUtc(new DateTime(year, 3, 16, 0, 0, 0, DateTimeKind.Utc)),
            LocalName = $"Solar Network {anniversaryNumber} 周年",
            GlobalName = $"Solar Network {anniversaryNumber}th anniversary",
            LocalizableKey = "Anniversary",
            CountryCode = null,
            Holidays = [],
        });

        return globalDays;
    }

    public async Task<NotableDay?> GetNextHoliday(string regionCode)
    {
        var currentDate = SystemClock.Instance.GetCurrentInstant();
        var currentYear = currentDate.InUtc().Year;

        var currentYearHolidays = await GetNotableDays(currentYear, regionCode);
        var nextYearHolidays = await GetNotableDays(currentYear + 1, regionCode);

        var allHolidays = currentYearHolidays.Concat(nextYearHolidays);

        return allHolidays
            .Where(day => day.Date >= currentDate)
            .OrderBy(day => day.Date)
            .FirstOrDefault();
    }

    public async Task<NotableDay?> GetCurrentHoliday(string regionCode)
    {
        var currentDate = SystemClock.Instance.GetCurrentInstant();
        var currentYear = currentDate.InUtc().Year;

        var currentYearHolidays = await GetNotableDays(currentYear, regionCode);

        return currentYearHolidays.FirstOrDefault(day =>
            day.Date.InUtc().Date == currentDate.InUtc().Date
        );
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

    public async Task PurgeCache(string? regionCode = null, int? year = null)
    {
        if (regionCode != null && year != null)
        {
            var cacheKey = $"{NotableDaysCacheKeyPrefix}:{year}:{regionCode}";
            await cache.RemoveAsync(cacheKey);
        }
        else
        {
            // Purge all notable days cache
            var currentYear = SystemClock.Instance.GetCurrentInstant().InUtc().Year;
            for (var y = currentYear - 1; y <= currentYear + 2; y++)
            {
                await cache.RemoveAsync($"{NotableDaysCacheKeyPrefix}:{y}:CN");
                await cache.RemoveAsync($"{NotableDaysCacheKeyPrefix}:{y}:US");
            }
        }
    }
}
