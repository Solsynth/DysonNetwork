using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DysonNetwork.Passport.Progression;

public class BadgesOptions
{
    public string? IconsPath { get; set; }
}

[ApiController]
[Route(".well-known")]
public class BadgesDiscoveryController(
    IOptions<BadgesOptions> options,
    ILogger<BadgesDiscoveryController> logger
) : ControllerBase
{
    [HttpGet("badges")]
    [ResponseCache(Duration = 3600)]
    public IActionResult GetBadgesManifest()
    {
        var iconsPath = options.Value.IconsPath;
        var hasIcons = !string.IsNullOrWhiteSpace(iconsPath) && Directory.Exists(iconsPath);

        var badges = BadgesManifestData.GetBadges(identifier =>
            hasIcons ? $"/.well-known/badges/icons/{identifier}" : null
        );

        return Ok(new
        {
            version = 1,
            badges
        });
    }

    [HttpGet("badges/icons/{identifier}")]
    [ResponseCache(Duration = 86400)]
    public IActionResult GetBadgeIcon(string identifier)
    {
        var iconsPath = options.Value.IconsPath;
        if (string.IsNullOrWhiteSpace(iconsPath))
            return NotFound();

        var fullPath = Path.GetFullPath(Path.Combine(iconsPath, $"{identifier}.svg"));
        var rootPath = Path.GetFullPath(iconsPath);

        if (!fullPath.StartsWith(rootPath, StringComparison.Ordinal))
            return BadRequest();

        if (!System.IO.File.Exists(fullPath))
            return NotFound();

        var bytes = System.IO.File.ReadAllBytes(fullPath);
        return File(bytes, "image/svg+xml");
    }
}

