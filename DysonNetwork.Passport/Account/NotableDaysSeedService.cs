using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Passport.Account;

public class NotableDaysSeedService(
    AppDatabase db,
    ILogger<NotableDaysSeedService> logger
)
{
    public async Task EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        var seedData = ChineseHolidaysSeed.GetChineseHolidays();
        if (seedData.Count == 0) return;

        var existingNames = await db.NotableDays
            .Where(n => n.DeletedAt == null && n.Region == "CN")
            .Select(n => n.Name)
            .ToListAsync(cancellationToken);

        var existingNameSet = existingNames.ToHashSet(StringComparer.Ordinal);

        var added = 0;
        foreach (var day in seedData)
        {
            if (existingNameSet.Contains(day.Name))
                continue;

            db.NotableDays.Add(day);
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded {Count} Chinese notable days.", added);
        }
    }
}
