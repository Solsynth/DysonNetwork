using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Passport.Progression;

public class ProgressionSeedService(
    AppDatabase db,
    IConfiguration configuration,
    ILogger<ProgressionSeedService> logger
)
{
    private readonly AppDatabase _db = db;
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<ProgressionSeedService> _logger = logger;

    public ProgressionSeedSettings GetSettings()
    {
        var options = _configuration.GetSection("Progression").Get<ProgressionSeedOptions>();
        return options?.Settings ?? new ProgressionSeedSettings();
    }

    public async Task EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        var options = _configuration.GetSection("Progression").Get<ProgressionSeedOptions>();
        if (options is null) return;

        await SyncAchievementsAsync(options.Achievements, cancellationToken);
        await SyncQuestsAsync(options.Quests, cancellationToken);
    }

    private async Task SyncAchievementsAsync(List<AchievementSeedDefinition> definitions, CancellationToken cancellationToken)
    {
        if (definitions.Count == 0) return;

        var identifiers = definitions.Select(m => m.Identifier).ToList();
        var existingMap = await _db.AchievementDefinitions
            .Where(m => identifiers.Contains(m.Identifier))
            .ToDictionaryAsync(m => m.Identifier, StringComparer.Ordinal, cancellationToken);

        foreach (var definition in definitions)
        {
            if (!existingMap.TryGetValue(definition.Identifier, out var existing))
            {
                _db.AchievementDefinitions.Add(BuildAchievement(definition));
                continue;
            }

            if (!existing.IsSeedManaged) continue;
            ApplyAchievement(existing, definition);
        }

        if (_db.ChangeTracker.HasChanges())
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Synchronized achievement definitions from configuration.");
        }
    }

    private async Task SyncQuestsAsync(List<QuestSeedDefinition> definitions, CancellationToken cancellationToken)
    {
        if (definitions.Count == 0) return;

        var identifiers = definitions.Select(m => m.Identifier).ToList();
        var existingMap = await _db.QuestDefinitions
            .Where(m => identifiers.Contains(m.Identifier))
            .ToDictionaryAsync(m => m.Identifier, StringComparer.Ordinal, cancellationToken);

        foreach (var definition in definitions)
        {
            if (!existingMap.TryGetValue(definition.Identifier, out var existing))
            {
                _db.QuestDefinitions.Add(BuildQuest(definition));
                continue;
            }

            if (!existing.IsSeedManaged) continue;
            ApplyQuest(existing, definition);
        }

        if (_db.ChangeTracker.HasChanges())
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Synchronized quest definitions from configuration.");
        }
    }

    private static SnAchievementDefinition BuildAchievement(AchievementSeedDefinition definition)
    {
        return new SnAchievementDefinition
        {
            Identifier = definition.Identifier,
            Title = definition.Title,
            Summary = definition.Summary,
            Icon = definition.Icon,
            SortOrder = definition.SortOrder,
            Hidden = definition.Hidden,
            IsEnabled = definition.IsEnabled,
            IsSeedManaged = definition.IsSeedManaged,
            TargetCount = definition.TargetCount,
            Trigger = definition.Trigger,
            Reward = definition.Reward
        };
    }

    private static SnQuestDefinition BuildQuest(QuestSeedDefinition definition)
    {
        return new SnQuestDefinition
        {
            Identifier = definition.Identifier,
            Title = definition.Title,
            Summary = definition.Summary,
            Icon = definition.Icon,
            SortOrder = definition.SortOrder,
            Hidden = definition.Hidden,
            IsEnabled = definition.IsEnabled,
            IsSeedManaged = definition.IsSeedManaged,
            TargetCount = definition.TargetCount,
            Trigger = definition.Trigger,
            Schedule = definition.Schedule,
            Reward = definition.Reward
        };
    }

    private static void ApplyAchievement(SnAchievementDefinition existing, AchievementSeedDefinition definition)
    {
        existing.Title = definition.Title;
        existing.Summary = definition.Summary;
        existing.Icon = definition.Icon;
        existing.SortOrder = definition.SortOrder;
        existing.Hidden = definition.Hidden;
        existing.IsEnabled = definition.IsEnabled;
        existing.TargetCount = definition.TargetCount;
        existing.Trigger = definition.Trigger;
        existing.Reward = definition.Reward;
    }

    private static void ApplyQuest(SnQuestDefinition existing, QuestSeedDefinition definition)
    {
        existing.Title = definition.Title;
        existing.Summary = definition.Summary;
        existing.Icon = definition.Icon;
        existing.SortOrder = definition.SortOrder;
        existing.Hidden = definition.Hidden;
        existing.IsEnabled = definition.IsEnabled;
        existing.TargetCount = definition.TargetCount;
        existing.Trigger = definition.Trigger;
        existing.Schedule = definition.Schedule;
        existing.Reward = definition.Reward;
    }
}