public static class BadgesManifestData
{
    private static readonly (string Identifier, string AchievementIdentifier, string Label, string Caption, string Icon, string Color, string LocalizationKey, string Category, object? Series, bool Hidden)[] BadgeEntries =
    [
        ("progression.post.expert", "expert-post", "Better than 陆游", "Created over 9362 posts", "ink", "#6366f1", "badge.post_expert", "post", null, false),
        ("progression.post.featured", "initial-featured-post", "Editor's Pick", "Created a post that got featured.", "sparkles", "#f59e0b", "badge.post_featured", "post", new { identifier = "featured-post", title = "Featured Posts", order = 1 }, false),
        ("progression.post.streak.30", "streak-post-30", "Serial Publisher", "Created posts for 30 days in a row.", "calendar-fold", "#f97316", "badge.post_streak_30", "streak", new { identifier = "post-streak", title = "Posting Streak", order = 3 }, false),
        ("progression.post.streak.90", "streak-post-90", "Quarterly Author", "Created posts for 90 days in a row.", "calendar-check", "#ea580c", "badge.post_streak_90", "streak", new { identifier = "post-streak", title = "Posting Streak", order = 4 }, false),
        ("progression.post.streak.365", "streak-post-365", "Daily Devotee", "Created posts for 365 days in a row.", "calendar-heart", "#dc2626", "badge.post_streak_365", "streak", new { identifier = "post-streak", title = "Posting Streak", order = 5 }, false),
        ("progression.login.streak.365", "streak-login-365", "Still Here", "Logged in for 365 days in a row.", "sun", "#eab308", "badge.login_streak_365", "streak", new { identifier = "activity-streak", title = "Activity Streak", order = 3 }, false),
        ("progression.login.streak.90", "streak-login-90", "Perennial", "Stayed active for 90 days in a row.", "sunrise", "#facc15", "badge.login_streak_90", "streak", new { identifier = "activity-streak", title = "Activity Streak", order = 4 }, false),
        ("progression.post.featured.expert", "expert-featured-post", "Hall of Fame", "Created 100 featured posts.", "sparkles", "#d97706", "badge.post_featured_expert", "post", new { identifier = "featured-post", title = "Featured Posts", order = 2 }, false),
        ("progression.stellar.supporter.12", "stellar-supporter-12", "Mega Supporter", "Purchased 12 eligible months of the Stellar Program.", "crown", "#a855f7", "badge.stellar_supporter_12", "supporter", new { identifier = "stellar-supporter", title = "Stellar Supporter", order = 5 }, false),
        ("progression.chat.expert", "expert-chat", "No-life's Otaku", "I can't believe some one spend for 10k minutes chatting on the Solar Network.", "messages-square", "#8b5cf6", "badge.chat_expert", "chat", null, false),
        ("progression.account.avatar", "first-avatar", "Picture Perfect", "Set a profile picture for the first time.", "image", "#22c55e", "badge.account_avatar", "account", null, false),
        ("progression.friends.50", "friends-50", "Community Pillar", "Made 50 friends on the Solar Network.", "heart-handshake", "#ec4899", "badge.friends_50", "social", new { identifier = "friend-count", title = "Friend Count", order = 4 }, false),
        ("progression.friends.100", "friends-100", "The Connector", "Made 100 friends on the Solar Network.", "network", "#f43f5e", "badge.friends_100", "social", new { identifier = "friend-count", title = "Friend Count", order = 5 }, false),
        ("progression.post.topical.first", "first-topical-post", "Well Structured", "Published a post with both categories and tags.", "layout-grid", "#14b8a6", "badge.post_topical_first", "post", null, false),
        ("progression.post.topical.50", "topical-post-50", "Organizer", "Published 50 posts with both categories and tags.", "layout-template", "#0d9488", "badge.post_topical_50", "post", null, false),
        ("progression.reaction.expert", "expert-reaction", "Empath", "Reacted to 1000 posts on the Solar Network.", "heart", "#e11d48", "badge.reaction_expert", "social", null, false),
        ("progression.post.downvote.5", "gots-fired-5", "炎上", "Received repeated downvotes on a publisher post in one day.", "flame", "#7c2d12", "badge.post_downvote_5", "hidden", null, true),
        ("progression.post.downvote.20", "gots-fired-20", "炎上", "Received a lot of downvotes on publisher posts.", "fire-extinguisher", "#991b1b", "badge.post_downvote_20", "hidden", null, true),
        ("progression.account.profile_complete", "profile-complete", "Fully Realized", "Completed their profile.", "user-check", "#16a34a", "badge.account_profile_complete", "account", null, false),
        ("progression.boost.50", "boost-50", "Signal Tower", "Boosted 50 posts on the Solar Network.", "repeat", "#0ea5e9", "badge.boost_50", "post", null, false),
        ("progression.hidden.night_owl", "hidden-night-owl", "Night Owl", "Posted between midnight and 4am.", "moon", "#1e1b4b", "badge.hidden_night_owl", "hidden", null, true),
        ("progression.hidden.speed_friend", "hidden-speed-friend", "Instant Connection", "Accepted a friend request within 60 seconds.", "zap", "#7c3aed", "badge.hidden_speed_friend", "hidden", null, true),
        ("progression.hidden.social_butterfly_day", "hidden-social-butterfly-day", "Social Butterfly", "Made 5 friends in a single day.", "butterfly", "#c026d3", "badge.hidden_social_butterfly_day", "hidden", null, true),
    ];

    public static object[] GetBadges(Func<string, string?> iconUrlFactory)
    {
        return BadgeEntries.Select(e =>
        {
            var iconUrl = iconUrlFactory(e.Identifier);
            return (object)new
            {
                identifier = e.Identifier,
                achievement_identifier = e.AchievementIdentifier,
                label = e.Label,
                caption = e.Caption,
                icon = e.Icon,
                color = e.Color,
                icon_url = iconUrl,
                localization_key = e.LocalizationKey,
                category = e.Category,
                series = e.Series,
                hidden = e.Hidden,
            };
        }).ToArray();
    }
}
