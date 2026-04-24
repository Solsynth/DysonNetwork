using System.ComponentModel;
using DysonNetwork.Insight.Agent.Foundation;
using DysonNetwork.Insight.Thought.Memory;

namespace DysonNetwork.Insight.MiChan.Plugins;

public class UserProfilePlugin(
    UserProfileService userProfileService,
    ILogger<UserProfilePlugin> logger)
{
    private const string DefaultBotName = "michan";

    [AgentTool("get_user_profile", Description = "Get MiChan's structured profile and relationship state for a specific user.")]
    public async Task<string> GetUserProfileAsync(
        [AgentToolParameter("The account ID of the user. Must be a Guid.")]
        Guid accountId)
    {
        try
        {
            var profile = await userProfileService.GetOrCreateAsync(accountId, DefaultBotName);
            return profile.ToPrompt();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get MiChan user profile for account {AccountId}", accountId);
            return $"Error getting user profile: {ex.Message}";
        }
    }

    [AgentTool("update_user_profile", Description = "Create or update MiChan's structured profile for a user, including profile summary, impression, relationship summary, tags, and relationship stats.")]
    public async Task<string> UpdateUserProfileAsync(
        [AgentToolParameter("The account ID of the user. Must be a Guid.")]
        Guid accountId,
        [AgentToolParameter("Optional: concise summary of the user's stable profile, background, or preferences.")]
        string? profileSummary = null,
        [AgentToolParameter("Optional: MiChan's current impression of the user.")]
        string? impressionSummary = null,
        [AgentToolParameter("Optional: summary of MiChan's relationship with the user.")]
        string? relationshipSummary = null,
        [AgentToolParameter("Optional: comma-separated tags describing the user, such as interests or traits.")]
        string? tags = null,
        [AgentToolParameter("Optional: relationship score from -100 to 100.")]
        int? favorability = null,
        [AgentToolParameter("Optional: trust score from -100 to 100.")]
        int? trustLevel = null,
        [AgentToolParameter("Optional: intimacy score from -100 to 100.")]
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
                intimacyLevel,
                DefaultBotName);

            return $"User profile updated.\n{profile.ToPrompt()}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update MiChan user profile for account {AccountId}", accountId);
            return $"Error updating user profile: {ex.Message}";
        }
    }

    [AgentTool("adjust_relationship", Description = "Adjust MiChan's relationship values for a user after an interaction, including favorability, trust, and intimacy deltas.")]
    public async Task<string> AdjustRelationshipAsync(
        [AgentToolParameter("The account ID of the user. Must be a Guid.")]
        Guid accountId,
        [AgentToolParameter("Delta for favorability from -100 to 100.")]
        int favorabilityDelta = 0,
        [AgentToolParameter("Delta for trust from -100 to 100.")]
        int trustDelta = 0,
        [AgentToolParameter("Delta for intimacy from -100 to 100.")]
        int intimacyDelta = 0,
        [AgentToolParameter("Optional: short note describing why the relationship changed.")]
        string? relationshipNote = null)
    {
        try
        {
            var profile = await userProfileService.AdjustRelationshipAsync(
                accountId,
                favorabilityDelta,
                trustDelta,
                intimacyDelta,
                relationshipNote,
                DefaultBotName);

            return $"Relationship updated.\n{profile.ToPrompt()}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to adjust MiChan relationship for account {AccountId}", accountId);
            return $"Error adjusting relationship: {ex.Message}";
        }
    }
}
