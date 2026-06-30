using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Text;

namespace DysonNetwork.Passport.Account;

public class NotableDaysService(AppDatabase db, ICacheService cache)
{
    private const string NotableDaysCacheKeyPrefix = "notable:";

    public async Task<List<NotableDay>> GetNotableDays(int? year, string regionCode, NotableDayTag? tag = null)
    {
        year ??= DateTime.UtcNow.Year;
        regionCode = NormalizeRegionCode(regionCode);

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
                days.AddRange(GeneratePeriodDays(notableDay, year.Value));
            }
            else if (notableDay.IsRecurring)
            {
                var recurringDay = GenerateRecurringDay(notableDay, year.Value);
                if (recurringDay != null)
                    days.Add(recurringDay);
            }
            else if (notableDay.StartDate.InUtc().Year == year.Value)
            {
                days.Add(AttachOccurrenceKey(notableDay.ToNotableDay(), regionCode));
            }
        }

        var globalDays = GetGlobalHolidays(year.Value, regionCode);
        foreach (var globalDay in globalDays)
        {
            if (tag.HasValue && !globalDay.Tags.Contains(tag.Value))
                continue;

            if (!days.Any(d => d.Date.Equals(globalDay.Date) && d.GlobalName == globalDay.GlobalName))
            {
                days.Add(globalDay);
            }
        }

        days = days
            .OrderBy(d => d.Date)
            .ThenBy(d => d.GlobalName)
            .ToList();

        await cache.SetAsync(cacheKey, days, TimeSpan.FromHours(12));
        return days;
    }

    public async Task<NotableDay?> GetGeneratedNotableDayAsync(string occurrenceKey)
    {
        if (!TryParseOccurrenceKey(occurrenceKey, out var regionCode, out var date, out _))
            return null;

        var notableDays = await GetNotableDays(date.Year, regionCode);
        return notableDays.FirstOrDefault(day => string.Equals(day.OccurrenceKey, occurrenceKey, StringComparison.Ordinal));
    }

    public async Task<(List<NotableDay> Results, int TotalCount)> SearchNotableDaysAsync(
        string regionCode,
        Instant? startTime = null,
        Instant? endTime = null,
        string? query = null,
        NotableDayTag? tag = null,
        int offset = 0,
        int take = 50)
    {
        regionCode = NormalizeRegionCode(regionCode);

        var years = GetSearchYears(startTime, endTime);
        var allDays = new List<NotableDay>();
        foreach (var year in years)
        {
            allDays.AddRange(await GetNotableDays(year, regionCode, tag));
        }

        var filtered = allDays
            .Where(day => !startTime.HasValue || day.Date >= startTime.Value)
            .Where(day => !endTime.HasValue || day.Date <= endTime.Value)
            .Where(day => string.IsNullOrWhiteSpace(query) || MatchesQuery(day, query))
            .OrderBy(day => day.Date)
            .ThenBy(day => day.GlobalName)
            .ToList();

        var totalCount = filtered.Count;
        return (filtered.Skip(offset).Take(take).ToList(), totalCount);
    }

    public static string BuildOccurrenceKey(string regionCode, Instant date, string sourceIdentity)
    {
        return $"{NormalizeRegionCode(regionCode)}|{date.InUtc().Date:yyyy-MM-dd}|{NormalizeSourceIdentity(sourceIdentity)}";
    }

    private List<NotableDay> GeneratePeriodDays(SnNotableDay notableDay, int year)
    {
        var days = new List<NotableDay>();
        var startDate = notableDay.StartDate.InUtc();
        var endDate = notableDay.EndDate.InUtc();

        var adjustedStart = new LocalDateTime(year, startDate.Month, startDate.Day, 0, 0, 0)
            .InZoneLeniently(DateTimeZone.Utc).ToInstant();
        var adjustedEnd = new LocalDateTime(year, endDate.Month, endDate.Day, 0, 0, 0)
            .InZoneLeniently(DateTimeZone.Utc).ToInstant();

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

            days.Add(AttachOccurrenceKey(new NotableDay
            {
                Date = current,
                LocalName = notableDay.LocalName ?? notableDay.Name,
                GlobalName = notableDay.Name,
                LocalizableKey = notableDay.LocalizableKey,
                CountryCode = notableDay.Region,
                Description = notableDay.Description,
                Meta = notableDay.Meta,
                Holidays = isHolidayDay ? [NotableHolidayType.Public] : [],
                Tags = notableDay.Tags,
            }, notableDay.Region));

            current = current.Plus(Duration.FromDays(1));
        }

        return days;
    }

    private NotableDay? GenerateRecurringDay(SnNotableDay notableDay, int year)
    {
        var originalDate = notableDay.StartDate.InUtc();
        var adjustedDate = new LocalDateTime(year, originalDate.Month, originalDate.Day, 0, 0, 0)
            .InZoneLeniently(DateTimeZone.Utc).ToInstant();

        return AttachOccurrenceKey(new NotableDay
        {
            Date = adjustedDate,
            LocalName = notableDay.LocalName ?? notableDay.Name,
            GlobalName = notableDay.Name,
            LocalizableKey = notableDay.LocalizableKey,
            CountryCode = notableDay.Region,
            Description = notableDay.Description,
            Meta = notableDay.Meta,
            Holidays = notableDay.Tags.Contains(NotableDayTag.Holiday)
                ? [NotableHolidayType.Public]
                : [],
            Tags = notableDay.Tags,
        }, notableDay.Region);
    }

    private static List<NotableDay> GetGlobalHolidays(int year, string regionCode)
    {
        var globalDays = new List<NotableDay>();

        globalDays.Add(AttachOccurrenceKey(new NotableDay
        {
            Date = Instant.FromDateTimeUtc(new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            LocalName = "New Year's Day",
            GlobalName = "New Year's Day",
            LocalizableKey = "NewYear",
            CountryCode = null,
            Holidays = [NotableHolidayType.Public],
            Tags = [NotableDayTag.Holiday],
        }, regionCode));

        globalDays.Add(AttachOccurrenceKey(new NotableDay
        {
            Date = Instant.FromDateTimeUtc(new DateTime(year, 12, 25, 0, 0, 0, DateTimeKind.Utc)),
            LocalName = "Christmas",
            GlobalName = "Christmas",
            LocalizableKey = "Christmas",
            CountryCode = null,
            Holidays = [NotableHolidayType.Public],
            Tags = [NotableDayTag.Holiday],
        }, regionCode));

        var anniversaryNumber = year - 2024;
        globalDays.Add(AttachOccurrenceKey(new NotableDay
        {
            Date = Instant.FromDateTimeUtc(new DateTime(year, 3, 16, 0, 0, 0, DateTimeKind.Utc)),
            LocalName = $"Solar Network {anniversaryNumber} 周年",
            GlobalName = $"Solar Network {anniversaryNumber}th anniversary",
            LocalizableKey = "Anniversary",
            CountryCode = null,
            Holidays = [],
            Tags = [NotableDayTag.Anniversary],
        }, regionCode));

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
            var normalizedRegionCode = NormalizeRegionCode(regionCode);
            await cache.RemoveAsync($"{NotableDaysCacheKeyPrefix}:{year}:{normalizedRegionCode}:");
            foreach (var tag in Enum.GetValues<NotableDayTag>())
            {
                await cache.RemoveAsync($"{NotableDaysCacheKeyPrefix}:{year}:{normalizedRegionCode}:{tag}");
            }
        }
        else
        {
            var currentYear = SystemClock.Instance.GetCurrentInstant().InUtc().Year;
            for (var y = currentYear - 1; y <= currentYear + 2; y++)
            {
                await PurgeCache("cn", y);
                await PurgeCache("us", y);
            }
        }
    }

    private static NotableDay AttachOccurrenceKey(NotableDay day, string regionCode)
    {
        day.OccurrenceKey = BuildOccurrenceKey(regionCode, day.Date, GetSourceIdentity(day));
        return day;
    }

    private static string GetSourceIdentity(NotableDay day)
    {
        return !string.IsNullOrWhiteSpace(day.LocalizableKey)
            ? day.LocalizableKey
            : !string.IsNullOrWhiteSpace(day.GlobalName)
                ? day.GlobalName
                : day.LocalName ?? day.Date.InUtc().Date.ToString();
    }

    private static bool MatchesQuery(NotableDay day, string query)
    {
        var normalizedQuery = query.Trim();
        return (!string.IsNullOrWhiteSpace(day.LocalName) && day.LocalName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(day.GlobalName) && day.GlobalName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(day.LocalizableKey) && day.LocalizableKey.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(day.Description) && day.Description.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase));
    }

    private static List<int> GetSearchYears(Instant? startTime, Instant? endTime)
    {
        if (startTime.HasValue || endTime.HasValue)
        {
            var effectiveStart = (startTime ?? endTime!.Value).InUtc().Year;
            var effectiveEnd = (endTime ?? startTime!.Value).InUtc().Year;
            if (effectiveEnd < effectiveStart)
                (effectiveStart, effectiveEnd) = (effectiveEnd, effectiveStart);
            return Enumerable.Range(effectiveStart, effectiveEnd - effectiveStart + 1).ToList();
        }

        var currentYear = SystemClock.Instance.GetCurrentInstant().InUtc().Year;
        return [currentYear, currentYear + 1];
    }

    private static bool TryParseOccurrenceKey(string occurrenceKey, out string regionCode, out LocalDate date, out string sourceIdentity)
    {
        regionCode = string.Empty;
        date = default;
        sourceIdentity = string.Empty;

        var parts = occurrenceKey.Split('|', 3, StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
            return false;

        var parsedDate = LocalDatePattern.Iso.Parse(parts[1]);
        if (!parsedDate.Success)
            return false;

        regionCode = NormalizeRegionCode(parts[0]);
        date = parsedDate.Value;
        sourceIdentity = parts[2];
        return true;
    }

    private static string NormalizeRegionCode(string? regionCode)
    {
        return string.IsNullOrWhiteSpace(regionCode) ? "us" : regionCode.Trim().ToLowerInvariant();
    }

    private static string NormalizeSourceIdentity(string sourceIdentity)
    {
        return sourceIdentity.Trim().ToLowerInvariant().Replace("|", "-");
    }
}
