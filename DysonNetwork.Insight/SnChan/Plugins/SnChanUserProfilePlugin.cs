using System.ComponentModel;
using System.Text.Json;
using DysonNetwork.Insight.Thought.Memory;
using DysonNetwork.Insight.Agent.Foundation;

namespace DysonNetwork.Insight.SnChan.Plugins;

public class SnChanUserProfilePlugin(
    UserProfileService userProfileService,
    ILogger<SnChanUserProfilePlugin> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    private const string BotName = "snchan";

    [AgentTool("get_user_profile", Description = "Get SnChan's structured profile for the current user")]
    public async Task<string> GetUserProfileAsync(
        [AgentToolParameter("The account ID of the user. Must be a Guid.")]
        Guid accountId)
    {
        try
        {
            var profile = await userProfileService.GetOrCreateAsync(accountId, BotName);
            return profile.ToPrompt();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get SnChan user profile for account {AccountId}", accountId);
            return $"Error getting user profile: {ex.Message}";
        }
    }

    [AgentTool("update_user_profile", Description = "Update SnChan's structured profile for a user, including profile summary, impression, relationship summary, tags, and relationship stats.")]
    public async Task<string> UpdateUserProfileAsync(
        [AgentToolParameter("The account ID of the user. Must be a Guid.")]
        Guid accountId,
        [AgentToolParameter("Optional: concise summary of the user's stable profile, background, or preferences.")]
        string? profileSummary = null,
        [AgentToolParameter("Optional: SnChan's current impression of the user.")]
        string? impressionSummary = null,
        [AgentToolParameter("Optional: summary of SnChan's relationship with the user.")]
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
                accountId: accountId,
                profileSummary: profileSummary,
                impressionSummary: impressionSummary,
                relationshipSummary: relationshipSummary,
                tags: parsedTags?.ToList(),
                favorability: favorability,
                trustLevel: trustLevel,
                intimacyLevel: intimacyLevel,
                botName: BotName);

            return JsonSerializer.Serialize(new 
            { 
                success = true, 
                message = "User profile updated",
                profile = profile.ToPrompt()
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update SnChan user profile for account {AccountId}", accountId);
            return JsonSerializer.Serialize(new 
            { 
                success = false, 
                error = ex.Message 
            }, JsonOptions);
        }
    }

    [AgentTool("adjust_relationship", Description = "Adjust SnChan's relationship scores for a user by the given deltas.")]
    public async Task<string> AdjustRelationshipAsync(
        [AgentToolParameter("The account ID of the user. Must be a Guid.")]
        Guid accountId,
        [AgentToolParameter("Favorability delta from -100 to 100 (positive = more liked)")]
        int? favorabilityDelta = null,
        [AgentToolParameter("Trust level delta from -100 to 100 (positive = more trusted)")]
        int? trustDelta = null,
        [AgentToolParameter("Intimacy delta from -100 to 100 (positive = more intimate)")]
        int? intimacyDelta = null)
    {
        try
        {
            await userProfileService.AdjustRelationshipAsync(
                accountId: accountId,
                favorabilityDelta: favorabilityDelta ?? 0,
                trustDelta: trustDelta ?? 0,
                intimacyDelta: intimacyDelta ?? 0,
                relationshipNote: null,
                botName: BotName);

            var profile = await userProfileService.GetOrCreateAsync(accountId, BotName);

            return JsonSerializer.Serialize(new 
            { 
                success = true, 
                message = "Relationship adjusted",
                favorability = profile.Favorability,
                trustLevel = profile.TrustLevel,
                intimacyLevel = profile.IntimacyLevel
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to adjust relationship for account {AccountId}", accountId);
            return JsonSerializer.Serialize(new 
            { 
                success = false, 
                error = ex.Message 
            }, JsonOptions);
        }
    }
}
