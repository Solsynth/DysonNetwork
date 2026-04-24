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

    [AgentTool("update_user_profile", Description = "Create or update MiChan's structured profile for a user, including profile summary, impression, relationship summary, user attitude memory, tags, and relationship stats.")]
    public async Task<string> UpdateUserProfileAsync(
        [AgentToolParameter("The account ID of the user. Must be a Guid.")]
        Guid accountId,
        [AgentToolParameter("Optional: concise summary of the user's stable profile, background, or preferences.")]
        string? profileSummary = null,
        [AgentToolParameter("Optional: MiChan's current impression of the user.")]
        string? impressionSummary = null,
        [AgentToolParameter("Optional: summary of MiChan's relationship with the user.")]
        string? relationshipSummary = null,
        [AgentToolParameter("Optional: summary of this user's attitude toward MiChan in recent interactions.")]
        string? attitudeSummary = null,
        [AgentToolParameter("Optional: attitude trend label such as warming, stable, cooling, mixed.")]
        string? attitudeTrend = null,
        [AgentToolParameter("Optional: comma-separated tags describing the user, such as interests or traits.")]
        string? tags = null,
        [AgentToolParameter("Optional: relationship score from -100 to 100.")]
        int? favorability = null,
        [AgentToolParameter("Optional: trust score from -100 to 100.")]
        int? trustLevel = null,
        [AgentToolParameter("Optional: intimacy score from -100 to 100.")]
        int? intimacyLevel = null,
        [AgentToolParameter("Optional: user's warmth score toward MiChan from -100 to 100.")]
        int? userWarmth = null,
        [AgentToolParameter("Optional: user's respect score toward MiChan from -100 to 100.")]
        int? userRespect = null,
        [AgentToolParameter("Optional: user's engagement score with MiChan from -100 to 100.")]
        int? userEngagement = null)
    {
        try
        {
            var parsedTags = string.IsNullOrWhiteSpace(tags)
                ? null
                : tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var profile = await userProfileService.UpdateProfileAsync(
                accountId: accountId,
                profileSummary: profileSummary,
                impressionSummary: impressionSummary,
                relationshipSummary: relationshipSummary,
                attitudeSummary: attitudeSummary,
                attitudeTrend: attitudeTrend,
                tags: parsedTags,
                favorability: favorability,
                trustLevel: trustLevel,
                intimacyLevel: intimacyLevel,
                userWarmth: userWarmth,
                userRespect: userRespect,
                userEngagement: userEngagement,
                botName: DefaultBotName);

            return $"User profile updated.\n{profile.ToPrompt()}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update MiChan user profile for account {AccountId}", accountId);
            return $"Error updating user profile: {ex.Message}";
        }
    }

    [AgentTool("adjust_relationship", Description = "Adjust MiChan's relationship and user-attitude values after an interaction, including favorability/trust/intimacy and warmth/respect/engagement deltas.")]
    public async Task<string> AdjustRelationshipAsync(
        [AgentToolParameter("The account ID of the user. Must be a Guid.")]
        Guid accountId,
        [AgentToolParameter("Delta for favorability from -100 to 100.")]
        int favorabilityDelta = 0,
        [AgentToolParameter("Delta for trust from -100 to 100.")]
        int trustDelta = 0,
        [AgentToolParameter("Delta for intimacy from -100 to 100.")]
        int intimacyDelta = 0,
        [AgentToolParameter("Delta for user's warmth toward MiChan from -100 to 100.")]
        int userWarmthDelta = 0,
        [AgentToolParameter("Delta for user's respect toward MiChan from -100 to 100.")]
        int userRespectDelta = 0,
        [AgentToolParameter("Delta for user's engagement with MiChan from -100 to 100.")]
        int userEngagementDelta = 0,
        [AgentToolParameter("Optional: short note describing why the relationship changed.")]
        string? relationshipNote = null,
        [AgentToolParameter("Optional: short note describing the user's attitude change toward MiChan.")]
        string? attitudeNote = null,
        [AgentToolParameter("Optional: attitude trend label such as warming, stable, cooling, mixed.")]
        string? attitudeTrend = null)
    {
        try
        {
            var profile = await userProfileService.AdjustRelationshipAsync(
                accountId: accountId,
                favorabilityDelta: favorabilityDelta,
                trustDelta: trustDelta,
                intimacyDelta: intimacyDelta,
                userWarmthDelta: userWarmthDelta,
                userRespectDelta: userRespectDelta,
                userEngagementDelta: userEngagementDelta,
                relationshipNote: relationshipNote,
                attitudeNote: attitudeNote,
                attitudeTrend: attitudeTrend,
                botName: DefaultBotName);

            return $"Relationship updated.\n{profile.ToPrompt()}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to adjust MiChan relationship for account {AccountId}", accountId);
            return $"Error adjusting relationship: {ex.Message}";
        }
    }
}
