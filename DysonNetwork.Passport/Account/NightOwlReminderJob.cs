using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Localization;
using DysonNetwork.Shared.Proto;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Quartz;

namespace DysonNetwork.Passport.Account;

public class NightOwlReminderJob(
    AppDatabase db,
    AccountService accounts,
    DyRingService.DyRingServiceClient pusher,
    ILocalizationService localizer,
    ICacheService cache,
    ILogger<NightOwlReminderJob> logger
) : IJob
{
    private static readonly Duration RecentActivityThreshold = Duration.FromMinutes(15);
    private static readonly TimeSpan ReminderCacheLifetime = TimeSpan.FromHours(26);
    private const int ReminderVariantCount = 3;

    public async Task Execute(IJobExecutionContext context)
    {
        var cancellationToken = context.CancellationToken;
        var now = SystemClock.Instance.GetCurrentInstant();
        var activeProfiles = await db.AccountProfiles
            .AsNoTracking()
            .Where(profile =>
                profile.LastSeenAt >= now - RecentActivityThreshold ||
                db.PresenceActivities.Any(activity =>
                    activity.AccountId == profile.AccountId &&
                    activity.DeletedAt == null &&
                    activity.LeaseExpiresAt > now
                )
            )
            .Select(profile => new { profile.AccountId, profile.TimeZone })
            .ToListAsync(cancellationToken);

        var reminded = 0;
        foreach (var profile in activeProfiles)
        {
            var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(profile.TimeZone ?? string.Empty);
            if (zone is null)
                continue;

            var localNow = now.InZone(zone);
            if (localNow.Hour >= 3)
                continue;

            var localDate = localNow.Date.ToString("yyyy-MM-dd", null);
            var reminderKey = $"accounts:night-owl-reminder:{profile.AccountId}:{localDate}:{localNow.Hour}";
            var result = await cache.ExecuteWithLockAsync(
                reminderKey,
                async () =>
                {
                    if (await cache.HasFlagAsync(reminderKey))
                        return false;

                    var account = await accounts.GetAccount(profile.AccountId);
                    if (account is null)
                        return false;

                    var variant = Random.Shared.Next(1, ReminderVariantCount + 1);
                    await pusher.SendPushNotificationToUserAsync(new DySendPushNotificationToUserRequest
                    {
                        UserId = profile.AccountId.ToString(),
                        Notification = new DyPushNotification
                        {
                            Topic = "accounts.wellbeing.sleep",
                            Title = localizer.Get($"nightOwlReminder{localNow.Hour}Title{variant}", account.Language),
                            Body = localizer.Get($"nightOwlReminder{localNow.Hour}Body{variant}", account.Language),
                            ActionUri = "/",
                            IsSavable = false
                        }
                    }, cancellationToken: cancellationToken);

                    await cache.SetFlagAsync(reminderKey, ReminderCacheLifetime);
                    return true;
                },
                TimeSpan.FromMinutes(1),
                waitTime: TimeSpan.Zero
            );

            if (result.Acquired && result.Result)
                reminded++;
        }

        if (reminded > 0)
            logger.LogInformation("Sent {Count} late-night sleep reminders.", reminded);
    }
}
