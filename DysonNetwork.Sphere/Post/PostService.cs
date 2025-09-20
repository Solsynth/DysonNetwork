using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Common;
using DysonNetwork.Shared;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Sphere.WebReader;
using DysonNetwork.Sphere.Localization;
using DysonNetwork.Sphere.Poll;
using DysonNetwork.Sphere.Publisher;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using NodaTime;

namespace DysonNetwork.Sphere.Post;

public partial class PostService(
    AppDatabase db,
    IStringLocalizer<NotificationResource> localizer,
    IServiceScopeFactory factory,
    FlushBufferService flushBuffer,
    ICacheService cache,
    ILogger<PostService> logger,
    FileService.FileServiceClient files,
    FileReferenceService.FileReferenceServiceClient fileRefs,
    PollService polls,
    Publisher.PublisherService ps,
    WebReaderService reader
)
{
    private const string PostFileUsageIdentifier = "post";

    private static List<Post> TruncatePostContent(List<Post> input)
    {
        const int maxLength = 256;
        const int embedMaxLength = 80;
        foreach (var item in input)
        {
            if (item.Content?.Length > maxLength)
            {
                item.Content = item.Content[..maxLength];
                item.IsTruncated = true;
            }

            // Truncate replied post content with shorter embed length
            if (item.RepliedPost?.Content?.Length > embedMaxLength)
            {
                item.RepliedPost.Content = item.RepliedPost.Content[..embedMaxLength];
                item.RepliedPost.IsTruncated = true;
            }

            // Truncate forwarded post content with shorter embed length
            if (item.ForwardedPost?.Content?.Length > embedMaxLength)
            {
                item.ForwardedPost.Content = item.ForwardedPost.Content[..embedMaxLength];
                item.ForwardedPost.IsTruncated = true;
            }
        }

        return input;
    }

    public (string title, string content) ChopPostForNotification(Post post)
    {
        var content = !string.IsNullOrEmpty(post.Description)
            ? post.Description?.Length >= 40 ? post.Description[..37] + "..." : post.Description
            : post.Content?.Length >= 100
                ? string.Concat(post.Content.AsSpan(0, 97), "...")
                : post.Content;
        var title = post.Title ?? (post.Content?.Length >= 10 ? post.Content[..10] + "..." : post.Content);
        content ??= localizer["PostOnlyMedia"];
        title ??= localizer["PostOnlyMedia"];
        return (title, content);
    }

    public async Task<Post> PostAsync(
        Post post,
        List<string>? attachments = null,
        List<string>? tags = null,
        List<string>? categories = null
    )
    {
        if (post.Empty)
            throw new InvalidOperationException("Cannot create a post with barely no content.");

        if (post.PublishedAt is not null)
        {
            if (post.PublishedAt.Value.ToDateTimeUtc() < DateTime.UtcNow)
                throw new InvalidOperationException("Cannot create the post which published in the past.");
        }
        else
        {
            post.PublishedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);
        }

        if (attachments is not null)
        {
            var queryRequest = new GetFileBatchRequest();
            queryRequest.Ids.AddRange(attachments);
            var queryResponse = await files.GetFileBatchAsync(queryRequest);

            post.Attachments = queryResponse.Files.Select(CloudFileReferenceObject.FromProtoValue).ToList();
            // Re-order the list to match the id list places
            post.Attachments = attachments
                .Select(id => post.Attachments.First(a => a.Id == id))
                .ToList();
        }

        if (tags is not null)
        {
            var existingTags = await db.PostTags.Where(e => tags.Contains(e.Slug)).ToListAsync();

            // Determine missing slugs
            var existingSlugs = existingTags.Select(t => t.Slug).ToHashSet();
            var missingSlugs = tags.Where(slug => !existingSlugs.Contains(slug)).ToList();

            var newTags = missingSlugs.Select(slug => new PostTag { Slug = slug }).ToList();
            if (newTags.Count > 0)
            {
                await db.PostTags.AddRangeAsync(newTags);
                await db.SaveChangesAsync();
            }

            post.Tags = existingTags.Concat(newTags).ToList();
        }

        if (categories is not null)
        {
            post.Categories = await db.PostCategories.Where(e => categories.Contains(e.Slug)).ToListAsync();
            if (post.Categories.Count != categories.Distinct().Count())
                throw new InvalidOperationException("Categories contains one or more categories that wasn't exists.");
        }

        db.Posts.Add(post);
        await db.SaveChangesAsync();

        // Create file references for each attachment
        if (post.Attachments.Count != 0)
        {
            var request = new CreateReferenceBatchRequest
            {
                Usage = PostFileUsageIdentifier,
                ResourceId = post.ResourceIdentifier,
            };
            request.FilesId.AddRange(post.Attachments.Select(a => a.Id));
            await fileRefs.CreateReferenceBatchAsync(request);
        }

        if (post.PublishedAt is not null && post.PublishedAt.Value.ToDateTimeUtc() <= DateTime.UtcNow)
            _ = Task.Run(async () =>
            {
                using var scope = factory.CreateScope();
                var pubSub = scope.ServiceProvider.GetRequiredService<PublisherSubscriptionService>();
                await pubSub.NotifySubscriberPost(post);
            });

        if (post.PublishedAt is not null && post.PublishedAt.Value.ToDateTimeUtc() <= DateTime.UtcNow &&
            post.RepliedPost is not null)
        {
            _ = Task.Run(async () =>
            {
                var sender = post.Publisher;
                using var scope = factory.CreateScope();
                var pub = scope.ServiceProvider.GetRequiredService<Publisher.PublisherService>();
                var nty = scope.ServiceProvider.GetRequiredService<RingService.RingServiceClient>();
                var accounts = scope.ServiceProvider.GetRequiredService<AccountService.AccountServiceClient>();
                try
                {
                    var members = await pub.GetPublisherMembers(post.RepliedPost.PublisherId);
                    var queryRequest = new GetAccountBatchRequest();
                    queryRequest.Id.AddRange(members.Select(m => m.AccountId.ToString()));
                    var queryResponse = await accounts.GetAccountBatchAsync(queryRequest);
                    foreach (var member in queryResponse.Accounts)
                    {
                        if (member is null) continue;
                        CultureService.SetCultureInfo(member);
                        await nty.SendPushNotificationToUserAsync(
                            new SendPushNotificationToUserRequest
                            {
                                UserId = member.Id,
                                Notification = new PushNotification
                                {
                                    Topic = "post.replies",
                                    Title = localizer["PostReplyTitle", sender.Nick],
                                    Body = string.IsNullOrWhiteSpace(post.Title)
                                        ? localizer["PostReplyBody", sender.Nick, ChopPostForNotification(post).content]
                                        : localizer["PostReplyContentBody", sender.Nick, post.Title,
                                            ChopPostForNotification(post).content],
                                    IsSavable = true,
                                    ActionUri = $"/posts/{post.Id}"
                                }
                            }
                        );
                    }
                }
                catch (Exception err)
                {
                    logger.LogError($"Error when sending post reactions notification: {err.Message} {err.StackTrace}");
                }
            });
        }

        // Process link preview in the background to avoid delaying post creation
        _ = Task.Run(async () => await ProcessPostLinkPreviewAsync(post));

        return post;
    }

    public async Task<Post> UpdatePostAsync(
        Post post,
        List<string>? attachments = null,
        List<string>? tags = null,
        List<string>? categories = null,
        Instant? publishedAt = null
    )
    {
        if (post.Empty)
            throw new InvalidOperationException("Cannot edit a post to barely no content.");

        post.EditedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);

        if (publishedAt is not null)
        {
            // User cannot set the published at to the past to prevent scam,
            // But we can just let the controller set the published at, because when no changes to
            // the published at will blocked the update operation
            if (publishedAt.Value.ToDateTimeUtc() < DateTime.UtcNow)
                throw new InvalidOperationException("Cannot set the published at to the past.");
        }

        if (attachments is not null)
        {
            var postResourceId = $"post:{post.Id}";

            // Update resource references using the new file list
            var request = new UpdateResourceFilesRequest
            {
                ResourceId = postResourceId,
                Usage = PostFileUsageIdentifier,
            };
            request.FileIds.AddRange(attachments);
            await fileRefs.UpdateResourceFilesAsync(request);

            // Update post attachments by getting files from database
            var queryRequest = new GetFileBatchRequest();
            queryRequest.Ids.AddRange(attachments);
            var queryResponse = await files.GetFileBatchAsync(queryRequest);

            post.Attachments = queryResponse.Files.Select(CloudFileReferenceObject.FromProtoValue).ToList();
        }

        if (tags is not null)
        {
            var existingTags = await db.PostTags.Where(e => tags.Contains(e.Slug)).ToListAsync();

            // Determine missing slugs
            var existingSlugs = existingTags.Select(t => t.Slug).ToHashSet();
            var missingSlugs = tags.Where(slug => !existingSlugs.Contains(slug)).ToList();

            var newTags = missingSlugs.Select(slug => new PostTag { Slug = slug }).ToList();
            if (newTags.Count > 0)
            {
                await db.PostTags.AddRangeAsync(newTags);
                await db.SaveChangesAsync();
            }

            post.Tags = existingTags.Concat(newTags).ToList();
        }

        if (categories is not null)
        {
            post.Categories = await db.PostCategories.Where(e => categories.Contains(e.Slug)).ToListAsync();
            if (post.Categories.Count != categories.Distinct().Count())
                throw new InvalidOperationException("Categories contains one or more categories that wasn't exists.");
        }

        db.Update(post);
        await db.SaveChangesAsync();

        // Process link preview in the background to avoid delaying post update
        _ = Task.Run(async () => await ProcessPostLinkPreviewAsync(post));

        return post;
    }

    [GeneratedRegex(@"https?://(?!.*\.\w{1,6}(?:[#?]|$))[^\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex GetLinkRegex();

    public async Task<Post> PreviewPostLinkAsync(Post item)
    {
        if (item.Type != PostType.Moment || string.IsNullOrEmpty(item.Content)) return item;

        // Find all URLs in the content
        var matches = GetLinkRegex().Matches(item.Content);

        if (matches.Count == 0)
            return item;

        // Initialize meta dictionary if null
        item.Meta ??= new Dictionary<string, object>();

        // Initialize the embeds' array if it doesn't exist
        if (!item.Meta.TryGetValue("embeds", out var existingEmbeds) || existingEmbeds is not List<EmbeddableBase>)
        {
            item.Meta["embeds"] = new List<Dictionary<string, object>>();
        }

        var embeds = (List<Dictionary<string, object>>)item.Meta["embeds"];

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
                    e.TryGetValue("Url", out var originalUrl) && (string)originalUrl == url);
                if (urlAlreadyEmbedded)
                    continue;

                // Preview the link
                var linkEmbed = await reader.GetLinkPreviewAsync(url);
                embeds.Add(EmbeddableBase.ToDictionary(linkEmbed));
                processedLinks++;
            }
            catch
            {
                // ignored
            }
        }

        item.Meta["embeds"] = embeds;

        return item;
    }

    /// <summary>
    /// Process link previews for a post in the background
    /// This method is designed to be called from a background task
    /// </summary>
    /// <param name="post">The post to process link previews for</param>
    private async Task ProcessPostLinkPreviewAsync(Post post)
    {
        try
        {
            // Create a new scope for database operations
            using var scope = factory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDatabase>();

            // Preview the links in the post
            var updatedPost = await PreviewPostLinkAsync(post);

            // If embeds were added, update the post in the database
            if (updatedPost.Meta != null &&
                updatedPost.Meta.TryGetValue("embeds", out var embeds) &&
                embeds is List<Dictionary<string, object>> { Count: > 0 } embedsList)
            {
                // Get a fresh copy of the post from the database
                var dbPost = await dbContext.Posts.FindAsync(post.Id);
                if (dbPost != null)
                {
                    // Update the meta field with the new embeds
                    dbPost.Meta ??= new Dictionary<string, object>();
                    dbPost.Meta["embeds"] = embedsList;

                    // Save changes to the database
                    dbContext.Update(dbPost);
                    await dbContext.SaveChangesAsync();

                    logger.LogDebug("Updated post {PostId} with {EmbedCount} link previews", post.Id, embedsList.Count);
                }
            }
        }
        catch (Exception ex)
        {
            // Log errors but don't rethrow - this is a background task
            logger.LogError(ex, "Error processing link previews for post {PostId}", post.Id);
        }
    }

    public async Task DeletePostAsync(Post post)
    {
        // Delete all file references for this post
        await fileRefs.DeleteResourceReferencesAsync(
            new DeleteResourceReferencesRequest { ResourceId = post.ResourceIdentifier }
        );

        var now = SystemClock.Instance.GetCurrentInstant();
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await db.PostReactions
                .Where(r => r.PostId == post.Id)
                .ExecuteUpdateAsync(p => p.SetProperty(x => x.DeletedAt, now));
            await db.Posts
                .Where(p => p.RepliedPostId == post.Id)
                .ExecuteUpdateAsync(p => p.SetProperty(x => x.RepliedGone, true));
            await db.Posts
                .Where(p => p.ForwardedPostId == post.Id)
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
    }

    public async Task<Post> PinPostAsync(Post post, Account currentUser, PostPinMode pinMode)
    {
        var accountId = Guid.Parse(currentUser.Id);
        if (post.RepliedPostId != null)
        {
            if (pinMode != PostPinMode.ReplyPage)
                throw new InvalidOperationException("Replies can only be pinned in the reply page.");
            if (post.RepliedPost == null) throw new ArgumentNullException(nameof(post.RepliedPost));

            if (!await ps.IsMemberWithRole(post.RepliedPost.PublisherId, accountId,
                    Publisher.PublisherMemberRole.Editor))
                throw new InvalidOperationException("Only editors of original post can pin replies.");

            post.PinMode = pinMode;
        }
        else
        {
            if (!await ps.IsMemberWithRole(post.PublisherId, accountId, Publisher.PublisherMemberRole.Editor))
                throw new InvalidOperationException("Only editors can pin replies.");

            post.PinMode = pinMode;
        }

        db.Update(post);
        await db.SaveChangesAsync();

        return post;
    }

    public async Task<Post> UnpinPostAsync(Post post, Account currentUser)
    {
        var accountId = Guid.Parse(currentUser.Id);
        if (post.RepliedPostId != null)
        {
            if (post.RepliedPost == null) throw new ArgumentNullException(nameof(post.RepliedPost));

            if (!await ps.IsMemberWithRole(post.RepliedPost.PublisherId, accountId,
                    Publisher.PublisherMemberRole.Editor))
                throw new InvalidOperationException("Only editors of original post can unpin replies.");
        }
        else
        {
            if (!await ps.IsMemberWithRole(post.PublisherId, accountId, Publisher.PublisherMemberRole.Editor))
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
        Post post,
        PostReaction reaction,
        Account sender,
        bool isRemoving,
        bool isSelfReact
    )
    {
        var isExistingReaction = await db.Set<PostReaction>()
            .AnyAsync(r => r.PostId == post.Id && r.AccountId == reaction.AccountId);

        if (isRemoving)
            await db.PostReactions
                .Where(r => r.PostId == post.Id && r.Symbol == reaction.Symbol && r.AccountId == reaction.AccountId)
                .ExecuteDeleteAsync();
        else
            db.PostReactions.Add(reaction);

        if (isExistingReaction)
        {
            if (!isRemoving)
                await db.SaveChangesAsync();
            return isRemoving;
        }

        if (isSelfReact)
        {
            await db.SaveChangesAsync();
            return isRemoving;
        }

        switch (reaction.Attitude)
        {
            case PostReactionAttitude.Positive:
                if (isRemoving) post.Upvotes--;
                else post.Upvotes++;
                break;
            case PostReactionAttitude.Negative:
                if (isRemoving) post.Downvotes--;
                else post.Downvotes++;
                break;
        }

        await db.SaveChangesAsync();

        if (!isSelfReact)
            _ = Task.Run(async () =>
            {
                using var scope = factory.CreateScope();
                var pub = scope.ServiceProvider.GetRequiredService<Publisher.PublisherService>();
                var nty = scope.ServiceProvider.GetRequiredService<RingService.RingServiceClient>();
                var accounts = scope.ServiceProvider.GetRequiredService<AccountService.AccountServiceClient>();
                try
                {
                    var members = await pub.GetPublisherMembers(post.PublisherId);
                    var queryRequest = new GetAccountBatchRequest();
                    queryRequest.Id.AddRange(members.Select(m => m.AccountId.ToString()));
                    var queryResponse = await accounts.GetAccountBatchAsync(queryRequest);
                    foreach (var member in queryResponse.Accounts)
                    {
                        if (member is null) continue;
                        CultureService.SetCultureInfo(member);

                        await nty.SendPushNotificationToUserAsync(
                            new SendPushNotificationToUserRequest
                            {
                                UserId = member.Id,
                                Notification = new PushNotification
                                {
                                    Topic = "posts.reactions.new",
                                    Title = localizer["PostReactTitle", sender.Nick],
                                    Body = string.IsNullOrWhiteSpace(post.Title)
                                        ? localizer["PostReactBody", sender.Nick, reaction.Symbol]
                                        : localizer["PostReactContentBody", sender.Nick, reaction.Symbol,
                                            post.Title],
                                    IsSavable = true,
                                    ActionUri = $"/posts/{post.Id}"
                                }
                            }
                        );
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError($"Error when sending post reactions notification: {ex.Message} {ex.StackTrace}");
                }
            });

        return isRemoving;
    }

    public async Task<Dictionary<string, int>> GetPostReactionMap(Guid postId)
    {
        return await db.Set<PostReaction>()
            .Where(r => r.PostId == postId)
            .GroupBy(r => r.Symbol)
            .ToDictionaryAsync(
                g => g.Key,
                g => g.Count()
            );
    }

    public async Task<Dictionary<Guid, Dictionary<string, int>>> GetPostReactionMapBatch(List<Guid> postIds)
    {
        return await db.Set<PostReaction>()
            .Where(r => postIds.Contains(r.PostId))
            .GroupBy(r => r.PostId)
            .ToDictionaryAsync(
                g => g.Key,
                g => g.GroupBy(r => r.Symbol)
                    .ToDictionary(
                        sg => sg.Key,
                        sg => sg.Count()
                    )
            );
    }

    public async Task<Dictionary<Guid, Dictionary<string, bool>>> GetPostReactionMadeMapBatch(List<Guid> postIds,
        Guid accountId)
    {
        var reactions = await db.Set<PostReaction>()
            .Where(r => postIds.Contains(r.PostId) && r.AccountId == accountId)
            .Select(r => new { r.PostId, r.Symbol })
            .ToListAsync();

        return postIds.ToDictionary(
            postId => postId,
            postId => reactions
                .Where(r => r.PostId == postId)
                .ToDictionary(
                    r => r.Symbol,
                    _ => true
                )
        );
    }

    /// <summary>
    /// Increases the view count for a post.
    /// Uses the flush buffer service to batch database updates for better performance.
    /// </summary>
    /// <param name="postId">The ID of the post to mark as viewed</param>
    /// <param name="viewerId">Optional viewer ID for unique view counting (anonymous if null)</param>
    /// <returns>Task representing the asynchronous operation</returns>
    public async Task IncreaseViewCount(Guid postId, string? viewerId = null)
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

        // Add view info to flush buffer
        flushBuffer.Enqueue(new PostViewInfo
        {
            PostId = postId,
            ViewerId = viewerId,
            ViewedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
        });
    }

    public async Task<List<Post>> LoadPublishers(List<Post> posts)
    {
        var publisherIds = posts
            .SelectMany<Post, Guid?>(e =>
            [
                e.PublisherId,
                e.RepliedPost?.PublisherId,
                e.ForwardedPost?.PublisherId
            ])
            .Where(e => e != null)
            .Distinct()
            .ToList();
        if (publisherIds.Count == 0) return posts;

        var publishers = await db.Publishers
            .Where(e => publisherIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id);

        foreach (var post in posts)
        {
            if (publishers.TryGetValue(post.PublisherId, out var publisher))
                post.Publisher = publisher;

            if (post.RepliedPost?.PublisherId != null &&
                publishers.TryGetValue(post.RepliedPost.PublisherId, out var repliedPublisher))
                post.RepliedPost.Publisher = repliedPublisher;

            if (post.ForwardedPost?.PublisherId != null &&
                publishers.TryGetValue(post.ForwardedPost.PublisherId, out var forwardedPublisher))
                post.ForwardedPost.Publisher = forwardedPublisher;
        }

        return posts;
    }

    public async Task<List<Post>> LoadInteractive(List<Post> posts, Account? currentUser = null)
    {
        if (posts.Count == 0) return posts;

        var postsId = posts.Select(e => e.Id).ToList();

        var reactionMaps = await GetPostReactionMapBatch(postsId);
        var reactionMadeMap = currentUser is not null
            ? await GetPostReactionMadeMapBatch(postsId, Guid.Parse(currentUser.Id))
            : new Dictionary<Guid, Dictionary<string, bool>>();
        var repliesCountMap = await GetPostRepliesCountBatch(postsId);

        foreach (var post in posts)
        {
            // Set reaction count
            post.ReactionsCount = reactionMaps.TryGetValue(post.Id, out var count)
                ? count
                : new Dictionary<string, int>();

            // Set reaction made status
            post.ReactionsMade = reactionMadeMap.TryGetValue(post.Id, out var made)
                ? made
                : [];

            // Set reply count
            post.RepliesCount = repliesCountMap.TryGetValue(post.Id, out var repliesCount)
                ? repliesCount
                : 0;

            // Track view for each post in the list
            if (currentUser != null)
                await IncreaseViewCount(post.Id, currentUser.Id);
            else
                await IncreaseViewCount(post.Id);
        }

        return posts;
    }

    private async Task<Dictionary<Guid, int>> GetPostRepliesCountBatch(List<Guid> postIds)
    {
        return await db.Posts
            .Where(p => p.RepliedPostId != null && postIds.Contains(p.RepliedPostId.Value))
            .GroupBy(p => p.RepliedPostId!.Value)
            .ToDictionaryAsync(
                g => g.Key,
                g => g.Count()
            );
    }

    private async Task LoadPostEmbed(Post post, Account? currentUser)
    {
        if (!post.Meta!.TryGetValue("embeds", out var value))
            return;

        var embeds = value switch
        {
            JsonElement e => e.Deserialize<List<Dictionary<string, object>>>(),
            _ => null
        };
        if (embeds is null)
            return;

        // Find the index of the poll embed first
        var pollIndex = embeds.FindIndex(e =>
            e.ContainsKey("type") && ((JsonElement)e["type"]).ToString() == "poll"
        );

        if (pollIndex < 0)
        {
            post.Meta["embeds"] = embeds;
            return;
        }

        var pollEmbed = embeds[pollIndex];
        try
        {
            var pollId = Guid.Parse(((JsonElement)pollEmbed["id"]).ToString());

            Guid? currentUserId = currentUser is not null ? Guid.Parse(currentUser.Id) : null;
            var updatedPoll = await polls.LoadPollEmbed(pollId, currentUserId);
            embeds[pollIndex] = EmbeddableBase.ToDictionary(updatedPoll);
            post.Meta["embeds"] = embeds;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load poll embed for post {PostId}", post.Id);
        }
    }

    public async Task<List<Post>> LoadPostInfo(
        List<Post> posts,
        Account? currentUser = null,
        bool truncate = false
    )
    {
        if (posts.Count == 0) return posts;

        posts = await LoadPublishers(posts);
        posts = await LoadInteractive(posts, currentUser);

        foreach (
            var post in posts
                .Where(e => e.Meta is not null && e.Meta.ContainsKey("embeds"))
        )
            await LoadPostEmbed(post, currentUser);

        if (truncate)
            posts = TruncatePostContent(posts);

        return posts;
    }

    public async Task<Post> LoadPostInfo(Post post, Account? currentUser = null, bool truncate = false)
    {
        // Convert single post to list, process it, then return the single post
        var posts = await LoadPostInfo([post], currentUser, truncate);
        return posts.First();
    }

    private const string FeaturedPostCacheKey = "posts:featured";

    public async Task<List<Post>> ListFeaturedPostsAsync(Account? currentUser = null)
    {
        // Check cache first for featured post IDs
        var featuredIds = await cache.GetAsync<List<Guid>>(FeaturedPostCacheKey);

        if (featuredIds is null)
        {
            // The previous day highest rated posts
            var today = SystemClock.Instance.GetCurrentInstant();
            var periodStart = today.InUtc().Date.AtStartOfDayInZone(DateTimeZone.Utc).ToInstant()
                .Minus(Duration.FromDays(1));
            var periodEnd = today.InUtc().Date.AtStartOfDayInZone(DateTimeZone.Utc).ToInstant();

            var postsInPeriod = await db.Posts
                .Where(e => e.Visibility == PostVisibility.Public)
                .Where(e => e.CreatedAt >= periodStart && e.CreatedAt < periodEnd)
                .Select(e => e.Id)
                .ToListAsync();

            var reactionScores = await db.PostReactions
                .Where(e => postsInPeriod.Contains(e.PostId))
                .GroupBy(e => e.PostId)
                .Select(e => new
                {
                    PostId = e.Key,
                    Score = e.Sum(r => r.Attitude == PostReactionAttitude.Positive ? 1 : -1)
                })
                .ToDictionaryAsync(e => e.PostId, e => e.Score);

            var repliesCounts = await db.Posts
                .Where(p => p.RepliedPostId != null && postsInPeriod.Contains(p.RepliedPostId.Value))
                .GroupBy(p => p.RepliedPostId!.Value)
                .ToDictionaryAsync(
                    g => g.Key,
                    g => g.Count()
                );

            // Load awardsScores for postsInPeriod
            var awardsScores = await db.Posts
                .Where(p => postsInPeriod.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.AwardedScore);

            var reactSocialPoints = postsInPeriod
                .Select(postId => new
                {
                    PostId = postId,
                    Count =
                        (reactionScores.TryGetValue(postId, out var rScore) ? rScore : 0)
                        + (repliesCounts.TryGetValue(postId, out var repCount) ? repCount : 0)
                        + (awardsScores.TryGetValue(postId, out var awardScore) ? (int)(awardScore / 10) : 0)
                })
                .OrderByDescending(e => e.Count)
                .Take(5)
                .ToDictionary(e => e.PostId, e => e.Count);

            featuredIds = reactSocialPoints.Select(e => e.Key).ToList();

            await cache.SetAsync(FeaturedPostCacheKey, featuredIds, TimeSpan.FromHours(4));

            // Create featured record
            var existingFeaturedPostIds = await db.PostFeaturedRecords
                .Where(r => featuredIds.Contains(r.PostId))
                .Select(r => r.PostId)
                .ToListAsync();

            var records = reactSocialPoints
                .Where(p => !existingFeaturedPostIds.Contains(p.Key))
                .Select(e => new PostFeaturedRecord
                {
                    PostId = e.Key,
                    SocialCredits = e.Value
                }).ToList();

            if (records.Count != 0)
            {
                db.PostFeaturedRecords.AddRange(records);
                await db.SaveChangesAsync();
            }
        }

        var posts = await db.Posts
            .Where(e => featuredIds.Contains(e.Id))
            .Include(e => e.ForwardedPost)
            .Include(e => e.RepliedPost)
            .Include(e => e.Categories)
            .Include(e => e.Publisher)
            .Take(featuredIds.Count)
            .ToListAsync();
        posts = posts.OrderBy(e => featuredIds.IndexOf(e.Id)).ToList();
        posts = await LoadPostInfo(posts, currentUser, true);

        return posts;
    }

    public async Task<PostAward> AwardPost(
        Guid postId,
        Guid accountId,
        decimal amount,
        PostReactionAttitude attitude,
        string? message
    )
    {
        var post = await db.Posts.Where(p => p.Id == postId).FirstOrDefaultAsync();
        if (post is null) throw new InvalidOperationException("Post not found");

        var award = new PostAward
        {
            Amount = amount,
            Attitude = attitude,
            Message = message,
            PostId = postId,
            AccountId = accountId
        };

        db.PostAwards.Add(award);
        await db.SaveChangesAsync();

        var delta = award.Attitude == PostReactionAttitude.Positive ? amount : -amount;

        await db.Posts.Where(p => p.Id == postId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.AwardedScore, p => p.AwardedScore + delta));

        _ = Task.Run(async () =>
        {
            using var scope = factory.CreateScope();
            var pub = scope.ServiceProvider.GetRequiredService<Publisher.PublisherService>();
            var nty = scope.ServiceProvider.GetRequiredService<RingService.RingServiceClient>();
            var accounts = scope.ServiceProvider.GetRequiredService<AccountService.AccountServiceClient>();
            var accountsHelper = scope.ServiceProvider.GetRequiredService<AccountClientHelper>();
            try
            {
                var sender = await accountsHelper.GetAccount(accountId);

                var members = await pub.GetPublisherMembers(post.PublisherId);
                var queryRequest = new GetAccountBatchRequest();
                queryRequest.Id.AddRange(members.Select(m => m.AccountId.ToString()));
                var queryResponse = await accounts.GetAccountBatchAsync(queryRequest);
                foreach (var member in queryResponse.Accounts)
                {
                    if (member is null) continue;
                    CultureService.SetCultureInfo(member);

                    await nty.SendPushNotificationToUserAsync(
                        new SendPushNotificationToUserRequest
                        {
                            UserId = member.Id,
                            Notification = new PushNotification
                            {
                                Topic = "posts.awards.new",
                                Title = localizer["PostAwardedTitle", sender.Nick],
                                Body = string.IsNullOrWhiteSpace(post.Title)
                                    ? localizer["PostAwardedBody", sender.Nick, amount]
                                    : localizer["PostAwardedContentBody", sender.Nick, amount,
                                        post.Title],
                                IsSavable = true,
                                ActionUri = $"/posts/{post.Id}"
                            }
                        }
                    );
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"Error when sending post awarded notification: {ex.Message} {ex.StackTrace}");
            }
        });

        return award;
    }
}

public static class PostQueryExtensions
{
    public static IQueryable<Post> FilterWithVisibility(
        this IQueryable<Post> source,
        Account? currentUser,
        List<Guid> userFriends,
        List<Publisher.Publisher> publishers,
        bool isListing = false
    )
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var publishersId = publishers.Select(e => e.Id).ToList();

        source = isListing switch
        {
            true when currentUser is not null => source.Where(e =>
                e.Visibility != PostVisibility.Unlisted || publishersId.Contains(e.PublisherId)),
            true => source.Where(e => e.Visibility != PostVisibility.Unlisted),
            _ => source
        };

        if (currentUser is null)
            return source
                .Where(e => e.PublishedAt != null && now >= e.PublishedAt)
                .Where(e => e.Visibility == PostVisibility.Public);

        return source
            .Where(e => (e.PublishedAt != null && now >= e.PublishedAt) || publishersId.Contains(e.PublisherId))
            .Where(e => e.Visibility != PostVisibility.Private || publishersId.Contains(e.PublisherId))
            .Where(e => e.Visibility != PostVisibility.Friends ||
                        (e.Publisher.AccountId != null && userFriends.Contains(e.Publisher.AccountId.Value)) ||
                        publishersId.Contains(e.PublisherId));
    }
}
