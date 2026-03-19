using System.Globalization;
using System.Text;
using System.Text.Json;
using DysonNetwork.Passport.Account;
using DysonNetwork.Passport.Leveling;
using DysonNetwork.Shared.EventBus;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Queue;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Calendars;

namespace DysonNetwork.Passport.Progression;

public class ProgressionService(
    AppDatabase db,
    AccountService accounts,
    ExperienceService experience,
    RemotePaymentService payments,
    RemoteRingService ring,
    ProgressionSeedService seedService,
    ILogger<ProgressionService> logger
)
{
    public async Task HandleActionLogAsync(ActionLogTriggeredEvent evt, CancellationToken cancellationToken = default)
    {
        var achievements = await db.AchievementDefinitions
            .Where(m => m.IsEnabled && m.IsProgressEnabled)
            .OrderBy(m => m.SortOrder)
            .ToListAsync(cancellationToken);

        foreach (var definition in achievements.Where(m => IsAvailableAt(m, evt.OccurredAt) && Matches(evt, m.Trigger)))
        {
            var grant = await ProcessAchievementAsync(evt, definition, cancellationToken);
            if (grant is not null)
                await DeliverGrantAsync(grant, cancellationToken);
        }

        var zone = await ResolveAccountZoneAsync(evt.AccountId, cancellationToken);
        var quests = await db.QuestDefinitions
            .Where(m => m.IsEnabled && m.IsProgressEnabled)
            .OrderBy(m => m.SortOrder)
            .ToListAsync(cancellationToken);

        foreach (var definition in quests.Where(m => IsAvailableAt(m, evt.OccurredAt) && Matches(evt, m.Trigger)))
        {
            var grant = await ProcessQuestAsync(evt, definition, zone, cancellationToken);
            if (grant is not null)
                await DeliverGrantAsync(grant, cancellationToken);
        }
    }

    public async Task<List<ProgressionAchievementState>> ListAchievementStatesAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var definitions = await db.AchievementDefinitions
            .Where(m => m.IsEnabled)
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Title)
            .ToListAsync(cancellationToken);
        var progressMap = await db.AccountAchievements
            .Where(m => m.AccountId == accountId)
            .ToDictionaryAsync(m => m.AchievementDefinitionId, cancellationToken);

        return definitions.Select(definition =>
            {
                progressMap.TryGetValue(definition.Id, out var progress);
                return new ProgressionAchievementState
                {
                    Identifier = definition.Identifier,
                    Title = definition.Title,
                    Summary = definition.Summary,
                    Icon = definition.Icon,
                    SortOrder = definition.SortOrder,
                    Hidden = definition.Hidden,
                    IsEnabled = definition.IsEnabled,
                    IsProgressEnabled = definition.IsProgressEnabled,
                    IsCurrentlyAvailable = definition.IsProgressEnabled && IsAvailableAt(definition, now),
                    AvailableFrom = definition.AvailableFrom,
                    AvailableUntil = definition.AvailableUntil,
                    TargetCount = definition.TargetCount,
                    ProgressCount = progress?.ProgressCount ?? 0,
                    IsCompleted = progress?.CompletedAt is not null,
                    CompletedAt = progress?.CompletedAt,
                    Reward = definition.Reward
                };
            })
            .OrderBy(state => state.IsCompleted)
            .ThenBy(state => state.SortOrder)
            .ThenBy(state => state.Title)
            .ToList();
    }

    public async Task<List<ProgressionQuestState>> ListQuestStatesAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var definitions = await db.QuestDefinitions
            .Where(m => m.IsEnabled)
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Title)
            .ToListAsync(cancellationToken);
        var zone = await ResolveAccountZoneAsync(accountId, cancellationToken);

        var states = new List<ProgressionQuestState>();
        foreach (var definition in definitions)
        {
            var (periodKey, nextResetAt) = GetQuestPeriod(definition.Schedule, SystemClock.Instance.GetCurrentInstant(), zone);
            var progress = await db.AccountQuestProgresses
                .FirstOrDefaultAsync(
                    m => m.AccountId == accountId && m.QuestDefinitionId == definition.Id && m.PeriodKey == periodKey,
                    cancellationToken
                );

            states.Add(new ProgressionQuestState
            {
                Identifier = definition.Identifier,
                Title = definition.Title,
                Summary = definition.Summary,
                Icon = definition.Icon,
                SortOrder = definition.SortOrder,
                Hidden = definition.Hidden,
                IsEnabled = definition.IsEnabled,
                IsProgressEnabled = definition.IsProgressEnabled,
                IsCurrentlyAvailable = definition.IsProgressEnabled && IsAvailableAt(definition, now),
                AvailableFrom = definition.AvailableFrom,
                AvailableUntil = definition.AvailableUntil,
                TargetCount = definition.TargetCount,
                ProgressCount = progress?.ProgressCount ?? 0,
                IsCompleted = progress?.CompletedAt is not null,
                CompletedAt = progress?.CompletedAt,
                PeriodKey = periodKey,
                NextResetAt = nextResetAt,
                Schedule = definition.Schedule,
                Reward = definition.Reward
            });
        }

        return states
            .OrderBy(state => state.IsCompleted)
            .ThenBy(state => state.SortOrder)
            .ThenBy(state => state.Title)
            .ToList();
    }

    public Task<List<SnProgressRewardGrant>> ListRewardGrantsAsync(Guid accountId, int take = 50, CancellationToken cancellationToken = default)
    {
        return db.ProgressRewardGrants
            .Where(m => m.AccountId == accountId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(cancellationToken);
    }

    private async Task<SnProgressRewardGrant?> ProcessAchievementAsync(
        ActionLogTriggeredEvent evt,
        SnAchievementDefinition definition,
        CancellationToken cancellationToken
    )
    {
        var rewardToken = $"{ProgressionDefinitionType.Achievement}:{definition.Identifier}:{evt.AccountId}";
        var existingGrant = await db.ProgressRewardGrants
            .FirstOrDefaultAsync(m => m.RewardToken == rewardToken, cancellationToken);
        if (existingGrant is not null)
            return existingGrant;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var existingReceipt = await db.ProgressEventReceipts.AnyAsync(
            m => m.EventId == evt.EventId &&
                 m.DefinitionType == ProgressionDefinitionType.Achievement &&
                 m.DefinitionIdentifier == definition.Identifier &&
                 m.PeriodKey == string.Empty,
            cancellationToken
        );
        if (existingReceipt)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await db.ProgressRewardGrants.FirstOrDefaultAsync(m => m.RewardToken == rewardToken, cancellationToken);
        }

        db.ProgressEventReceipts.Add(new SnProgressEventReceipt
        {
            EventId = evt.EventId,
            AccountId = evt.AccountId,
            DefinitionType = ProgressionDefinitionType.Achievement,
            DefinitionIdentifier = definition.Identifier,
            PeriodKey = string.Empty
        });

        var progress = await db.AccountAchievements
            .FirstOrDefaultAsync(m => m.AccountId == evt.AccountId && m.AchievementDefinitionId == definition.Id, cancellationToken);
        if (progress is null)
        {
            progress = new SnAccountAchievement
            {
                AccountId = evt.AccountId,
                AchievementDefinitionId = definition.Id
            };
            db.AccountAchievements.Add(progress);
        }

        if (progress.CompletedAt is null)
        {
            progress.ProgressCount = Math.Min(definition.TargetCount, progress.ProgressCount + 1);
            if (progress.ProgressCount >= definition.TargetCount)
            {
                progress.CompletedAt = SystemClock.Instance.GetCurrentInstant();
                progress.LastRewardToken = rewardToken;

                existingGrant = new SnProgressRewardGrant
                {
                    AccountId = evt.AccountId,
                    DefinitionType = ProgressionDefinitionType.Achievement,
                    DefinitionIdentifier = definition.Identifier,
                    DefinitionTitle = definition.Title,
                    RewardToken = rewardToken,
                    SourceEventId = evt.EventId,
                    Reward = definition.Reward
                };
                db.ProgressRewardGrants.Add(existingGrant);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return existingGrant;
    }

    private async Task<SnProgressRewardGrant?> ProcessQuestAsync(
        ActionLogTriggeredEvent evt,
        SnQuestDefinition definition,
        DateTimeZone zone,
        CancellationToken cancellationToken
    )
    {
        var (periodKey, _) = GetQuestPeriod(definition.Schedule, evt.OccurredAt, zone);
        var rewardToken = $"{ProgressionDefinitionType.Quest}:{definition.Identifier}:{evt.AccountId}:{periodKey}";
        var existingGrant = await db.ProgressRewardGrants
            .FirstOrDefaultAsync(m => m.RewardToken == rewardToken, cancellationToken);
        if (existingGrant is not null)
            return existingGrant;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var existingReceipt = await db.ProgressEventReceipts.AnyAsync(
            m => m.EventId == evt.EventId &&
                 m.DefinitionType == ProgressionDefinitionType.Quest &&
                 m.DefinitionIdentifier == definition.Identifier &&
                 m.PeriodKey == periodKey,
            cancellationToken
        );
        if (existingReceipt)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await db.ProgressRewardGrants.FirstOrDefaultAsync(m => m.RewardToken == rewardToken, cancellationToken);
        }

        db.ProgressEventReceipts.Add(new SnProgressEventReceipt
        {
            EventId = evt.EventId,
            AccountId = evt.AccountId,
            DefinitionType = ProgressionDefinitionType.Quest,
            DefinitionIdentifier = definition.Identifier,
            PeriodKey = periodKey
        });

        var progress = await db.AccountQuestProgresses
            .FirstOrDefaultAsync(
                m => m.AccountId == evt.AccountId && m.QuestDefinitionId == definition.Id && m.PeriodKey == periodKey,
                cancellationToken
            );
        if (progress is null)
        {
            progress = new SnAccountQuestProgress
            {
                AccountId = evt.AccountId,
                QuestDefinitionId = definition.Id,
                PeriodKey = periodKey
            };
            db.AccountQuestProgresses.Add(progress);
        }

        if (progress.CompletedAt is null)
        {
            progress.ProgressCount = Math.Min(definition.TargetCount, progress.ProgressCount + 1);
            if (progress.ProgressCount >= definition.TargetCount)
            {
                progress.CompletedAt = SystemClock.Instance.GetCurrentInstant();
                progress.LastRewardToken = rewardToken;
                progress.RepeatIterationCount += 1;

                existingGrant = new SnProgressRewardGrant
                {
                    AccountId = evt.AccountId,
                    DefinitionType = ProgressionDefinitionType.Quest,
                    DefinitionIdentifier = definition.Identifier,
                    DefinitionTitle = definition.Title,
                    RewardToken = rewardToken,
                    SourceEventId = evt.EventId,
                    Reward = definition.Reward,
                    PeriodKey = periodKey
                };
                db.ProgressRewardGrants.Add(existingGrant);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return existingGrant;
    }

    private async Task DeliverGrantAsync(SnProgressRewardGrant grant, CancellationToken cancellationToken)
    {
        var now = SystemClock.Instance.GetCurrentInstant();

        if (grant.Reward.Badge is not null && grant.BadgeGrantedAt is null)
        {
            var account = await accounts.GetAccount(grant.AccountId);
            if (account is not null)
            {
                var alreadyGranted = await db.Badges.AnyAsync(
                    m => m.AccountId == grant.AccountId && m.Type == grant.Reward.Badge.Type,
                    cancellationToken
                );
                if (!alreadyGranted)
                {
                    await accounts.GrantBadge(account, new SnAccountBadge
                    {
                        Type = grant.Reward.Badge.Type,
                        Label = grant.Reward.Badge.Label,
                        Caption = grant.Reward.Badge.Caption,
                        Meta = grant.Reward.Badge.Meta
                    });
                }
                grant.BadgeGrantedAt = now;
            }
        }

        if (grant.Reward.Experience > 0 && grant.ExperienceGrantedAt is null)
        {
            await experience.AddRecord("progression", grant.RewardToken, grant.Reward.Experience, grant.AccountId);
            grant.ExperienceGrantedAt = now;
        }

        if (grant.Reward.SourcePoints > 0 && grant.SourcePointsGrantedAt is null)
        {
            await payments.CreateTransactionWithAccount(
                null,
                grant.AccountId.ToString(),
                string.IsNullOrWhiteSpace(grant.Reward.SourcePointsCurrency)
                    ? seedService.GetSettings().SourcePointCurrency
                    : grant.Reward.SourcePointsCurrency,
                grant.Reward.SourcePoints.ToString(CultureInfo.InvariantCulture),
                $"Progression reward: {grant.DefinitionTitle}",
                DyTransactionType.System
            );
            grant.SourcePointsGrantedAt = now;
        }

        if (grant.NotificationSentAt is null)
        {
            var packet = new ProgressionCompletionPacket
            {
                Kind = grant.DefinitionType,
                Identifier = grant.DefinitionIdentifier,
                Title = grant.DefinitionTitle,
                PeriodKey = grant.PeriodKey,
                Reward = grant.Reward
            };
            var payload = JsonSerializer.SerializeToUtf8Bytes(packet);
            await ring.SendPushNotificationToUser(
                grant.AccountId.ToString(),
                "progression.completed",
                grant.DefinitionTitle,
                grant.DefinitionType,
                BuildNotificationBody(grant),
                payload,
                actionUri: "/account/progression",
                isSilent: true,
                isSavable: true
            );
            grant.NotificationSentAt = now;
        }

        db.ProgressRewardGrants.Update(grant);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static bool Matches(ActionLogTriggeredEvent evt, SnProgressTriggerDefinition trigger)
    {
        if (trigger.Actions.Count > 0 &&
            !trigger.Actions.Any(m => string.Equals(m, evt.Action, StringComparison.OrdinalIgnoreCase)))
            return false;

        foreach (var (key, expected) in trigger.MetaEquals)
        {
            if (!evt.Meta.TryGetValue(key, out var raw))
                return false;

            var actual = ConvertMetaValue(raw);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static bool IsAvailableAt(IProgressionDefinition definition, Instant instant)
    {
        if (definition.AvailableFrom.HasValue && instant < definition.AvailableFrom.Value)
            return false;

        if (definition.AvailableUntil.HasValue && instant > definition.AvailableUntil.Value)
            return false;

        return true;
    }

    private static string ConvertMetaValue(object raw)
    {
        return raw switch
        {
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonElement element => element.ToString(),
            _ => raw.ToString() ?? string.Empty
        };
    }

    private static string BuildNotificationBody(SnProgressRewardGrant grant)
    {
        var parts = new List<string>();

        if (grant.Reward.Badge is not null)
            parts.Add($"Badge: {grant.Reward.Badge.Label ?? grant.Reward.Badge.Type}");

        if (grant.Reward.Experience > 0)
            parts.Add($"+{grant.Reward.Experience} XP");

        if (grant.Reward.SourcePoints > 0)
            parts.Add($"+{grant.Reward.SourcePoints} {grant.Reward.SourcePointsCurrency}");

        if (parts.Count == 0)
            return $"Completed {grant.DefinitionType}.";

        return $"Completed {grant.DefinitionType}. Rewards: {string.Join(", ", parts)}";
    }

    private async Task<DateTimeZone> ResolveAccountZoneAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var timeZone = await db.AccountProfiles
            .Where(m => m.AccountId == accountId)
            .Select(m => m.TimeZone)
            .FirstOrDefaultAsync(cancellationToken);

        var fallback = seedService.GetSettings().DefaultTimeZone;
        return ResolveZone(string.IsNullOrWhiteSpace(timeZone) ? fallback : timeZone!);
    }

    private static DateTimeZone ResolveZone(string zoneId)
    {
        if (DateTimeZoneProviders.Tzdb.Ids.Contains(zoneId))
            return DateTimeZoneProviders.Tzdb[zoneId];

        return DateTimeZone.Utc;
    }

    private static (string PeriodKey, Instant NextResetAt) GetQuestPeriod(
        SnQuestScheduleConfig schedule,
        Instant instant,
        DateTimeZone zone
    )
    {
        var zoned = instant.InZone(zone);
        return schedule.Repeatability switch
        {
            QuestRepeatability.Weekly => GetWeeklyPeriod(zoned),
            QuestRepeatability.Monthly => GetMonthlyPeriod(zoned),
            _ => GetDailyPeriod(zoned)
        };
    }

    private static (string PeriodKey, Instant NextResetAt) GetDailyPeriod(ZonedDateTime zoned)
    {
        var next = zoned.Zone.AtStartOfDay(zoned.Date.PlusDays(1)).ToInstant();
        return (zoned.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), next);
    }

    private static (string PeriodKey, Instant NextResetAt) GetWeeklyPeriod(ZonedDateTime zoned)
    {
        var start = zoned.Date.PlusDays(1 - (int)zoned.Date.DayOfWeek);
        var next = zoned.Zone.AtStartOfDay(start.PlusWeeks(1)).ToInstant();
        return ($"{start.Year:0000}-W{WeekYearRules.Iso.GetWeekOfWeekYear(start):00}", next);
    }

    private static (string PeriodKey, Instant NextResetAt) GetMonthlyPeriod(ZonedDateTime zoned)
    {
        var start = new LocalDate(zoned.Date.Year, zoned.Date.Month, 1);
        var next = zoned.Zone.AtStartOfDay(start.PlusMonths(1)).ToInstant();
        return ($"{start.Year:0000}-{start.Month:00}", next);
    }
}
