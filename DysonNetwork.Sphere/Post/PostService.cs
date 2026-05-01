using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.ComponentModel.DataAnnotations.Schema;
using AngleSharp.Html.Parser;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Localization;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Models.Embed;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Sphere.ActivityPub;
using DysonNetwork.Sphere.ActivityPub.Services;
using DysonNetwork.Sphere.Publisher;
using Markdig;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Npgsql;
using PostContentType = DysonNetwork.Shared.Models.PostContentType;

namespace DysonNetwork.Sphere.Post;

public partial class PostService(
    AppDatabase db,
    IServiceProvider serviceProvider,
    ILocalizationService localizer,
    IServiceScopeFactory factory,
    FlushBufferService flushBuffer,
    ICacheService cache,
    ILogger<PostService> logger,
    DyFileService.DyFileServiceClient files,
    PublisherService ps,
    PostTagService tagService,
    RemoteWebReaderService reader,
    DyProfileService.DyProfileServiceClient accounts,
    ActivityRenderer objFactory,
    RemoteActionLogService actionLogs
)
{
    private sealed class ThreadReplyCountResult
    {
        [Column("ancestor_id")]
        public Guid AncestorId { get; set; }

        [Column("count")]
        public int Count { get; set; }
    }

    private const int PositiveReactionWeight = 2;
    private const int NeutralReactionWeight = 1;
    private const int NegativeReactionWeight = -2;
    private const double PositiveInterestScore = 2.0;
    private const double NeutralInterestScore = 1.0;
    private const double NegativeInterestScore = -2.0;
    private const double ReplyInterestScore = 1.5;
    private const double ViewInterestScore = 0.2;
    private const double PublisherViewInterestMultiplier = 0.15;
    private const double PublisherReplyInterestMultiplier = 0.75;
    private const string PublisherDefaultTagsMetaKey = "default_post_tags";
    private const string PublisherDefaultCategoriesMetaKey = "default_post_categories";
    private const string PostAutoTaggingMetaKey = "auto_tagging";

    private static int GetReactionWeight(PostReactionAttitude attitude) =>
        attitude switch
        {
            PostReactionAttitude.Positive => PositiveReactionWeight,
            PostReactionAttitude.Neutral => NeutralReactionWeight,
            PostReactionAttitude.Negative => NegativeReactionWeight,
            _ => 0,
        };

    private static string? GetEmojiForSymbol(string symbol) => symbol switch
    {
        "thumb_up" => "👍",
        "thumb_down" => "👎",
        "heart" => "❤️",
        "laugh" => "😂",
        "clap" => "👏",
        "party" => "🎉",
        "pray" => "🙏",
        "cry" => "😭",
        "confuse" => "😕",
        "angry" => "😡",
        "just_okay" => "😐",
        _ => null
    };

    private static double ClampInterestScore(double score) => Math.Clamp(score, -100d, 100d);

    private static double AdjustInterestDeltaForTarget(
        double scoreDelta,
        string signalType,
        PostInterestKind kind
    )
    {
        if (kind != PostInterestKind.Publisher)
            return scoreDelta;

        if (signalType.Equals("view", StringComparison.OrdinalIgnoreCase))
            return scoreDelta * PublisherViewInterestMultiplier;

        if (signalType.Equals("reply", StringComparison.OrdinalIgnoreCase))
            return scoreDelta * PublisherReplyInterestMultiplier;

        return scoreDelta;
    }

    private static string NormalizeTopicSlug(string value) =>
        value.Trim().ToLowerInvariant();

    private static List<string> NormalizeTopicSlugs(IEnumerable<string> values) =>
        values.Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(NormalizeTopicSlug)
            .Distinct()
            .ToList();

    private static string NormalizeTopicText(string? value) =>
        Regex.Replace(value?.ToLowerInvariant() ?? string.Empty, @"[\s\-_\.]+", " ").Trim();

    private static List<string> ExtractStringList(object? value)
    {
        return value switch
        {
            null => [],
            List<string> items => NormalizeTopicSlugs(items),
            string[] items => NormalizeTopicSlugs(items),
            IEnumerable<object> items => NormalizeTopicSlugs(items.Select(x => x?.ToString() ?? string.Empty)),
            JsonElement { ValueKind: JsonValueKind.Array } element => NormalizeTopicSlugs(
                element.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString() ?? string.Empty)
            ),
            JsonElement { ValueKind: JsonValueKind.String } element => NormalizeTopicSlugs(
                [element.GetString() ?? string.Empty]
            ),
            _ => [],
        };
    }

    private static List<string> GetPublisherTopicDefaults(SnPublisher? publisher, string key) =>
        publisher?.Meta is not null && publisher.Meta.TryGetValue(key, out var value)
            ? ExtractStringList(value)
            : [];

    private async Task<SnPublisher?> GetPublisherForPostAsync(SnPost post)
    {
        if (post.Publisher is not null)
            return post.Publisher;
        if (!post.PublisherId.HasValue)
            return null;

        return await db.Publishers.FirstOrDefaultAsync(p => p.Id == post.PublisherId.Value);
    }

    private async Task<List<SnPostTag>> ResolveTagsAsync(IEnumerable<string> slugs, SnPublisher? publisher = null)
    {
        var normalizedSlugs = NormalizeTopicSlugs(slugs);
        if (normalizedSlugs.Count == 0)
            return [];

        var existingTags = await db.PostTags.Where(e => normalizedSlugs.Contains(e.Slug)).ToListAsync();
        var existingSlugs = existingTags.Select(t => t.Slug).ToHashSet();
        var missingSlugs = normalizedSlugs.Where(slug => !existingSlugs.Contains(slug)).ToList();

        var newTags = missingSlugs.Select(slug => new SnPostTag { Slug = slug }).ToList();
        if (newTags.Count > 0)
        {
            await db.PostTags.AddRangeAsync(newTags);
            await db.SaveChangesAsync();
        }

        var allTags = existingTags.Concat(newTags).ToList();

        foreach (var tag in allTags)
        {
            if (!tagService.IsTagAvailable(tag))
                throw new InvalidOperationException($"Tag '{tag.Slug}' is an event tag that has expired.");
            if (tag.IsProtected && tag.OwnerPublisherId is not null && publisher is not null && tag.OwnerPublisherId.Value != publisher.Id)
                throw new InvalidOperationException($"Tag '{tag.Slug}' is protected and can only be used by its owner.");
        }

        return allTags;
    }

    private async Task<List<SnPostCategory>> ResolveCategoriesAsync(IEnumerable<string> slugs)
    {
        var normalizedSlugs = NormalizeTopicSlugs(slugs);
        if (normalizedSlugs.Count == 0)
            return [];

        return await db.PostCategories.Where(e => normalizedSlugs.Contains(e.Slug)).ToListAsync();
    }

    private static string BuildInferenceText(SnPost post)
    {
        return string.Join(
            '\n',
            new[] { post.Title, post.Description, post.Content }.Where(x => !string.IsNullOrWhiteSpace(x))
        );
    }

    private async Task<List<string>> InferMatchingTopicSlugsAsync(
        string source,
        bool categories,
        int take = 5
    )
    {
        var normalizedSource = NormalizeTopicText(source);
        if (string.IsNullOrWhiteSpace(normalizedSource))
            return [];

        if (categories)
        {
            var candidates = await db.PostCategories.Select(x => new { x.Slug, x.Name }).ToListAsync();
            return candidates
                .Where(x =>
                    normalizedSource.Contains(NormalizeTopicText(x.Slug))
                    || (!string.IsNullOrWhiteSpace(x.Name)
                        && normalizedSource.Contains(NormalizeTopicText(x.Name)))
                )
                .Select(x => x.Slug)
                .Distinct()
                .Take(take)
                .ToList();
        }

        var tagCandidates = await db.PostTags.Select(x => new { x.Slug, x.Name }).ToListAsync();
        return tagCandidates
            .Where(x =>
                normalizedSource.Contains(NormalizeTopicText(x.Slug))
                || (!string.IsNullOrWhiteSpace(x.Name)
                    && normalizedSource.Contains(NormalizeTopicText(x.Name)))
            )
            .Select(x => x.Slug)
            .Distinct()
            .Take(take)
            .ToList();
    }

    private async Task<List<string>> GetDerivedPublisherTopicSlugsAsync(
        Guid publisherId,
        bool categories,
        int take = 3
    )
    {
        var posts = await db.Posts.Where(p => p.PublisherId == publisherId && p.DraftedAt == null)
            .Where(p => p.PublishedAt != null)
            .Include(p => p.Tags)
            .Include(p => p.Categories)
            .OrderByDescending(p => p.PublishedAt)
            .Take(40)
            .ToListAsync();

        var now = SystemClock.Instance.GetCurrentInstant();
        var scores = new Dictionary<string, double>();
        foreach (var post in posts)
        {
            var topicItems = categories ? post.Categories.Select(x => x.Slug) : post.Tags.Select(x => x.Slug);
            var topicSlugs = NormalizeTopicSlugs(topicItems);
            if (topicSlugs.Count == 0)
                continue;

            var ageDays = Math.Max(0, (now - (post.PublishedAt ?? post.CreatedAt)).TotalDays);
            var recencyWeight = Math.Exp(-ageDays / 30d);
            var engagementWeight =
                1d + Math.Max(0, post.ReactionScore) / 4d + Math.Max(0, post.RepliesCount) / 2d + (double)post.AwardedScore / 20d;
            var score = Math.Max(0.25d, recencyWeight * engagementWeight);

            foreach (var slug in topicSlugs)
                scores[slug] = scores.GetValueOrDefault(slug, 0d) + score;
        }

        return scores.OrderByDescending(x => x.Value).Select(x => x.Key).Take(take).ToList();
    }

    private async Task ApplyAutomaticTopicsAsync(
        SnPost post,
        List<string>? tags,
        List<string>? categories
    )
    {
        var sources = new List<string>();
        var publisher = await GetPublisherForPostAsync(post);
        var inferenceText = BuildInferenceText(post);

        if (tags is null && post.Tags.Count == 0)
        {
            var tagSlugs = await InferMatchingTopicSlugsAsync(inferenceText, categories: false);
            if (tagSlugs.Count > 0)
                sources.Add("content-tags");

            if (publisher is not null && tagSlugs.Count < 2)
            {
                var defaultTags = GetPublisherTopicDefaults(publisher, PublisherDefaultTagsMetaKey);
                if (defaultTags.Count > 0)
                    sources.Add("publisher-default-tags");
                tagSlugs = tagSlugs.Concat(defaultTags).Distinct().ToList();
            }

            if (post.PublisherId.HasValue && tagSlugs.Count < 2)
            {
                var derivedTags = await GetDerivedPublisherTopicSlugsAsync(
                    post.PublisherId.Value,
                    categories: false
                );
                if (derivedTags.Count > 0)
                    sources.Add("publisher-derived-tags");
                tagSlugs = tagSlugs.Concat(derivedTags).Distinct().ToList();
            }

            post.Tags = await ResolveTagsAsync(tagSlugs);
        }

        if (categories is null && post.Categories.Count == 0)
        {
            var categorySlugs = await InferMatchingTopicSlugsAsync(inferenceText, categories: true);
            if (categorySlugs.Count > 0)
                sources.Add("content-categories");

            if (publisher is not null && categorySlugs.Count < 2)
            {
                var defaultCategories = GetPublisherTopicDefaults(
                    publisher,
                    PublisherDefaultCategoriesMetaKey
                );
                if (defaultCategories.Count > 0)
                    sources.Add("publisher-default-categories");
                categorySlugs = categorySlugs.Concat(defaultCategories).Distinct().ToList();
            }

            if (post.PublisherId.HasValue && categorySlugs.Count < 2)
            {
                var derivedCategories = await GetDerivedPublisherTopicSlugsAsync(
                    post.PublisherId.Value,
                    categories: true
                );
                if (derivedCategories.Count > 0)
                    sources.Add("publisher-derived-categories");
                categorySlugs = categorySlugs.Concat(derivedCategories).Distinct().ToList();
            }

            post.Categories = await ResolveCategoriesAsync(categorySlugs);
        }

        if (sources.Count == 0)
            return;

        post.Metadata ??= new Dictionary<string, object>();
        post.Metadata[PostAutoTaggingMetaKey] = new Dictionary<string, object>
        {
            ["tags"] = post.Tags.Select(x => x.Slug).ToList(),
            ["categories"] = post.Categories.Select(x => x.Slug).ToList(),
            ["sources"] = sources.Distinct().ToList(),
            ["applied_at"] = DateTime.UtcNow.ToString("O"),
        };
    }

    public async Task ApplyInterestSignalsAsync(IReadOnlyList<PostInterestSignal> signals)
    {
        if (signals.Count == 0)
            return;

        var aggregatedSignals = signals.GroupBy(x => new { x.AccountId, x.PostId })
            .Select(g => new
            {
                g.Key.AccountId,
                g.Key.PostId,
                ScoreDelta = g.Sum(x => x.ScoreDelta),
                InteractionCount = g.Count(),
                LastInteractedAt = g.Max(x => x.OccurredAt),
                SignalType = g.OrderByDescending(x => x.OccurredAt).First().SignalType,
            })
            .ToList();

        var postIds = aggregatedSignals.Select(x => x.PostId).Distinct().ToList();
        var posts = await db.Posts.Where(p => postIds.Contains(p.Id))
            .Include(p => p.Tags)
            .Include(p => p.Categories)
            .ToDictionaryAsync(p => p.Id);

        var accountIds = aggregatedSignals.Select(x => x.AccountId).Distinct().ToList();
        var tagIds = posts.Values.SelectMany(p => p.Tags.Select(x => x.Id)).Distinct().ToList();
        var categoryIds = posts.Values.SelectMany(p => p.Categories.Select(x => x.Id)).Distinct().ToList();
        var publisherIds = posts.Values.Where(p => p.PublisherId.HasValue).Select(p => p.PublisherId!.Value).Distinct().ToList();

        var existingProfiles = await db.PostInterestProfiles.Where(p => accountIds.Contains(p.AccountId))
            .Where(p =>
                (p.Kind == PostInterestKind.Tag && tagIds.Contains(p.ReferenceId))
                || (p.Kind == PostInterestKind.Category && categoryIds.Contains(p.ReferenceId))
                || (p.Kind == PostInterestKind.Publisher && publisherIds.Contains(p.ReferenceId))
            )
            .ToListAsync();

        var profileMap = existingProfiles.ToDictionary(
            x => (x.AccountId, x.Kind, x.ReferenceId),
            x => x
        );

        foreach (var signal in aggregatedSignals)
        {
            if (!posts.TryGetValue(signal.PostId, out var post))
                continue;

            var targets = new List<(PostInterestKind Kind, Guid ReferenceId)>();
            targets.AddRange(post.Tags.Select(x => (PostInterestKind.Tag, x.Id)));
            targets.AddRange(post.Categories.Select(x => (PostInterestKind.Category, x.Id)));
            if (post.PublisherId.HasValue)
                targets.Add((PostInterestKind.Publisher, post.PublisherId.Value));

            foreach (var target in targets.Distinct())
            {
                var key = (signal.AccountId, target.Kind, target.ReferenceId);
                if (!profileMap.TryGetValue(key, out var profile))
                {
                    profile = new SnPostInterestProfile
                    {
                        AccountId = signal.AccountId,
                        Kind = target.Kind,
                        ReferenceId = target.ReferenceId,
                    };
                    profileMap[key] = profile;
                    db.PostInterestProfiles.Add(profile);
                }

                var adjustedDelta = AdjustInterestDeltaForTarget(
                    signal.ScoreDelta,
                    signal.SignalType,
                    target.Kind
                );
                profile.Score = ClampInterestScore(profile.Score + adjustedDelta);
                profile.InteractionCount += signal.InteractionCount;
                profile.LastInteractedAt = signal.LastInteractedAt;
                profile.LastSignalType = signal.SignalType;
            }
        }

        var newProfiles = profileMap.Values
            .Where(p => !existingProfiles.Contains(p))
            .ToList();

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            db.ChangeTracker.Clear();

            foreach (var profile in newProfiles)
            {
                await UpsertInterestProfileAsync(db, profile);
            }
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException pgEx && pgEx.SqlState == "23505";
    }

    private static async Task UpsertInterestProfileAsync(AppDatabase db, SnPostInterestProfile profile)
    {
        const string sql = """
            INSERT INTO post_interest_profiles (id, account_id, kind, reference_id, score, interaction_count, last_interacted_at, last_signal_type, created_at, updated_at)
            VALUES (@Id, @AccountId, @Kind, @ReferenceId, @Score, @InteractionCount, @LastInteractedAt, @LastSignalType, @CreatedAt, @UpdatedAt)
            ON CONFLICT (account_id, kind, reference_id) DO UPDATE SET
                score = EXCLUDED.score,
                interaction_count = EXCLUDED.interaction_count,
                last_interacted_at = EXCLUDED.last_interacted_at,
                last_signal_type = EXCLUDED.last_signal_type,
                updated_at = EXCLUDED.updated_at
            """;
        await db.Database.ExecuteSqlRawAsync(sql,
            new NpgsqlParameter("@Id", profile.Id),
            new NpgsqlParameter("@AccountId", profile.AccountId),
            new NpgsqlParameter("@Kind", (int)profile.Kind),
            new NpgsqlParameter("@ReferenceId", profile.ReferenceId),
            new NpgsqlParameter("@Score", profile.Score),
            new NpgsqlParameter("@InteractionCount", profile.InteractionCount),
            new NpgsqlParameter("@LastInteractedAt", profile.LastInteractedAt ?? (object)DBNull.Value),
            new NpgsqlParameter("@LastSignalType", profile.LastSignalType ?? (object)DBNull.Value),
            new NpgsqlParameter("@CreatedAt", profile.CreatedAt),
            new NpgsqlParameter("@UpdatedAt", profile.UpdatedAt));
    }

    private static List<SnPost> TruncatePostContent(List<SnPost> input)
    {
        const int maxLength = 256;
        const int embedMaxLength = 80;
        var parser = new HtmlParser();
        foreach (var item in input)
        {
            if (item.Content?.Length > maxLength)
            {
                string plainText;
                if (item.ContentType == PostContentType.Markdown)
                {
                    plainText = Markdown.ToPlainText(item.Content);
                }
                else if (item.ContentType == PostContentType.Html)
                {
                    var document = parser.ParseDocument(item.Content);
                    plainText = document.Body?.TextContent.Trim() ?? "";
                }
                else
                {
                    continue;
                }

                if (plainText.Length > maxLength)
                {
                    item.Content = plainText.Substring(0, maxLength);
                    item.IsTruncated = true;
                }
            }

            // Truncate replied post content with shorter embed length
            if (item.RepliedPost?.Content != null && item.Content?.Length > embedMaxLength)
            {
                string plainText;
                if (item.ContentType == PostContentType.Markdown)
                {
                    plainText = Markdown.ToPlainText(item.RepliedPost.Content);
                }
                else if (item.ContentType == PostContentType.Html)
                {
                    var document = parser.ParseDocument(item.RepliedPost.Content);
                    plainText = document.Body?.TextContent.Trim() ?? "";
                }
                else
                {
                    continue;
                }

                if (plainText.Length > embedMaxLength)
                {
                    item.RepliedPost.Content = plainText.Substring(0, embedMaxLength);
                    item.RepliedPost.IsTruncated = true;
                }
            }

            // Truncate forwarded post content with shorter embed length
            if (item.ForwardedPost?.Content != null && item.Content?.Length > embedMaxLength)
            {
                string plainText;
                if (item.ContentType == PostContentType.Markdown)
                {
                    plainText = Markdown.ToPlainText(item.ForwardedPost.Content);
                }
                else if (item.ContentType == PostContentType.Html)
                {
                    var document = parser.ParseDocument(item.ForwardedPost.Content);
                    plainText = document.Body?.TextContent.Trim() ?? "";
                }
                else
                {
                    continue;
                }

                if (plainText.Length > embedMaxLength)
                {
                    item.ForwardedPost.Content = plainText.Substring(0, embedMaxLength);
                    item.ForwardedPost.IsTruncated = true;
                }
            }
        }

        return input;
    }

    public (string title, string content) ChopPostForNotification(SnPost post)
    {
        var locale = CultureInfo.CurrentUICulture.Name;
        var content = !string.IsNullOrEmpty(post.Description)
            ? post.Description?.Length >= 40
                ? post.Description[..37] + "..."
                : post.Description
            : post.Content?.Length >= 100
                ? string.Concat(post.Content.AsSpan(0, 97), "...")
                : post.Content;
        var title =
            post.Title ?? (post.Content?.Length >= 10 ? post.Content[..10] + "..." : post.Content);
        title ??= localizer.Get("postOnlyMedia", locale: locale);
        if (string.IsNullOrWhiteSpace(content))
            content = localizer.Get("postOnlyMedia", locale: locale);
        return (title, content);
    }

    private async Task BroadcastPostUpdateAsync(SnPost post, string eventType)
    {
        using var scope = serviceProvider.CreateScope();
        var scopedWs = scope.ServiceProvider.GetRequiredService<RemoteWebSocketService>();
        var scopedPost = scope.ServiceProvider.GetRequiredService<PostService>();

        try
        {
            // Get all connected users
            var connectedUserIds = await scopedWs.GetAllConnectedUserIds();
            if (connectedUserIds.Count == 0)
                return;

            // Filter users based on visibility
            List<string> targetUserIds;
            if (post.Visibility == PostVisibility.Public)
            {
                // Public posts go to all connected users
                targetUserIds = connectedUserIds;
            }
            else
            {
                // For non-public posts, we need to filter based on visibility
                targetUserIds = await scopedPost.FilterUsersByPostVisibility(
                    post,
                    connectedUserIds
                );
            }

            if (targetUserIds.Count == 0)
                return;

            // Preload necessary post data
            post = await scopedPost.LoadPostInfo(post);

            // Serialize the post to JSON
            var postData = JsonSerializer.Serialize(
                post,
                InfraObjectCoder.SerializerOptionsWithoutIgnore
            );
            var postBytes = Encoding.UTF8.GetBytes(postData);

            // Push to all target users
            await scopedWs.PushWebSocketPacketToUsers(targetUserIds, eventType, postBytes);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error broadcasting post update for post {PostId}",
                post.Id.ToString()
            );
        }
    }

    private async Task BroadcastReactionUpdateAsync(SnPostReaction reaction, string eventType)
    {
        using var scope = serviceProvider.CreateScope();
        var scopedWs = scope.ServiceProvider.GetRequiredService<RemoteWebSocketService>();
        var scopedPost = scope.ServiceProvider.GetRequiredService<PostService>();
        var scopedDb = scope.ServiceProvider.GetRequiredService<AppDatabase>();

        try
        {
            // Get all connected users
            var onlineUsers = await scopedWs.GetAllConnectedUserIds();
            if (onlineUsers.Count == 0)
                return;

            // Get the post to check visibility
            var post = await scopedDb.Posts.FindAsync(reaction.PostId);
            if (post == null)
                return;

            // Filter users based on visibility
            List<string> targetUserIds;
            if (post.Visibility == PostVisibility.Public)
            {
                // Public posts go to all connected users
                targetUserIds = onlineUsers;
            }
            else
            {
                // For non-public posts, we need to filter based on visibility
                targetUserIds = await scopedPost.FilterUsersByPostVisibility(post, onlineUsers);
            }

            if (targetUserIds.Count == 0)
                return;

            // Serialize the reaction to JSON
            var reactionData = JsonSerializer.Serialize(
                reaction,
                InfraObjectCoder.SerializerOptionsWithoutIgnore
            );
            var reactionBytes = Encoding.UTF8.GetBytes(reactionData);

            // Push to all target users
            await scopedWs.PushWebSocketPacketToUsers(targetUserIds, eventType, reactionBytes);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error broadcasting reaction update for post {PostId}",
                reaction.PostId.ToString()
            );
        }
    }

    private async Task<List<string>> FilterUsersByPostVisibility(
        SnPost post,
        List<string> connectedUserIds
    )
    {
        var filteredUserIds = new List<string>();
        var publisherId = post.PublisherId;

        if (publisherId == null)
            return filteredUserIds;

        var publisherMembers = await ps.GetPublisherMembers(publisherId.Value);
        var memberAccountIds = publisherMembers.Select(m => m.AccountId.ToString()).ToHashSet();

        var postsRequireFollow = await ps.HasPostsRequireFollowFlag(publisherId.Value);
        HashSet<string>? followerAccountIds = null;
        if (postsRequireFollow)
        {
            var followerRequests = await db.PublisherFollowRequests
                .Where(r => r.PublisherId == publisherId.Value && r.State == FollowRequestState.Accepted)
                .Select(r => r.AccountId.ToString())
                .ToListAsync();
            followerAccountIds = followerRequests.ToHashSet();
        }

        HashSet<string>? friendAccountIds = null;
        if (post.Visibility == PostVisibility.Friends)
        {
            var queryRequest = new DyGetAccountBatchRequest();
            queryRequest.Id.AddRange(
                publisherMembers
                    .Where(m => m.AccountId != Guid.Empty)
                    .Select(m => m.AccountId.ToString())
            );
            var queryResponse = await accounts.GetAccountBatchAsync(queryRequest);

            friendAccountIds = new HashSet<string>();
            foreach (var member in queryResponse.Accounts)
            {
                if (member == null)
                    continue;
                var friendsResponse = await accounts.ListFriendsAsync(
                    new DyListRelationshipSimpleRequest { RelatedId = member.Id }
                );
                foreach (var friendId in friendsResponse.AccountsId)
                {
                    friendAccountIds.Add(friendId);
                }
            }
        }

        foreach (var userId in connectedUserIds)
        {
            var isMember = memberAccountIds.Contains(userId);

            switch (post.Visibility)
            {
                case PostVisibility.Private:
                {
                    if (isMember)
                        filteredUserIds.Add(userId);
                    continue;
                }
                case PostVisibility.Friends:
                {
                    if (isMember || (friendAccountIds != null && friendAccountIds.Contains(userId)))
                        filteredUserIds.Add(userId);
                    continue;
                }
                case PostVisibility.Unlisted:
                case PostVisibility.Public:
                {
                    if (postsRequireFollow)
                    {
                        if (isMember || (followerAccountIds != null && followerAccountIds.Contains(userId)))
                            filteredUserIds.Add(userId);
                    }
                    else
                    {
                        filteredUserIds.Add(userId);
                    }
                    continue;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        return filteredUserIds;
    }

    private List<string> ExtractMentions(string content)
    {
        if (string.IsNullOrEmpty(content))
            return new List<string>();

        var matches = GetMentionRegex().Matches(content);
        var mentions = new List<string>();

        foreach (Match match in matches)
        {
            var username = match.Groups[1].Value;
            if (
                !string.IsNullOrEmpty(username)
                && !mentions.Contains(username, StringComparer.OrdinalIgnoreCase)
            )
            {
                mentions.Add(username);
            }
        }

        return mentions;
    }

    private async Task SendMentionNotificationsAsync(SnPost post)
    {
        try
        {
            if (string.IsNullOrEmpty(post.Content))
                return;

            var mentions = ExtractMentions(post.Content);
            if (mentions.Count == 0)
                return;

            // Limit to 16 mentions maximum
            if (mentions.Count > 16)
            {
                logger.LogWarning(
                    "Post {PostId} has {MentionCount} mentions, limiting to 16",
                    post.Id,
                    mentions.Count
                );
                mentions = mentions.Take(16).ToList();
            }

            using var scope = factory.CreateScope();
            var scopedDb = scope.ServiceProvider.GetRequiredService<AppDatabase>();
            var nty = scope.ServiceProvider.GetRequiredService<DyRingService.DyRingServiceClient>();
            var accountsClient =
                scope.ServiceProvider.GetRequiredService<DyAccountService.DyAccountServiceClient>();

            var sender = post.Publisher;

            foreach (var username in mentions)
            {
                try
                {
                    // Find publisher by name/username
                    var mentionedPublisher = await scopedDb
                        .Publishers.Include(p => p.Members)
                        .FirstOrDefaultAsync(p => p.Name == username);

                    if (mentionedPublisher == null)
                        continue;
                    if (mentionedPublisher.IsShadowbanned)
                        continue;

                    // Get all member accounts of the mentioned publisher
                    var memberIds = mentionedPublisher
                        .Members.Select(m => m.AccountId.ToString())
                        .ToList();
                    if (memberIds.Count == 0)
                        continue;

                    // Get account details for language preferences
                    var queryRequest = new DyGetAccountBatchRequest();
                    queryRequest.Id.AddRange(memberIds);
                    var queryResponse = await accountsClient.GetAccountBatchAsync(queryRequest);

                    var (title, body) = ChopPostForNotification(post);

                    foreach (var member in queryResponse.Accounts)
                    {
                        if (member == null)
                            continue;

                        await nty.SendPushNotificationToUserAsync(
                            new DySendPushNotificationToUserRequest
                            {
                                UserId = member.Id,
                                Notification = new DyPushNotification
                                {
                                    Topic = "posts.mentions.new",
                                    Title = localizer.Get(
                                        "postMentionTitle",
                                        locale: member.Language,
                                        args: new { user = sender!.Nick }
                                    ),
                                    Body = localizer.Get(
                                        "postMentionBody",
                                        locale: member.Language,
                                        args: new { user = sender!.Nick, content = body }
                                    ),
                                    IsSavable = true,
                                    ActionUri = $"/posts/{post.Id}",
                                },
                            }
                        );
                    }

                    logger.LogInformation(
                        "Sent mention notification for post {PostId} to publisher {PublisherName} ({MemberCount} members)",
                        post.Id,
                        username,
                        memberIds.Count
                    );
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Error sending mention notification to {Username} for post {PostId}",
                        username,
                        post.Id
                    );
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error processing mention notifications for post {PostId}",
                post.Id
            );
        }
    }

    public async Task<SnPost> PostAsync(
        SnPost post,
        List<string>? attachments = null,
        List<string>? tags = null,
        List<string>? categories = null,
        DyAccount? actor = null
    )
    {
        if (post.Empty)
            throw new InvalidOperationException("Cannot create a post with barely no content.");

        if (post.DraftedAt is not null)
        {
            if (post.PublishedAt is not null)
                throw new InvalidOperationException("Cannot set both draftedAt and publishedAt.");
        }
        else if (post.PublishedAt is not null)
        {
            if (post.PublishedAt.Value.ToDateTimeUtc() < DateTime.UtcNow)
                throw new InvalidOperationException(
                    "Cannot create the post which published in the past."
                );
        }
        else
        {
            post.PublishedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);
        }

        if (attachments is not null)
        {
            var queryRequest = new DyGetFileBatchRequest();
            queryRequest.Ids.AddRange(attachments);
            var queryResponse = await files.GetFileBatchAsync(queryRequest);

            post.Attachments = queryResponse
                .Files.Select(SnCloudFileReferenceObject.FromProtoValue)
                .ToList();
            // Re-order the list to match the id list places
            post.Attachments = attachments
                .Select(id => post.Attachments.First(a => a.Id == id))
                .ToList();
        }

        if (tags is not null)
        {
            var publisher = await GetPublisherForPostAsync(post);
            post.Tags = await ResolveTagsAsync(tags, publisher);
        }

        if (categories is not null)
        {
            post.Categories = await ResolveCategoriesAsync(categories);
            if (post.Categories.Count != NormalizeTopicSlugs(categories).Count)
                throw new InvalidOperationException(
                    "Categories contains one or more categories that wasn't exists."
                );
        }

        await ApplyAutomaticTopicsAsync(post, tags, categories);

        db.Posts.Add(post);
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var isPublishedNow =
            post.DraftedAt is null
            && post.PublishedAt is not null
            && post.PublishedAt.Value.ToDateTimeUtc() <= now;

        if (
            isPublishedNow
        )
            _ = Task.Run(async () =>
            {
                using var scope = factory.CreateScope();
                var pubSub =
                    scope.ServiceProvider.GetRequiredService<PublisherSubscriptionService>();
                await pubSub.NotifySubscriberPost(post);
            });

        if (
            isPublishedNow
            && post.RepliedPost is not null
        )
        {
            _ = Task.Run(async () =>
            {
                var sender = post.Publisher;
                using var scope = factory.CreateScope();
                var pub = scope.ServiceProvider.GetRequiredService<PublisherService>();
                var nty = scope.ServiceProvider.GetRequiredService<DyRingService.DyRingServiceClient>();
                var notifyTargets =
                    scope.ServiceProvider.GetRequiredService<DyAccountService.DyAccountServiceClient>();
                try
                {
                    var members = await pub.GetPublisherMembers(
                        post.RepliedPost.PublisherId!.Value
                    );
                    var queryRequest = new DyGetAccountBatchRequest();
                    queryRequest.Id.AddRange(members.Select(m => m.AccountId.ToString()));
                    var queryResponse = await notifyTargets.GetAccountBatchAsync(queryRequest);
                    foreach (var member in queryResponse.Accounts)
                    {
                        if (member is null)
                            continue;
                        await nty.SendPushNotificationToUserAsync(
                            new DySendPushNotificationToUserRequest
                            {
                                UserId = member.Id,
                                Notification = new DyPushNotification
                                {
                                    Topic = "post.replies",
                                    Title = localizer.Get(
                                        "postReplyTitle",
                                        locale: member.Language,
                                        args: new { user = sender!.Nick }
                                    ),
                                    Body = ChopPostForNotification(post).content,
                                    IsSavable = true,
                                    ActionUri = $"/posts/{post.Id}",
                                },
                            }
                        );
                    }
                }
                catch (Exception err)
                {
                    logger.LogError(
                        $"Error when sending post reactions notification: {err.Message} {err.StackTrace}"
                    );
                }
            });
        }

        if (isPublishedNow && post.RepliedPostId.HasValue && actor is not null)
        {
            flushBuffer.Enqueue(
                new PostInterestSignal
                {
                    AccountId = Guid.Parse(actor.Id),
                    PostId = post.RepliedPostId.Value,
                    ScoreDelta = ReplyInterestScore,
                    SignalType = "reply",
                    OccurredAt = Instant.FromDateTimeUtc(DateTime.UtcNow),
                }
            );
        }

        if (isPublishedNow)
        {
            // Send mention notifications in the background
            _ = Task.Run(async () => await SendMentionNotificationsAsync(post));

            // Process link preview in the background to avoid delaying post creation
            _ = Task.Run(async () => await CreateLinkPreviewAsync(post));
        }

        // Send ActivityPub Create activity in background for public posts
        if (
            isPublishedNow
            && post.Visibility == PostVisibility.Public
        )
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = factory.CreateScope();
                    var deliveryService =
                        scope.ServiceProvider.GetRequiredService<ActivityPubDeliveryService>();
                    await deliveryService.SendCreateActivityAsync(post);
                }
                catch (Exception err)
                {
                    logger.LogError(
                        $"Error when sending ActivityPub Create activity: {err.Message}"
                    );
                }
            });
        }

        if (isPublishedNow)
        {
            // Broadcast real-time update to connected clients
            _ = Task.Run(async () => await BroadcastPostUpdateAsync(post, "post.created"));
        }

        return post;
    }

    public async Task<SnPost> UpdatePostAsync(
        SnPost post,
        List<string>? attachments = null,
        List<string>? tags = null,
        List<string>? categories = null,
        Instant? draftedAt = null,
        Instant? publishedAt = null
    )
    {
        if (post.Empty)
            throw new InvalidOperationException("Cannot edit a post to barely no content.");

        post.EditedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);
        var now = SystemClock.Instance.GetCurrentInstant();

        if (draftedAt is not null && publishedAt is not null)
            throw new InvalidOperationException("Cannot set both draftedAt and publishedAt.");

        if (publishedAt is not null)
        {
            // User cannot set the published at to the past to prevent scam,
            // But we can just let the controller set the published at, because when no changes to
            // the published at will blocked the update operation
            if (publishedAt.Value < now)
            {
                if (post.DraftedAt is not null)
                    publishedAt = now;
                else
                    throw new InvalidOperationException("Cannot set the published at to the past.");
            }

            post.PublishedAt = publishedAt;
            post.DraftedAt = null;
        }

        if (draftedAt is not null)
        {
            post.DraftedAt = draftedAt;
            post.PublishedAt = null;
        }

        var entity = db.Entry(post);
        var previousDraftedAt = entity.Property(e => e.DraftedAt).OriginalValue;
        var previousPublishedAt = entity.Property(e => e.PublishedAt).OriginalValue;

        var wasPublished =
            previousDraftedAt is null
            && previousPublishedAt is not null
            && previousPublishedAt.Value <= now;
        var isPublished =
            post.DraftedAt is null && post.PublishedAt is not null && post.PublishedAt.Value <= now;

        if (attachments is not null)
        {
            // Update post attachments by getting files from database
            var queryRequest = new DyGetFileBatchRequest();
            queryRequest.Ids.AddRange(attachments);
            var queryResponse = await files.GetFileBatchAsync(queryRequest);

            post.Attachments = queryResponse
                .Files.Select(SnCloudFileReferenceObject.FromProtoValue)
                .ToList();
        }

        if (tags is not null)
        {
            var publisher = await GetPublisherForPostAsync(post);
            post.Tags = await ResolveTagsAsync(tags, publisher);
        }

        if (categories is not null)
        {
            post.Categories = await ResolveCategoriesAsync(categories);
            if (post.Categories.Count != NormalizeTopicSlugs(categories).Count)
                throw new InvalidOperationException(
                    "Categories contains one or more categories that wasn't exists."
                );
        }

        await ApplyAutomaticTopicsAsync(post, tags, categories);

        db.Update(post);
        await db.SaveChangesAsync();

        if (!wasPublished && isPublished)
        {
            _ = Task.Run(async () =>
            {
                using var scope = factory.CreateScope();
                var pubSub = scope.ServiceProvider.GetRequiredService<PublisherSubscriptionService>();
                await pubSub.NotifySubscriberPost(post);
            });

            if (post.RepliedPost is not null)
            {
                _ = Task.Run(async () =>
                {
                    var sender = post.Publisher;
                    using var scope = factory.CreateScope();
                    var pub = scope.ServiceProvider.GetRequiredService<PublisherService>();
                    var nty =
                        scope.ServiceProvider.GetRequiredService<DyRingService.DyRingServiceClient>();
                    var notifyTargets =
                        scope.ServiceProvider.GetRequiredService<DyAccountService.DyAccountServiceClient>();
                    try
                    {
                        var members = await pub.GetPublisherMembers(post.RepliedPost.PublisherId!.Value);
                        var queryRequest = new DyGetAccountBatchRequest();
                        queryRequest.Id.AddRange(members.Select(m => m.AccountId.ToString()));
                        var queryResponse = await notifyTargets.GetAccountBatchAsync(queryRequest);
                        foreach (var member in queryResponse.Accounts)
                        {
                            if (member is null)
                                continue;
                            await nty.SendPushNotificationToUserAsync(
                                new DySendPushNotificationToUserRequest
                                {
                                    UserId = member.Id,
                                    Notification = new DyPushNotification
                                    {
                                        Topic = "post.replies",
                                        Title = localizer.Get(
                                            "postReplyTitle",
                                            locale: member.Language,
                                            args: new { user = sender!.Nick }
                                        ),
                                        Body = ChopPostForNotification(post).content,
                                        IsSavable = true,
                                        ActionUri = $"/posts/{post.Id}",
                                    },
                                }
                            );
                        }
                    }
                    catch (Exception err)
                    {
                        logger.LogError(
                            $"Error when sending post reactions notification: {err.Message} {err.StackTrace}"
                        );
                    }
                });
            }

            // Send mention notifications in the background on publish.
            _ = Task.Run(async () => await SendMentionNotificationsAsync(post));
        }

        if (isPublished)
        {
            // Process link preview in the background to avoid delaying post update
            _ = Task.Run(async () => await CreateLinkPreviewAsync(post));
        }

        // Send ActivityPub activity in background for published public posts
        if (isPublished && post.Visibility == PostVisibility.Public)
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = factory.CreateScope();
                    var deliveryService =
                        scope.ServiceProvider.GetRequiredService<ActivityPubDeliveryService>();
                    if (wasPublished)
                        await deliveryService.SendUpdateActivityAsync(post);
                    else
                        await deliveryService.SendCreateActivityAsync(post);
                }
                catch (Exception err)
                {
                    logger.LogError(
                        $"Error when sending ActivityPub Update activity: {err.Message}"
                    );
                }
            });

        if (isPublished)
        {
            // Broadcast real-time update to connected clients
            _ = Task.Run(
                async () =>
                    await BroadcastPostUpdateAsync(post, wasPublished ? "post.updated" : "post.created")
            );
        }

        return post;
    }

    [GeneratedRegex(@"https?://(?!.*\.\w{1,6}(?:[#?]|$))[^\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex GetLinkRegex();

    [GeneratedRegex(@"@(\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex GetMentionRegex();

    private async Task<SnPost> PreviewPostLinkAsync(SnPost item)
    {
        if (item.Type != PostType.Moment || string.IsNullOrEmpty(item.Content))
            return item;

        // Find all URLs in the content
        var matches = GetLinkRegex().Matches(item.Content);

        if (matches.Count == 0)
            return item;

        // Initialize meta dictionary if null
        item.Metadata ??= new Dictionary<string, object>();
        if (
            !item.Metadata.TryGetValue("embeds", out var existingEmbeds)
            || existingEmbeds is not List<EmbeddableBase>
        )
            item.Metadata["embeds"] = new List<Dictionary<string, object>>();
        var embeds = (List<Dictionary<string, object>>)item.Metadata["embeds"];

        // Process up to 3 links to avoid excessive processing
        const int maxLinks = 3;
        var processedLinks = 0;
        foreach (Match match in matches)
        {
            if (processedLinks >= maxLinks)
                break;

            var url = match.Value;

            try
            {
                // Check if this URL is already in the embed list
                var urlAlreadyEmbedded = embeds.Any(e =>
                    e.TryGetValue("Url", out var originalUrl) && (string)originalUrl == url
                );
                if (urlAlreadyEmbedded)
                    continue;

                // Preview the link
                var linkEmbed = await reader.GetLinkPreview(url);
                embeds.Add(EmbeddableBase.ToDictionary(linkEmbed));
                processedLinks++;
            }
            catch
            {
                // ignored
            }
        }

        item.Metadata["embeds"] = embeds;
        return item;
    }

    /// <summary>
    /// Process link previews for a post in background
    /// This method is designed to be called from a background task
    /// </summary>
    /// <param name="post">The post to process link previews for</param>
    private async Task CreateLinkPreviewAsync(SnPost post)
    {
        try
        {
            // Create a new scope for database operations
            using var scope = factory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDatabase>();

            // Preview the links in the post
            var updatedPost = await PreviewPostLinkAsync(post);

            // If embeds were added, update the post in the database
            if (
                updatedPost.Metadata != null
                && updatedPost.Metadata.TryGetValue("embeds", out var embeds)
                && embeds is List<Dictionary<string, object>> { Count: > 0 } embedsList
            )
            {
                // Get a fresh copy of the post from the database
                var dbPost = await dbContext.Posts.FindAsync(post.Id);
                if (dbPost != null)
                {
                    // Update the metadata field with the new embeds
                    dbPost.Metadata ??= new Dictionary<string, object>();
                    dbPost.Metadata["embeds"] = embedsList;

                    // Save changes to the database
                    dbContext.Update(dbPost);
                    await dbContext.SaveChangesAsync();

                    logger.LogDebug(
                        "Updated post {PostId} with {EmbedCount} link previews",
                        post.Id,
                        embedsList.Count
                    );
                }
            }
        }
        catch (Exception ex)
        {
            // Log errors but don't rethrow - this is a background task
            logger.LogError(ex, "Error processing link previews for post {PostId}", post.Id);
        }
    }

    public async Task DeletePostAsync(SnPost post)
    {
        // Broadcast deletion before removing from database
        _ = Task.Run(async () => await BroadcastPostUpdateAsync(post, "post.deleted"));

        var now = SystemClock.Instance.GetCurrentInstant();
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await db
                .PostReactions.Where(r => r.PostId == post.Id)
                .ExecuteUpdateAsync(p => p.SetProperty(x => x.DeletedAt, now));
            await db
                .Posts.Where(p => p.RepliedPostId == post.Id)
                .ExecuteUpdateAsync(p => p.SetProperty(x => x.RepliedGone, true));
            await db
                .Posts.Where(p => p.ForwardedPostId == post.Id)
                .ExecuteUpdateAsync(p => p.SetProperty(x => x.ForwardedGone, true));

            db.Posts.Remove(post);
            await db.SaveChangesAsync();

            await transaction.CommitAsync();
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }

        // Send ActivityPub Delete activity in background for public posts
        if (post.Visibility == PostVisibility.Public)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = factory.CreateScope();
                    var deliveryService =
                        scope.ServiceProvider.GetRequiredService<ActivityPubDeliveryService>();
                    await deliveryService.SendDeleteActivityAsync(post);
                }
                catch (Exception err)
                {
                    logger.LogError(
                        $"Error when sending ActivityPub Delete activity: {err.Message}"
                    );
                }
            });
        }
    }

    public async Task<SnPost> PinPostAsync(
        SnPost post,
        DyAccount currentUser,
        PostPinMode pinMode
    )
    {
        var accountId = Guid.Parse(currentUser.Id);
        if (post.RepliedPostId != null)
        {
            if (pinMode != PostPinMode.ReplyPage)
                throw new InvalidOperationException(
                    "Replies can only be pinned in the reply page."
                );
            if (post.RepliedPost == null)
                throw new ArgumentNullException(nameof(post.RepliedPost));

            if (
                !await ps.IsMemberWithRole(
                    post.RepliedPost.PublisherId!.Value,
                    accountId,
                    PublisherMemberRole.Editor
                )
            )
                throw new InvalidOperationException(
                    "Only editors of original post can pin replies."
                );

            post.PinMode = pinMode;
        }
        else
        {
            if (
                post.PublisherId == null
                || !await ps.IsMemberWithRole(
                    post.PublisherId.Value,
                    accountId,
                    PublisherMemberRole.Editor
                )
            )
                throw new InvalidOperationException("Only editors can pin replies.");

            post.PinMode = pinMode;
        }

        db.Update(post);
        await db.SaveChangesAsync();

        return post;
    }

    public async Task<SnPost> UnpinPostAsync(SnPost post, DyAccount currentUser)
    {
        var accountId = Guid.Parse(currentUser.Id);
        if (post.RepliedPostId != null)
        {
            if (post.RepliedPost == null)
                throw new ArgumentNullException(nameof(post.RepliedPost));

            if (
                !await ps.IsMemberWithRole(
                    post.RepliedPost.PublisherId!.Value,
                    accountId,
                    PublisherMemberRole.Editor
                )
            )
                throw new InvalidOperationException(
                    "Only editors of original post can unpin replies."
                );
        }
        else
        {
            if (
                post.PublisherId == null
                || !await ps.IsMemberWithRole(
                    post.PublisherId.Value,
                    accountId,
                    PublisherMemberRole.Editor
                )
            )
                throw new InvalidOperationException("Only editors can unpin posts.");
        }

        post.PinMode = null;
        db.Update(post);
        await db.SaveChangesAsync();

        return post;
    }

    /// <summary>
    /// Calculate the total number of votes for a post.
    /// This function helps you save the new reactions.
    /// </summary>
    /// <param name="post">Post that modifying</param>
    /// <param name="reaction">The new / target reaction adding / removing</param>
    /// <param name="op">The original poster account of this post</param>
    /// <param name="isRemoving">Indicate this operation is adding / removing</param>
    /// <param name="isSelfReact">Indicate this reaction is by the original post himself</param>
    /// <param name="sender">The account that creates this reaction</param>
    public async Task<bool> ModifyPostVotes(
        SnPost post,
        SnPostReaction reaction,
        DyAccount sender,
        bool isRemoving,
        bool isSelfReact
    )
    {
        var hasMatchingReaction =
            reaction.AccountId.HasValue
            && await db.Set<SnPostReaction>().AnyAsync(r =>
                r.PostId == post.Id
                && r.Symbol == reaction.Symbol
                && r.AccountId == reaction.AccountId.Value
            );

        if (isRemoving)
        {
            if (!hasMatchingReaction)
                return true;

            await db
                .PostReactions.Where(r =>
                    r.PostId == post.Id
                    && r.Symbol == reaction.Symbol
                    && reaction.AccountId.HasValue
                    && r.AccountId == reaction.AccountId.Value
                )
                .ExecuteDeleteAsync();
        }
        else if (!hasMatchingReaction)
        {
            db.PostReactions.Add(reaction);
        }

        if (!hasMatchingReaction && isRemoving)
            return true;

        if (!isSelfReact && hasMatchingReaction == isRemoving)
        {
            var weight = GetReactionWeight(reaction.Attitude);
            var reactionDelta = isRemoving ? -weight : weight;
            post.ReactionScore += reactionDelta;

            switch (reaction.Attitude)
            {
                case PostReactionAttitude.Positive:
                    post.Upvotes += isRemoving ? -1 : 1;
                    break;
                case PostReactionAttitude.Negative:
                    post.Downvotes += isRemoving ? -1 : 1;
                    break;
            }
        }

        await db.SaveChangesAsync();

        // Broadcast reaction update to connected clients
        _ = Task.Run(async () =>
            await BroadcastReactionUpdateAsync(
                reaction,
                isRemoving ? "post.reaction.removed" : "post.reaction.added"
            )
        );

        if (isSelfReact)
            return isRemoving;

        if (reaction.AccountId.HasValue && hasMatchingReaction == isRemoving)
        {
            var interestDelta = isRemoving
                ? -GetReactionWeight(reaction.Attitude)
                : GetReactionWeight(reaction.Attitude);
            flushBuffer.Enqueue(
                new PostInterestSignal
                {
                    AccountId = reaction.AccountId.Value,
                    PostId = post.Id,
                    ScoreDelta = interestDelta,
                    SignalType = $"reaction:{reaction.Attitude.ToString().ToLowerInvariant()}",
                    OccurredAt = Instant.FromDateTimeUtc(DateTime.UtcNow),
                }
            );
        }

        // Send ActivityPub Like/Undo activities if post's publisher has actor
        if (post.PublisherId.HasValue)
        {
            var accountId = Guid.Parse(sender.Id);
            SnPublisher? accountPublisher = null;
            var settings = await db.PublishingSettings
                .FirstOrDefaultAsync(s => s.AccountId == accountId);
            if (settings?.DefaultFediversePublisherId != null)
            {
                accountPublisher = await db.Publishers
                    .Where(p => p.Id == settings.DefaultFediversePublisherId && p.Members.Any(m => m.AccountId == accountId))
                    .FirstOrDefaultAsync();
            }
            if (accountPublisher == null)
            {
                accountPublisher = await db
                    .Publishers.Where(p => p.Members.Any(m => m.AccountId == accountId))
                    .FirstOrDefaultAsync();
            }
            var accountActor = accountPublisher is null
                ? null
                : await objFactory.GetLocalActorAsync(accountPublisher.Id);
            var publisherActor = await objFactory.GetLocalActorAsync(post.PublisherId.Value);

            if (accountActor != null && publisherActor != null)
            {
                var emoji = GetEmojiForSymbol(reaction.Symbol);
                if (emoji != null)
                {
                    if (!isRemoving)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                using var scope = factory.CreateScope();
                                var deliveryService =
                                    scope.ServiceProvider.GetRequiredService<ActivityPubDeliveryService>();
                                var activityId = $"{accountActor.Uri}/reactions/{Guid.NewGuid()}";
                                await deliveryService.SendEmojiReactionActivityAsync(
                                    accountActor,
                                    post.Id,
                                    emoji,
                                    publisherActor,
                                    activityId
                                );
                                reaction.FediverseUri = activityId;
                            }
                            catch (Exception ex)
                            {
                                logger.LogError($"Error sending ActivityPub EmojiReact: {ex.Message}");
                            }
                        });
                    }
                    else
                    {
                        var existingReaction = await db.PostReactions
                            .FirstOrDefaultAsync(r =>
                                r.PostId == post.Id
                                && r.Symbol == reaction.Symbol
                                && r.AccountId == reaction.AccountId
                            );
                        var undoActivityId = existingReaction?.FediverseUri;
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                using var scope = factory.CreateScope();
                                var deliveryService =
                                    scope.ServiceProvider.GetRequiredService<ActivityPubDeliveryService>();
                                await deliveryService.SendUndoEmojiReactionActivityAsync(
                                    accountActor,
                                    post.Id,
                                    emoji,
                                    publisherActor,
                                    undoActivityId ?? ""
                                );
                            }
                            catch (Exception ex)
                            {
                                logger.LogError($"Error sending ActivityPub Undo EmojiReact: {ex.Message}");
                            }
                        });
                    }
                }
            }
            else
            {
                logger.LogWarning(
                    "Seems {PublisherName} didn't enable actor, skipping delivery of EmojiReact activity...",
                    accountPublisher?.Name
                );
            }
        }

        _ = Task.Run(async () =>
        {
            using var scope = factory.CreateScope();
            var pub = scope.ServiceProvider.GetRequiredService<PublisherService>();
            var nty = scope.ServiceProvider.GetRequiredService<DyRingService.DyRingServiceClient>();
            var accounts =
                scope.ServiceProvider.GetRequiredService<DyAccountService.DyAccountServiceClient>();
            try
            {
                if (post.PublisherId == null)
                    return;
                var members = await pub.GetPublisherMembers(post.PublisherId.Value);
                var queryRequest = new DyGetAccountBatchRequest();
                queryRequest.Id.AddRange(members.Select(m => m.AccountId.ToString()));
                var queryResponse = await accounts.GetAccountBatchAsync(queryRequest);
                foreach (var member in queryResponse.Accounts)
                {
                    if (member is null)
                        continue;

                    await nty.SendPushNotificationToUserAsync(
                        new DySendPushNotificationToUserRequest
                        {
                            UserId = member.Id,
                            Notification = new DyPushNotification
                            {
                                Topic = "posts.reactions.new",
                                Title = localizer.Get(
                                    "postReactTitle",
                                    locale: member.Language,
                                    args: new { user = sender.Nick }
                                ),
                                Body = string.IsNullOrWhiteSpace(post.Title)
                                    ? localizer.Get(
                                        "postReactBody",
                                        locale: member.Language,
                                        args: new { user = sender.Nick, reaction = reaction.Symbol }
                                    )
                                    : localizer.Get(
                                        "postReactContentBody",
                                        locale: member.Language,
                                        args: new
                                        {
                                            user = sender.Nick,
                                            reaction = reaction.Symbol,
                                            title = post.Title,
                                        }
                                    ),
                                IsSavable = true,
                                ActionUri = $"/posts/{post.Id}",
                            },
                        }
                    );
                }
            }
            catch (Exception ex)
            {
                logger.LogError(
                    $"Error when sending post reactions notification: {ex.Message} {ex.StackTrace}"
                );
            }
        });

        return isRemoving;
    }

    public async Task<Dictionary<string, int>> GetPostReactionMap(Guid postId)
    {
        return await db.Set<SnPostReaction>()
            .Where(r => r.PostId == postId)
            .GroupBy(r => r.Symbol)
            .ToDictionaryAsync(g => g.Key, g => g.Count());
    }

    public async Task<Dictionary<Guid, Dictionary<string, int>>> GetPostReactionMapBatch(
        List<Guid> postIds
    )
    {
        return await db.Set<SnPostReaction>()
            .Where(r => postIds.Contains(r.PostId))
            .GroupBy(r => r.PostId)
            .ToDictionaryAsync(
                g => g.Key,
                g => g.GroupBy(r => r.Symbol).ToDictionary(sg => sg.Key, sg => sg.Count())
            );
    }

    private async Task<Dictionary<Guid, Dictionary<string, bool>>> GetPostReactionMadeMapBatch(
        List<Guid> postIds,
        Guid accountId
    )
    {
        var reactions = await db.Set<SnPostReaction>()
            .Where(r => postIds.Contains(r.PostId) && r.AccountId == accountId)
            .Select(r => new { r.PostId, r.Symbol })
            .ToListAsync();

        return postIds.ToDictionary(
            postId => postId,
            postId =>
                reactions.Where(r => r.PostId == postId).ToDictionary(r => r.Symbol, _ => true)
        );
    }

    /// <summary>
    /// Increases the view count for a post.
    /// Uses the flush buffer service to batch database updates for better performance.
    /// </summary>
    /// <param name="postId">The ID of the post to mark as viewed</param>
    /// <param name="viewerId">Optional viewer ID for unique view counting (anonymous if null)</param>
    /// <returns>Task representing the asynchronous operation</returns>
    public async Task IncreaseViewCount(Guid postId, string? viewerId = null, bool isDetailView = false)
    {
        // Check if this view is already counted in cache to prevent duplicate counting
        if (!string.IsNullOrEmpty(viewerId))
        {
            var cacheKey = $"post:view:{postId}:{viewerId}";
            var (found, _) = await cache.GetAsyncWithStatus<bool>(cacheKey);

            if (found)
            {
                // Already viewed by this user recently, don't count again
                return;
            }

            // Mark as viewed in cache for 1 hour to prevent duplicate counting
            await cache.SetAsync(cacheKey, true, TimeSpan.FromHours(1));
        }

        // Always increment view count
        flushBuffer.Enqueue(
            new PostViewInfo
            {
                PostId = postId,
                ViewerId = viewerId,
                ViewedAt = Instant.FromDateTimeUtc(DateTime.UtcNow),
            }
        );

        // Only fire interest signal on detail page views
        if (isDetailView && !string.IsNullOrEmpty(viewerId) && Guid.TryParse(viewerId, out var accountId))
        {
            var interestCacheKey =
                $"post:interest:view:{postId}:{viewerId}:{DateTime.UtcNow:yyyyMMdd}";
            var (interestFound, _) = await cache.GetAsyncWithStatus<bool>(interestCacheKey);
            if (!interestFound)
            {
                await cache.SetAsync(interestCacheKey, true, TimeSpan.FromDays(1));
                flushBuffer.Enqueue(
                    new PostInterestSignal
                    {
                        AccountId = accountId,
                        PostId = postId,
                        ScoreDelta = ViewInterestScore,
                        SignalType = "view",
                        OccurredAt = Instant.FromDateTimeUtc(DateTime.UtcNow),
                    }
                );
            }
        }
    }

    private async Task<List<SnPost>> LoadPubsAndActors(List<SnPost> posts)
    {
        var publisherIds = posts
            .SelectMany<SnPost, Guid?>(e =>
                [e.PublisherId, e.RepliedPost?.PublisherId, e.ForwardedPost?.PublisherId]
            )
            .Where(e => e != null)
            .Distinct()
            .ToList();
        var actorIds = posts
            .SelectMany<SnPost, Guid?>(e =>
                [e.ActorId, e.RepliedPost?.ActorId, e.ForwardedPost?.ActorId]
            )
            .Where(e => e != null)
            .Distinct()
            .ToList();
        if (publisherIds.Count == 0 && actorIds.Count == 0)
            return posts;

        var publishers = await db
            .Publishers.Where(e => publisherIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id);

        var actors = await db
            .FediverseActors.Include(e => e.Instance)
            .Where(e => actorIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id);

        foreach (var post in posts)
        {
            if (
                post.PublisherId.HasValue
                && publishers.TryGetValue(post.PublisherId.Value, out var publisher)
            )
                post.Publisher = publisher;

            if (post.ActorId.HasValue && actors.TryGetValue(post.ActorId.Value, out var actor))
                post.Actor = actor;

            if (
                post.RepliedPost?.PublisherId != null
                && publishers.TryGetValue(
                    post.RepliedPost.PublisherId.Value,
                    out var repliedPublisher
                )
            )
                post.RepliedPost.Publisher = repliedPublisher;

            if (
                post.RepliedPost?.ActorId != null
                && actors.TryGetValue(post.RepliedPost.ActorId.Value, out var repliedActor)
            )
                post.RepliedPost.Actor = repliedActor;

            if (
                post.ForwardedPost?.PublisherId != null
                && publishers.TryGetValue(
                    post.ForwardedPost.PublisherId.Value,
                    out var forwardedPublisher
                )
            )
                post.ForwardedPost.Publisher = forwardedPublisher;

            if (
                post.ForwardedPost?.ActorId != null
                && actors.TryGetValue(post.ForwardedPost.ActorId.Value, out var forwardedActor)
            )
                post.ForwardedPost.Actor = forwardedActor;
        }

        await ps.LoadIndividualPublisherAccounts(publishers.Values);

        return posts;
    }

    private async Task<List<SnPost>> LoadInteractive(
        List<SnPost> posts,
        DyAccount? currentUser = null
    )
    {
        if (posts.Count == 0)
            return posts;

        var postsId = posts.Select(e => e.Id).ToList();

        var reactionMaps = await GetPostReactionMapBatch(postsId);
        var reactionMadeMap = currentUser is not null
            ? await GetPostReactionMadeMapBatch(postsId, Guid.Parse(currentUser.Id))
            : new Dictionary<Guid, Dictionary<string, bool>>();
        var repliesCountMap = await GetPostRepliesCountBatch(postsId);
        var threadRepliesCountMap = await GetPostThreadRepliesCountBatch(postsId);

        // Load user friends if the current user exists
        List<SnPublisher> publishers = [];
        List<Guid> userFriends = [];
        if (currentUser is not null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(
                new DyListRelationshipSimpleRequest { AccountId = currentUser.Id }
            );
            userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
            publishers = await ps.GetUserPublishers(Guid.Parse(currentUser.Id));
        }

        foreach (var post in posts)
        {
            // Set reaction count
            post.ReactionsCount = reactionMaps.TryGetValue(post.Id, out var count)
                ? count
                : new Dictionary<string, int>();

            // Set reaction made status
            post.ReactionsMade = reactionMadeMap.TryGetValue(post.Id, out var made) ? made : [];

            // Set reply count
            post.RepliesCount = repliesCountMap.GetValueOrDefault(post.Id, 0);
            post.ThreadRepliesCount = threadRepliesCountMap.GetValueOrDefault(post.Id, 0);

            // Check visibility for replied post
            if (post.RepliedPost != null)
            {
                if (!CanViewPost(post.RepliedPost, currentUser, publishers, userFriends))
                {
                    post.RepliedPost = null;
                    post.RepliedGone = true;
                }
            }

            // Check visibility for forwarded post
            if (post.ForwardedPost != null)
            {
                if (!CanViewPost(post.ForwardedPost, currentUser, publishers, userFriends))
                {
                    post.ForwardedPost = null;
                    post.ForwardedGone = true;
                }
            }

            // Track view for each post in the list (without interest signal)
            if (currentUser != null)
                await IncreaseViewCount(post.Id, currentUser.Id);
            else
                await IncreaseViewCount(post.Id);
        }

        return posts;
    }

    private bool CanViewPost(
        SnPost post,
        DyAccount? currentUser,
        List<SnPublisher> publishers,
        List<Guid> userFriends
    )
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var publishersId = publishers.Select(e => e.Id).ToList();

        // Check if post is deleted
        if (post.DeletedAt != null)
            return false;

        if (currentUser is null)
        {
            // Anonymous user can only view public posts that are published
            return post.DraftedAt is null
                && post.PublishedAt != null
                && now >= post.PublishedAt
                && post.Visibility == PostVisibility.Public;
        }

        // Check publication status - either published or user is member
        var isPublished =
            post.DraftedAt is null && post.PublishedAt != null && now >= post.PublishedAt;
        var isMember = post.PublisherId.HasValue && publishersId.Contains(post.PublisherId.Value);
        if (!isPublished && !isMember)
            return false;

        // Check visibility
        if (post.Visibility == PostVisibility.Private && !isMember)
            return false;

        if (
            post.Visibility == PostVisibility.Friends
            && !(
                post.Publisher is not null
                    && post.Publisher.AccountId.HasValue
                    && userFriends.Contains(post.Publisher.AccountId.Value)
                || isMember
            )
        )
            return false;

        // Public and Unlisted are allowed
        return true;
    }

    private async Task<Dictionary<Guid, int>> GetPostRepliesCountBatch(List<Guid> postIds)
    {
        return await db
            .Posts.Where(p => p.RepliedPostId != null && postIds.Contains(p.RepliedPostId.Value))
            .GroupBy(p => p.RepliedPostId!.Value)
            .ToDictionaryAsync(g => g.Key, g => g.Count());
    }

    private async Task<Dictionary<Guid, int>> GetPostThreadRepliesCountBatch(List<Guid> postIds)
    {
        if (postIds.Count == 0)
            return [];

        var postIdsParameter = new NpgsqlParameter<Guid[]>("postIds", postIds.ToArray());

        var results = await db.Database
            .SqlQueryRaw<ThreadReplyCountResult>(
                """
                WITH RECURSIVE reply_tree AS (
                    SELECT replied_post_id AS ancestor_id, id AS descendant_id
                    FROM posts
                    WHERE replied_post_id = ANY (@postIds) AND deleted_at IS NULL
                    UNION ALL
                    SELECT reply_tree.ancestor_id, posts.id AS descendant_id
                    FROM posts
                    INNER JOIN reply_tree ON posts.replied_post_id = reply_tree.descendant_id
                    WHERE posts.deleted_at IS NULL
                )
                SELECT ancestor_id, COUNT(*)::int AS count
                FROM reply_tree
                GROUP BY ancestor_id
                """,
                postIdsParameter
            )
            .ToListAsync();

        return results.ToDictionary(x => x.AncestorId, x => x.Count);
    }

    public async Task<List<SnPost>> LoadPostInfo(
        List<SnPost> posts,
        DyAccount? currentUser = null,
        bool truncate = false
    )
    {
        if (posts.Count == 0)
            return posts;

        posts = await LoadPubsAndActors(posts);
        posts = await LoadInteractive(posts, currentUser);

        if (truncate)
            posts = TruncatePostContent(posts);

        return posts;
    }

    public async Task<SnPost> LoadPostInfo(
        SnPost post,
        DyAccount? currentUser = null,
        bool truncate = false
    )
    {
        // Convert single post to list, process it, then return the single post
        var posts = await LoadPostInfo([post], currentUser, truncate);
        return posts.First();
    }

    private const string FeaturedPostCacheKey = "posts:featured";

    public async Task<List<SnPost>> ListFeaturedPostsAsync(DyAccount? currentUser = null)
    {
        // Check cache first for featured post IDs
        var featuredIds = await cache.GetAsync<List<Guid>>(FeaturedPostCacheKey);

        if (featuredIds is null)
        {
            // The previous day the highest rated posts
            var today = SystemClock.Instance.GetCurrentInstant();
            var periodStart = today
                .InUtc()
                .Date.AtStartOfDayInZone(DateTimeZone.Utc)
                .ToInstant()
                .Minus(Duration.FromDays(1));
            var periodEnd = today.InUtc().Date.AtStartOfDayInZone(DateTimeZone.Utc).ToInstant();

            var postsInPeriod = await db
                .Posts.Where(e => e.Visibility == PostVisibility.Public)
                .Where(e => e.CreatedAt >= periodStart && e.CreatedAt < periodEnd)
                .Where(e => e.FediverseUri == null)
                .Where(e => e.Publisher == null || (e.Publisher.GatekeptFollows != true && (e.Publisher.ShadowbanReason == null || e.Publisher.ShadowbanReason == PublisherShadowbanReason.None)))
                .Where(e => e.ShadowbanReason == null || e.ShadowbanReason == PostShadowbanReason.None)
                .Select(e => e.Id)
                .ToListAsync();

            var reactionScores = await db
                .PostReactions.Where(e => postsInPeriod.Contains(e.PostId))
                .GroupBy(e => e.PostId)
                .Select(e => new
                {
                    PostId = e.Key,
                    Score = e.Sum(r =>
                        r.Attitude == PostReactionAttitude.Positive ? 1 : -1
                    ),
                })
                .ToDictionaryAsync(e => e.PostId, e => e.Score);

            var repliesCounts = await db
                .Posts.Where(p =>
                    p.RepliedPostId != null && postsInPeriod.Contains(p.RepliedPostId.Value)
                )
                .GroupBy(p => p.RepliedPostId!.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            // Load awardsScores for postsInPeriod
            var awardsScores = await db
                .Posts.Where(p => postsInPeriod.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.AwardedScore);

            var reactSocialPoints = postsInPeriod
                .Select(postId => new
                {
                    PostId = postId,
                    Count = (reactionScores.GetValueOrDefault(postId, 0))
                        + (repliesCounts.GetValueOrDefault(postId, 0))
                        + (
                            awardsScores.TryGetValue(postId, out var awardScore)
                                ? (int)(awardScore / 10)
                                : 0
                        ),
                })
                .OrderByDescending(e => e.Count)
                .Take(5)
                .ToDictionary(e => e.PostId, e => e.Count);

            featuredIds = reactSocialPoints.Select(e => e.Key).ToList();

            await cache.SetAsync(FeaturedPostCacheKey, featuredIds, TimeSpan.FromHours(4));

            // Create featured record
            var existingFeaturedPostIds = await db
                .PostFeaturedRecords.Where(r => featuredIds.Contains(r.PostId))
                .Select(r => r.PostId)
                .ToListAsync();

            var records = reactSocialPoints
                .Where(p => !existingFeaturedPostIds.Contains(p.Key))
                .Select(e => new SnPostFeaturedRecord { PostId = e.Key, SocialCredits = e.Value })
                .ToList();

            if (records.Count != 0)
            {
                db.PostFeaturedRecords.AddRange(records);
                await db.SaveChangesAsync();

                var featuredPosts = await db.Posts
                    .Where(p => records.Select(r => r.PostId).Contains(p.Id))
                    .Include(p => p.Publisher)
                    .ToListAsync();

                foreach (var featuredPost in featuredPosts.Where(p => p.Publisher?.AccountId is not null))
                {
                    var record = records.First(r => r.PostId == featuredPost.Id);
                    actionLogs.CreateActionLog(
                        featuredPost.Publisher!.AccountId!.Value,
                        ActionLogType.PostFeatured,
                        new Dictionary<string, object>
                        {
                            ["post_id"] = featuredPost.Id,
                            ["publisher_id"] = featuredPost.PublisherId ?? Guid.Empty,
                            ["social_credits"] = record.SocialCredits
                        }
                    );
                }
            }
        }

        var posts = await db
            .Posts.Where(e => featuredIds.Contains(e.Id))
            .Include(e => e.ForwardedPost)
            .Include(e => e.RepliedPost)
            .Include(e => e.Categories)
            .Include(e => e.Publisher)
            .Include(e => e.FeaturedRecords)
            .Where(e => e.Publisher == null || e.Publisher.GatekeptFollows != true)
            .Take(featuredIds.Count)
            .ToListAsync();
        posts = posts.OrderBy(e => featuredIds.IndexOf(e.Id)).ToList();
        posts = await LoadPostInfo(posts, currentUser, true);

        return posts;
    }

    public async Task<SnPostAward> AwardPost(
        Guid postId,
        Guid accountId,
        decimal amount,
        PostReactionAttitude attitude,
        string? message
    )
    {
        var post = await db.Posts.Where(p => p.Id == postId).FirstOrDefaultAsync();
        if (post is null)
            throw new InvalidOperationException("Post not found");

        var award = new SnPostAward
        {
            Amount = amount,
            Attitude = attitude,
            Message = message,
            PostId = postId,
            AccountId = accountId,
        };

        db.PostAwards.Add(award);
        await db.SaveChangesAsync();

        var delta =
            award.Attitude == PostReactionAttitude.Positive ? amount : -amount;

        await db
            .Posts.Where(p => p.Id == postId)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(p => p.AwardedScore, p => p.AwardedScore + delta)
            );

        _ = Task.Run(async () =>
        {
            using var scope = factory.CreateScope();
            var pub = scope.ServiceProvider.GetRequiredService<PublisherService>();
            var nty = scope.ServiceProvider.GetRequiredService<DyRingService.DyRingServiceClient>();
            var accounts =
                scope.ServiceProvider.GetRequiredService<DyAccountService.DyAccountServiceClient>();
            var accountsHelper = scope.ServiceProvider.GetRequiredService<RemoteAccountService>();
            try
            {
                var sender = await accountsHelper.GetAccount(accountId);

                if (post.PublisherId == null)
                    return;
                var members = await pub.GetPublisherMembers(post.PublisherId.Value);
                var queryRequest = new DyGetAccountBatchRequest();
                queryRequest.Id.AddRange(members.Select(m => m.AccountId.ToString()));
                var queryResponse = await accounts.GetAccountBatchAsync(queryRequest);
                foreach (var member in queryResponse.Accounts)
                {
                    if (member is null)
                        continue;

                    await nty.SendPushNotificationToUserAsync(
                        new DySendPushNotificationToUserRequest
                        {
                            UserId = member.Id,
                            Notification = new DyPushNotification
                            {
                                Topic = "posts.awards.new",
                                Title = localizer.Get(
                                    "postAwardedTitle",
                                    locale: member.Language,
                                    args: new { user = sender.Nick }
                                ),
                                Body = string.IsNullOrWhiteSpace(post.Title)
                                    ? localizer.Get(
                                        "postAwardedBody",
                                        locale: member.Language,
                                        args: new { user = sender.Nick, amount }
                                    )
                                    : localizer.Get(
                                        "postAwardedContentBody",
                                        locale: member.Language,
                                        args: new
                                        {
                                            user = sender.Nick,
                                            amount,
                                            title = post.Title,
                                        }
                                    ),
                                IsSavable = true,
                                ActionUri = $"/posts/{post.Id}",
                            },
                        }
                    );
                }
            }
            catch (Exception ex)
            {
                logger.LogError(
                    $"Error when sending post awarded notification: {ex.Message} {ex.StackTrace}"
                );
            }
        });

        return award;
    }

    public enum PostVisibilityResult
    {
        Visible,
        NotVisible,
        Gatekept,
    }

    public async Task<PostVisibilityResult> CheckPostVisibilityAsync(SnPost post, DyAccount? currentUser)
    {
        var now = SystemClock.Instance.GetCurrentInstant();

        if (post.DraftedAt != null)
        {
            if (currentUser == null)
                return PostVisibilityResult.NotVisible;

            if (post.Publisher?.AccountId == null || post.Publisher.AccountId.Value != Guid.Parse(currentUser.Id))
                return PostVisibilityResult.NotVisible;
        }

        if (post.PublishedAt == null || post.PublishedAt > now)
            return PostVisibilityResult.NotVisible;

        if (post.Visibility == PostVisibility.Private)
        {
            if (currentUser == null)
                return PostVisibilityResult.NotVisible;

            if (post.Publisher?.AccountId == null || post.Publisher.AccountId.Value != Guid.Parse(currentUser.Id))
                return PostVisibilityResult.NotVisible;
        }

        if (post.Visibility == PostVisibility.Friends)
        {
            if (currentUser == null)
                return PostVisibilityResult.NotVisible;

            if (post.Publisher?.AccountId != null && post.Publisher.AccountId.Value == Guid.Parse(currentUser.Id))
                return PostVisibilityResult.Visible;
        }

        if (post.PublisherId.HasValue)
        {
            var publisher = post.Publisher;
            if (publisher == null)
            {
                publisher = await db.Publishers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == post.PublisherId.Value);
            }

            if (publisher?.IsGatekept == true)
            {
                if (currentUser == null)
                    return PostVisibilityResult.Gatekept;

                var accountId = Guid.Parse(currentUser.Id);
                var isSubscribed = await db.PublisherSubscriptions
                    .AnyAsync(s =>
                        s.PublisherId == post.PublisherId.Value &&
                        s.AccountId == accountId &&
                        s.EndedAt == null
                    );

                if (!isSubscribed)
                    return PostVisibilityResult.Gatekept;
            }
        }

        return PostVisibilityResult.Visible;
    }

    public void EnsurePostVisible(SnPost post, DyAccount? currentUser)
    {
        var result = CheckPostVisibilityAsync(post, currentUser).GetAwaiter().GetResult();
        switch (result)
        {
            case PostVisibilityResult.NotVisible:
                throw new InvalidOperationException("Post is not visible to this user");
            case PostVisibilityResult.Gatekept:
                throw new InvalidOperationException("Post is from a gatekept publisher and user is not subscribed");
        }
    }
}

