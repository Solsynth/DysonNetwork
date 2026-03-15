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
        CancellationToken cancellationToken = default)
    {
        var profile = await database.UserProfiles
            .FirstOrDefaultAsync(p => p.AccountId == accountId, cancellationToken);

        if (profile != null)
            return profile;

        profile = new MiChanUserProfile
        {
            AccountId = accountId
        };

        database.UserProfiles.Add(profile);
        await database.SaveChangesAsync(cancellationToken);

        logger.LogDebug("Created MiChan user profile for account {AccountId}", accountId);
        return profile;
    }

    public async Task<MiChanUserProfile?> GetAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        return await database.UserProfiles
            .FirstOrDefaultAsync(p => p.AccountId == accountId, cancellationToken);
    }

    public async Task<MiChanUserProfile> TouchInteractionAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var profile = await GetOrCreateAsync(accountId, cancellationToken);
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
        CancellationToken cancellationToken = default)
    {
        var profile = await GetOrCreateAsync(accountId, cancellationToken);

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

        logger.LogInformation("Updated MiChan user profile for account {AccountId}", accountId);
        return profile;
    }

    public async Task<MiChanUserProfile> AdjustRelationshipAsync(
        Guid accountId,
        int favorabilityDelta = 0,
        int trustDelta = 0,
        int intimacyDelta = 0,
        string? relationshipNote = null,
        CancellationToken cancellationToken = default)
    {
        var profile = await GetOrCreateAsync(accountId, cancellationToken);

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
            "Adjusted MiChan relationship for account {AccountId}: favorability={Favorability}, trust={Trust}, intimacy={Intimacy}",
            accountId, profile.Favorability, profile.TrustLevel, profile.IntimacyLevel);

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
