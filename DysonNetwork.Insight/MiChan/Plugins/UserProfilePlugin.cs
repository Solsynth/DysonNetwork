using System.ComponentModel;
using DysonNetwork.Insight.Thought.Memory;
using Microsoft.SemanticKernel;

namespace DysonNetwork.Insight.MiChan.Plugins;

public class UserProfilePlugin(
    UserProfileService userProfileService,
    ILogger<UserProfilePlugin> logger)
{
    [KernelFunction("get_user_profile")]
    [Description("Get MiChan's structured profile and relationship state for a specific user.")]
    public async Task<string> GetUserProfileAsync(
        [Description("The account ID of the user. Must be a Guid.")]
        Guid accountId)
    {
        try
        {
            var profile = await userProfileService.GetOrCreateAsync(accountId);
            return profile.ToPrompt();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get MiChan user profile for account {AccountId}", accountId);
            return $"Error getting user profile: {ex.Message}";
        }
    }

    [KernelFunction("update_user_profile")]
    [Description("Create or update MiChan's structured profile for a user, including profile summary, impression, relationship summary, tags, and relationship stats.")]
    public async Task<string> UpdateUserProfileAsync(
        [Description("The account ID of the user. Must be a Guid.")]
        Guid accountId,
        [Description("Optional: concise summary of the user's stable profile, background, or preferences.")]
        string? profileSummary = null,
        [Description("Optional: MiChan's current impression of the user.")]
        string? impressionSummary = null,
        [Description("Optional: summary of MiChan's relationship with the user.")]
        string? relationshipSummary = null,
        [Description("Optional: comma-separated tags describing the user, such as interests or traits.")]
        string? tags = null,
        [Description("Optional: relationship score from -100 to 100.")]
        int? favorability = null,
        [Description("Optional: trust score from -100 to 100.")]
        int? trustLevel = null,
        [Description("Optional: intimacy score from -100 to 100.")]
        int? intimacyLevel = null)
    {
        try
        {
            var parsedTags = string.IsNullOrWhiteSpace(tags)
                ? null
                : tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var profile = await userProfileService.UpdateProfileAsync(
                accountId,
                profileSummary,
                impressionSummary,
                relationshipSummary,
                parsedTags,
                favorability,
                trustLevel,
                intimacyLevel);

            return $"User profile updated.\n{profile.ToPrompt()}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update MiChan user profile for account {AccountId}", accountId);
            return $"Error updating user profile: {ex.Message}";
        }
    }

    [KernelFunction("adjust_relationship")]
    [Description("Adjust MiChan's relationship values for a user after an interaction, including favorability, trust, and intimacy deltas.")]
    public async Task<string> AdjustRelationshipAsync(
        [Description("The account ID of the user. Must be a Guid.")]
        Guid accountId,
        [Description("Delta for favorability from -100 to 100.")]
        int favorabilityDelta = 0,
        [Description("Delta for trust from -100 to 100.")]
        int trustDelta = 0,
        [Description("Delta for intimacy from -100 to 100.")]
        int intimacyDelta = 0,
        [Description("Optional: short note describing why the relationship changed.")]
        string? relationshipNote = null)
    {
        try
        {
            var profile = await userProfileService.AdjustRelationshipAsync(
                accountId,
                favorabilityDelta,
                trustDelta,
                intimacyDelta,
                relationshipNote);

            return $"Relationship updated.\n{profile.ToPrompt()}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to adjust MiChan relationship for account {AccountId}", accountId);
            return $"Error adjusting relationship: {ex.Message}";
        }
    }
}