public static class PostQueryExtensions
{
    public static IQueryable<SnPost> FilterWithVisibility(
        this IQueryable<SnPost> source,
        DyAccount? currentUser,
        List<Guid> userFriends,
        List<SnPublisher> publishers,
        bool isListing = false,
        HashSet<Guid>? gatekeptPublisherIds = null,
        HashSet<Guid>? followerPublisherIds = null
    )
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var publishersId = publishers.Select(e => e.Id).ToList();

        if (isListing)
            source = source.Where(e => e.DraftedAt == null);

        source = isListing switch
        {
            true when currentUser is not null => source.Where(e =>
                e.Visibility != Shared.Models.PostVisibility.Unlisted
                || (e.PublisherId.HasValue && publishersId.Contains(e.PublisherId.Value))
            ),
            true => source.Where(e => e.Visibility != Shared.Models.PostVisibility.Unlisted),
            _ => source,
        };

        if (currentUser is null)
        {
            if (gatekeptPublisherIds != null && gatekeptPublisherIds.Count > 0)
            {
                source = source.Where(e =>
                    !(e.PublisherId.HasValue && gatekeptPublisherIds.Contains(e.PublisherId.Value))
                );
            }
            return source
                .Where(e => e.DraftedAt == null)
                .Where(e => e.PublishedAt != null && now >= e.PublishedAt)
                .Where(e => e.Visibility == Shared.Models.PostVisibility.Public);
        }

        var result = source
            .Where(e =>
                (e.DraftedAt == null && e.PublishedAt != null && now >= e.PublishedAt)
                || (e.PublisherId.HasValue && publishersId.Contains(e.PublisherId.Value))
            )
            .Where(e =>
                e.Visibility != Shared.Models.PostVisibility.Private
                || publishersId.Contains(e.PublisherId!.Value)
            )
            .Where(e =>
                e.Visibility != Shared.Models.PostVisibility.Friends
                || (
                    e.Publisher!.AccountId != null
                    && userFriends.Contains(e.Publisher.AccountId.Value)
                )
                || publishersId.Contains(e.PublisherId!.Value)
            );

        if (gatekeptPublisherIds != null && gatekeptPublisherIds.Count > 0 && followerPublisherIds != null)
        {
            result = result.Where(e =>
                !(e.PublisherId.HasValue && gatekeptPublisherIds.Contains(e.PublisherId.Value))
                || publishersId.Contains(e.PublisherId.Value)
                || followerPublisherIds.Contains(e.PublisherId.Value)
            );
        }

        return result;
    }
}
