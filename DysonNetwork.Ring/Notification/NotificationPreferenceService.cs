using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Ring.Notification;

public class NotificationPreferenceService(AppDatabase db)
{
    public async Task<List<SnNotificationPreference>> GetPreferencesAsync(Guid accountId)
    {
        return await db.NotificationPreferences
            .Where(p => p.AccountId == accountId)
            .OrderBy(p => p.Topic)
            .ToListAsync();
    }

    public async Task<NotificationPreferenceLevel> GetPreferenceAsync(Guid accountId, string topic)
    {
        var preference = await db.NotificationPreferences
            .Where(p => p.AccountId == accountId && p.Topic == topic)
            .FirstOrDefaultAsync();

        return preference?.Preference ?? NotificationPreferenceLevel.Normal;
    }

    public async Task SetPreferenceAsync(Guid accountId, string topic, NotificationPreferenceLevel level)
    {
        var existing = await db.NotificationPreferences
            .Where(p => p.AccountId == accountId && p.Topic == topic)
            .FirstOrDefaultAsync();

        var now = SystemClock.Instance.GetCurrentInstant();

        if (existing != null)
        {
            existing.Preference = level;
            existing.UpdatedAt = now;
        }
        else
        {
            db.NotificationPreferences.Add(new SnNotificationPreference
            {
                AccountId = accountId,
                Topic = topic,
                Preference = level,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await db.SaveChangesAsync();
    }

    public async Task DeletePreferenceAsync(Guid accountId, string topic)
    {
        var existing = await db.NotificationPreferences
            .Where(p => p.AccountId == accountId && p.Topic == topic)
            .FirstOrDefaultAsync();

        if (existing != null)
        {
            existing.DeletedAt = SystemClock.Instance.GetCurrentInstant();
            await db.SaveChangesAsync();
        }
    }
}