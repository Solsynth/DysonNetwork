using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Passport.Account;

public class NotableDaysSeedService(
    AppDatabase db,
    NotableDaysService notableDaysService,
    ILogger<NotableDaysSeedService> logger
)
{
    public async Task EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        var seedData = ChineseHolidaysSeed.GetChineseHolidays();
        if (seedData.Count == 0) return;

        var existing = await db.NotableDays
            .Where(n => n.DeletedAt == null && n.Region == "CN")
            .ToListAsync(cancellationToken);

        var existingByName = existing.ToDictionary(n => n.Name, StringComparer.Ordinal);

        var added = 0;
        var updated = 0;
        foreach (var day in seedData)
        {
            if (existingByName.TryGetValue(day.Name, out var dbDay))
            {
                if (ApplySeedData(dbDay, day))
                    updated++;
            }
            else
            {
                db.NotableDays.Add(day);
                added++;
            }
        }

        if (added > 0 || updated > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            await notableDaysService.PurgeCache("CN");
            logger.LogInformation(
                "Seeded Chinese notable days: {Added} added, {Updated} updated.",
                added,
                updated
            );
        }
    }

    /// <summary>
    /// Copies seed fields onto an existing row. Returns true when any tracked
    /// property changed so callers only save/purge when needed.
    /// </summary>
    private static bool ApplySeedData(SnNotableDay target, SnNotableDay source)
    {
        var changed = false;

        changed |= target.Description != source.Description;
        target.Description = source.Description;

        changed |= target.LocalName != source.LocalName;
        target.LocalName = source.LocalName;

        changed |= target.LocalizableKey != source.LocalizableKey;
        target.LocalizableKey = source.LocalizableKey;

        changed |= target.StartDate != source.StartDate;
        target.StartDate = source.StartDate;

        changed |= target.EndDate != source.EndDate;
        target.EndDate = source.EndDate;

        changed |= !target.Tags.SequenceEqual(source.Tags);
        target.Tags = source.Tags;

        changed |= !MetaEqual(target.Meta, source.Meta);
        target.Meta = source.Meta;

        changed |= target.IsRecurring != source.IsRecurring;
        target.IsRecurring = source.IsRecurring;

        changed |= target.RecurrencePattern != source.RecurrencePattern;
        target.RecurrencePattern = source.RecurrencePattern;

        changed |= target.IsPeriod != source.IsPeriod;
        target.IsPeriod = source.IsPeriod;

        changed |= !HolidayDaysEqual(target.HolidayDays, source.HolidayDays);
        target.HolidayDays = source.HolidayDays;

        changed |= target.DisplayOrder != source.DisplayOrder;
        target.DisplayOrder = source.DisplayOrder;

        return changed;
    }

    private static bool MetaEqual(Dictionary<string, object>? a, Dictionary<string, object>? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null || a.Count != b.Count) return false;
        foreach (var (key, value) in a)
        {
            if (!b.TryGetValue(key, out var other) || !value.Equals(other))
                return false;
        }
        return true;
    }

    private static bool HolidayDaysEqual(List<string>? a, List<string>? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null || a.Count != b.Count) return false;
        return a.SequenceEqual(b);
    }
}
