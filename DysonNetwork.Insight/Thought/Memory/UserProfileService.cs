using DysonNetwork.Insight.MiChan;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Insight.Thought.Memory;

public class UserProfileService(
    AppDatabase database,
    ILogger<UserProfileService> logger
)
{
    public async Task<MiChanUserProfile> GetOrCreateAsync(
        Guid accountId,
        string? botName = null,
        CancellationToken cancellationToken = default)
    {
        // Build query with optional bot name filter
        var query = database.UserProfiles.AsQueryable();

        if (string.IsNullOrEmpty(botName))
        {
            // If no bot name specified, look for profiles with no bot name assigned (backward compatible)
            query = query.Where(p => p.AccountId == accountId && p.BotName == null);
        }
        else
        {
            // Look for bot-specific profile
            query = query.Where(p => p.AccountId == accountId && p.BotName == botName);
        }

        var profile = await query.FirstOrDefaultAsync(cancellationToken);

        if (profile != null)
            return profile;

        profile = new MiChanUserProfile
        {
            AccountId = accountId,
            BotName = botName
        };

        database.UserProfiles.Add(profile);
        await database.SaveChangesAsync(cancellationToken);

        logger.LogDebug("Created MiChan user profile for account {AccountId}, bot: {BotName}", accountId, botName ?? "none");
        return profile;
    }

    public async Task<MiChanUserProfile?> GetAsync(
        Guid accountId,
        string? botName = null,
        CancellationToken cancellationToken = default)
    {
        var query = database.UserProfiles.AsQueryable();

        if (string.IsNullOrEmpty(botName))
        {
            query = query.Where(p => p.AccountId == accountId && p.BotName == null);
        }
        else
        {
            query = query.Where(p => p.AccountId == accountId && p.BotName == botName);
        }

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<MiChanUserProfile> TouchInteractionAsync(
        Guid accountId,
        string? botName = null,
        CancellationToken cancellationToken = default)
    {
        var profile = await GetOrCreateAsync(accountId, botName, cancellationToken);
        profile.InteractionCount += 1;
        profile.LastInteractionAt = SystemClock.Instance.GetCurrentInstant();
        await database.SaveChangesAsync(cancellationToken);
        return profile;
    }

    public async Task<MiChanUserProfile> UpdateProfileAsync(
        Guid accountId,
        string? profileSummary = null,
        string? impressionSummary = null,
        string? relationshipSummary = null,
        IEnumerable<string>? tags = null,
        int? favorability = null,
        int? trustLevel = null,
        int? intimacyLevel = null,
        string? botName = null,
        CancellationToken cancellationToken = default)
    {
        var profile = await GetOrCreateAsync(accountId, botName, cancellationToken);

        if (profileSummary != null)
            profile.ProfileSummary = NullIfWhiteSpace(profileSummary);
        if (impressionSummary != null)
            profile.ImpressionSummary = NullIfWhiteSpace(impressionSummary);
        if (relationshipSummary != null)
            profile.RelationshipSummary = NullIfWhiteSpace(relationshipSummary);
        if (tags != null)
            profile.Tags = tags
                .Select(tag => tag.Trim())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        if (favorability.HasValue)
            profile.Favorability = ClampRelationshipValue(favorability.Value);
        if (trustLevel.HasValue)
            profile.TrustLevel = ClampRelationshipValue(trustLevel.Value);
        if (intimacyLevel.HasValue)
            profile.IntimacyLevel = ClampRelationshipValue(intimacyLevel.Value);

        profile.LastProfileUpdateAt = SystemClock.Instance.GetCurrentInstant();
        await database.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Updated MiChan user profile for account {AccountId}, bot: {BotName}", accountId, botName ?? "none");
        return profile;
    }

    public async Task<MiChanUserProfile> AdjustRelationshipAsync(
        Guid accountId,
        int favorabilityDelta = 0,
        int trustDelta = 0,
        int intimacyDelta = 0,
        string? relationshipNote = null,
        string? botName = null,
        CancellationToken cancellationToken = default)
    {
        var profile = await GetOrCreateAsync(accountId, botName, cancellationToken);

        profile.Favorability = ClampRelationshipValue(profile.Favorability + favorabilityDelta);
        profile.TrustLevel = ClampRelationshipValue(profile.TrustLevel + trustDelta);
        profile.IntimacyLevel = ClampRelationshipValue(profile.IntimacyLevel + intimacyDelta);

        if (!string.IsNullOrWhiteSpace(relationshipNote))
        {
            profile.RelationshipSummary = MergeSummary(profile.RelationshipSummary, relationshipNote);
        }

        profile.LastProfileUpdateAt = SystemClock.Instance.GetCurrentInstant();
        await database.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Adjusted MiChan relationship for account {AccountId}, bot: {BotName}: favorability={Favorability}, trust={Trust}, intimacy={Intimacy}",
            accountId, botName ?? "none", profile.Favorability, profile.TrustLevel, profile.IntimacyLevel);

        return profile;
    }

    private static int ClampRelationshipValue(int value) => Math.Clamp(value, -100, 100);

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string MergeSummary(string? existing, string update)
    {
        if (string.IsNullOrWhiteSpace(existing))
            return update.Trim();

        return $"{existing.Trim()}\n{update.Trim()}";
    }
}
