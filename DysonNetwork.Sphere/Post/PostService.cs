using System.Text.RegularExpressions;
using DysonNetwork.Shared;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Sphere.WebReader;
using DysonNetwork.Sphere.Localization;
using DysonNetwork.Sphere.Publisher;
using DysonNetwork.Sphere.ActivityPub;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using NodaTime;
using Markdig;
using DysonNetwork.Shared.Models;

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
    Publisher.PublisherService ps,
    WebReaderService reader,
    AccountService.AccountServiceClient accounts,
    ActivityPubObjectFactory objFactory
)
{
    private const string PostFileUsageIdentifier = "post";

    private static List<SnPost> TruncatePostContent(List<SnPost> input)
    {
        const int maxLength = 256;
        const int embedMaxLength = 80;
        foreach (var item in input)
        {
            if (item.Content?.Length > maxLength)
            {
                var plainText = Markdown.ToPlainText(item.Content);
                item.Content = plainText.Length > maxLength ? plainText[..maxLength] : plainText;
                item.IsTruncated = true;
            }

            // Truncate replied post content with shorter embed length
            if (item.RepliedPost?.Content != null)
            {
                var plainText = Markdown.ToPlainText(item.RepliedPost.Content);
                if (plainText.Length > embedMaxLength)
                {
                    item.RepliedPost.Content = plainText[..embedMaxLength];
                    item.RepliedPost.IsTruncated = true;
                }
            }

            // Truncate forwarded post content with shorter embed length
            if (item.ForwardedPost?.Content == null ||
                Markdown.ToPlainText(item.ForwardedPost.Content).Length <= embedMaxLength) continue;
            var forwardedPlainText = Markdown.ToPlainText(item.ForwardedPost.Content);
            item.ForwardedPost.Content = forwardedPlainText[..embedMaxLength];
            item.ForwardedPost.IsTruncated = true;
        }

        return input;
    }

    public (string title, string content) ChopPostForNotification(SnPost post)
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

    public async Task<SnPost> PostAsync(
        SnPost post,
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

            post.Attachments = queryResponse.Files.Select(SnCloudFileReferenceObject.FromProtoValue).ToList();
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

            var newTags = missingSlugs.Select(slug => new SnPostTag { Slug = slug }).ToList();
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
                    var members = await pub.GetPublisherMembers(post.RepliedPost.PublisherId!.Value);
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
                                    Title = localizer["PostReplyTitle", sender!.Nick],
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
        _ = Task.Run(async () => await CreateLinkPreviewAsync(post));

        // Send ActivityPub Create activity in background for public posts
        if (post.PublishedAt is not null && post.PublishedAt.Value.ToDateTimeUtc() <= DateTime.UtcNow &&
            post.Visibility == Shared.Models.PostVisibility.Public)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = factory.CreateScope();
                    var deliveryService = scope.ServiceProvider.GetRequiredService<ActivityPubDeliveryService>();
                    await deliveryService.SendCreateActivityAsync(post);
                }
                catch (Exception err)
                {
                    logger.LogError($"Error when sending ActivityPub Create activity: {err.Message}");
                }
            });
        }

        return post;
    }

    public async Task<SnPost> UpdatePostAsync(
        SnPost post,
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

            post.Attachments = queryResponse.Files.Select(SnCloudFileReferenceObject.FromProtoValue).ToList();
        }

        if (tags is not null)
        {
            var existingTags = await db.PostTags.Where(e => tags.Contains(e.Slug)).ToListAsync();

            // Determine missing slugs
            var existingSlugs = existingTags.Select(t => t.Slug).ToHashSet();
            var missingSlugs = tags.Where(slug => !existingSlugs.Contains(slug)).ToList();

            var newTags = missingSlugs.Select(slug => new SnPostTag { Slug = slug }).ToList();
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
        _ = Task.Run(async () => await CreateLinkPreviewAsync(post));

        // Send ActivityPub Update activity in background for public posts
        if (post.Visibility == Shared.Models.PostVisibility.Public)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = factory.CreateScope();
                    var deliveryService = scope.ServiceProvider.GetRequiredService<ActivityPubDeliveryService>();
                    await deliveryService.SendUpdateActivityAsync(post);
                }
                catch (Exception err)
                {
                    logger.LogError($"Error when sending ActivityPub Update activity: {err.Message}");
                }
            });
        }

        return post;
    }

    [GeneratedRegex(@"https?://(?!.*\.\w{1,6}(?:[#?]|$))[^\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex GetLinkRegex();

    private async Task<SnPost> PreviewPostLinkAsync(SnPost item)
    {
        if (item.Type != Shared.Models.PostType.Moment || string.IsNullOrEmpty(item.Content)) return item;

        // Find all URLs in the content
        var matches = GetLinkRegex().Matches(item.Content);

        if (matches.Count == 0)
            return item;

        // Initialize meta dictionary if null
        item.Metadata ??= new Dictionary<string, object>();
        if (!item.Metadata.TryGetValue("embeds", out var existingEmbeds) || existingEmbeds is not List<EmbeddableBase>)
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
            if (updatedPost.Metadata != null &&
                updatedPost.Metadata.TryGetValue("embeds", out var embeds) &&
                embeds is List<Dictionary<string, object>> { Count: > 0 } embedsList)
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

    public async Task DeletePostAsync(SnPost post)
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

        // Send ActivityPub Delete activity in background for public posts
        if (post.Visibility == Shared.Models.PostVisibility.Public)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = factory.CreateScope();
                    var deliveryService = scope.ServiceProvider.GetRequiredService<ActivityPubDeliveryService>();
                    await deliveryService.SendDeleteActivityAsync(post);
                }
                catch (Exception err)
                {
                    logger.LogError($"Error when sending ActivityPub Delete activity: {err.Message}");
                }
            });
        }
    }

    public async Task<SnPost> PinPostAsync(SnPost post, Account currentUser, Shared.Models.PostPinMode pinMode)
    {
        var accountId = Guid.Parse(currentUser.Id);
        if (post.RepliedPostId != null)
        {
            if (pinMode != Shared.Models.PostPinMode.ReplyPage)
                throw new InvalidOperationException("Replies can only be pinned in the reply page.");
            if (post.RepliedPost == null) throw new ArgumentNullException(nameof(post.RepliedPost));

            if (!await ps.IsMemberWithRole(post.RepliedPost.PublisherId!.Value, accountId,
                    Shared.Models.PublisherMemberRole.Editor))
                throw new InvalidOperationException("Only editors of original post can pin replies.");

            post.PinMode = pinMode;
        }
        else
        {
            if (post.PublisherId == null || !await ps.IsMemberWithRole(post.PublisherId.Value, accountId,
                    Shared.Models.PublisherMemberRole.Editor))
                throw new InvalidOperationException("Only editors can pin replies.");

            post.PinMode = pinMode;
        }

        db.Update(post);
        await db.SaveChangesAsync();

        return post;
    }

    public async Task<SnPost> UnpinPostAsync(SnPost post, Account currentUser)
    {
        var accountId = Guid.Parse(currentUser.Id);
        if (post.RepliedPostId != null)
        {
            if (post.RepliedPost == null) throw new ArgumentNullException(nameof(post.RepliedPost));

            if (!await ps.IsMemberWithRole(post.RepliedPost.PublisherId!.Value, accountId,
                    Shared.Models.PublisherMemberRole.Editor))
                throw new InvalidOperationException("Only editors of original post can unpin replies.");
        }
        else
        {
            if (post.PublisherId == null || !await ps.IsMemberWithRole(post.PublisherId.Value, accountId,
                    Shared.Models.PublisherMemberRole.Editor))
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
        Account sender,
        bool isRemoving,
        bool isSelfReact
    )
    {
        var isExistingReaction = reaction.AccountId.HasValue &&
                                 await db.Set<SnPostReaction>()
                                     .AnyAsync(r => r.PostId == post.Id && r.AccountId == reaction.AccountId.Value);

        if (isRemoving)
            await db.PostReactions
                .Where(r => r.PostId == post.Id && r.Symbol == reaction.Symbol &&
                            reaction.AccountId.HasValue && r.AccountId == reaction.AccountId.Value)
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
            case Shared.Models.PostReactionAttitude.Positive:
                if (isRemoving) post.Upvotes--;
                else post.Upvotes++;
                break;
            case Shared.Models.PostReactionAttitude.Negative:
                if (isRemoving) post.Downvotes--;
                else post.Downvotes++;
                break;
        }

        await db.SaveChangesAsync();

        if (isSelfReact) return isRemoving;
        
        // Send ActivityPub Like/Undo activities if post's publisher has actor
        if (post.PublisherId.HasValue)
        {
            var accountId = Guid.Parse(sender.Id);
            var accountPublisher = await db.Publishers
                .Where(p => p.Members.Any(m => m.AccountId == accountId))
                .FirstOrDefaultAsync();
            var accountActor = accountPublisher is null
                ? null
                : await objFactory.GetLocalActorAsync(accountPublisher.Id);

            if (accountActor != null && reaction.Attitude == Shared.Models.PostReactionAttitude.Positive)
            {
                if (!isRemoving)
                {
                    // Sending Like - deliver to publisher's remote followers
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var scope = factory.CreateScope();
                            var deliveryService = scope.ServiceProvider
                                .GetRequiredService<ActivityPubDeliveryService>();
                            await deliveryService.SendLikeActivityToLocalPostAsync(
                                accountActor,
                                post.Id
                            );
                        }
                        catch (Exception ex)
                        {
                            logger.LogError($"Error sending ActivityPub Like: {ex.Message}");
                        }
                    });
                }
                else
                {
                    // Sending Undo Like - deliver to publisher's remote followers
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var scope = factory.CreateScope();
                            var deliveryService = scope.ServiceProvider
                                .GetRequiredService<ActivityPubDeliveryService>();
                            await deliveryService.SendUndoLikeActivityAsync(
                                accountActor,
                                post.Id
                            );
                        }
                        catch (Exception ex)
                        {
                            logger.LogError($"Error sending ActivityPub Undo Like: {ex.Message}");
                        }
                    });
                }
            }
        }

        _ = Task.Run(async () =>
        {
            using var scope = factory.CreateScope();
            var pub = scope.ServiceProvider.GetRequiredService<Publisher.PublisherService>();
            var nty = scope.ServiceProvider.GetRequiredService<RingService.RingServiceClient>();
            var accounts = scope.ServiceProvider.GetRequiredService<AccountService.AccountServiceClient>();
            try
            {
                if (post.PublisherId == null) return;
                var members = await pub.GetPublisherMembers(post.PublisherId.Value);
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
        return await db.Set<SnPostReaction>()
            .Where(r => r.PostId == postId)
            .GroupBy(r => r.Symbol)
            .ToDictionaryAsync(
                g => g.Key,
                g => g.Count()
            );
    }

    public async Task<Dictionary<Guid, Dictionary<string, int>>> GetPostReactionMapBatch(List<Guid> postIds)
    {
        return await db.Set<SnPostReaction>()
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

    private async Task<Dictionary<Guid, Dictionary<string, bool>>> GetPostReactionMadeMapBatch(List<Guid> postIds,
        Guid accountId)
    {
        var reactions = await db.Set<SnPostReaction>()
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

    private async Task<List<SnPost>> LoadPubsAndActors(List<SnPost> posts)
    {
        var publisherIds = posts
            .SelectMany<SnPost, Guid?>(e =>
            [
                e.PublisherId,
                e.RepliedPost?.PublisherId,
                e.ForwardedPost?.PublisherId
            ])
            .Where(e => e != null)
            .Distinct()
            .ToList();
        var actorIds = posts
            .SelectMany<SnPost, Guid?>(e =>
            [
                e.ActorId,
                e.RepliedPost?.ActorId,
                e.ForwardedPost?.ActorId
            ])
            .Where(e => e != null)
            .Distinct()
            .ToList();
        if (publisherIds.Count == 0 && actorIds.Count == 0) return posts;

        var publishers = await db.Publishers
            .Where(e => publisherIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id);

        var actors = await db.FediverseActors
            .Include(e => e.Instance)
            .Where(e => actorIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id);

        foreach (var post in posts)
        {
            if (post.PublisherId.HasValue && publishers.TryGetValue(post.PublisherId.Value, out var publisher))
                post.Publisher = publisher;

            if (post.ActorId.HasValue && actors.TryGetValue(post.ActorId.Value, out var actor))
                post.Actor = actor;

            if (post.RepliedPost?.PublisherId != null &&
                publishers.TryGetValue(post.RepliedPost.PublisherId.Value, out var repliedPublisher))
                post.RepliedPost.Publisher = repliedPublisher;

            if (post.RepliedPost?.ActorId != null &&
                actors.TryGetValue(post.RepliedPost.ActorId.Value, out var repliedActor))
                post.RepliedPost.Actor = repliedActor;

            if (post.ForwardedPost?.PublisherId != null &&
                publishers.TryGetValue(post.ForwardedPost.PublisherId.Value, out var forwardedPublisher))
                post.ForwardedPost.Publisher = forwardedPublisher;

            if (post.ForwardedPost?.ActorId != null &&
                actors.TryGetValue(post.ForwardedPost.ActorId.Value, out var forwardedActor))
                post.ForwardedPost.Actor = forwardedActor;
        }

        await ps.LoadIndividualPublisherAccounts(publishers.Values);

        return posts;
    }

    private async Task<List<SnPost>> LoadInteractive(List<SnPost> posts, Account? currentUser = null)
    {
        if (posts.Count == 0) return posts;

        var postsId = posts.Select(e => e.Id).ToList();

        var reactionMaps = await GetPostReactionMapBatch(postsId);
        var reactionMadeMap = currentUser is not null
            ? await GetPostReactionMadeMapBatch(postsId, Guid.Parse(currentUser.Id))
            : new Dictionary<Guid, Dictionary<string, bool>>();
        var repliesCountMap = await GetPostRepliesCountBatch(postsId);

        // Load user friends if the current user exists
        List<SnPublisher> publishers = [];
        List<Guid> userFriends = [];
        if (currentUser is not null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(new ListRelationshipSimpleRequest
                { AccountId = currentUser.Id });
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
            post.ReactionsMade = reactionMadeMap.TryGetValue(post.Id, out var made)
                ? made
                : [];

            // Set reply count
            post.RepliesCount = repliesCountMap.GetValueOrDefault(post.Id, 0);

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

            // Track view for each post in the list
            if (currentUser != null)
                await IncreaseViewCount(post.Id, currentUser.Id);
            else
                await IncreaseViewCount(post.Id);
        }

        return posts;
    }

    private bool CanViewPost(SnPost post, Account? currentUser, List<SnPublisher> publishers, List<Guid> userFriends)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var publishersId = publishers.Select(e => e.Id).ToList();

        // Check if post is deleted
        if (post.DeletedAt != null)
            return false;

        if (currentUser is null)
        {
            // Anonymous user can only view public posts that are published
            return post.PublishedAt != null && now >= post.PublishedAt &&
                   post.Visibility == Shared.Models.PostVisibility.Public;
        }

        // Check publication status - either published or user is member
        var isPublished = post.PublishedAt != null && now >= post.PublishedAt;
        var isMember = post.PublisherId.HasValue && publishersId.Contains(post.PublisherId.Value);
        if (!isPublished && !isMember)
            return false;

        // Check visibility
        if (post.Visibility == Shared.Models.PostVisibility.Private && !isMember)
            return false;

        if (post.Visibility == Shared.Models.PostVisibility.Friends &&
            !(post.Publisher.AccountId.HasValue && userFriends.Contains(post.Publisher.AccountId.Value) || isMember))
            return false;

        // Public and Unlisted are allowed
        return true;
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

    public async Task<List<SnPost>> LoadPostInfo(
        List<SnPost> posts,
        Account? currentUser = null,
        bool truncate = false
    )
    {
        if (posts.Count == 0) return posts;

        posts = await LoadPubsAndActors(posts);
        posts = await LoadInteractive(posts, currentUser);

        if (truncate)
            posts = TruncatePostContent(posts);

        return posts;
    }

    public async Task<SnPost> LoadPostInfo(SnPost post, Account? currentUser = null, bool truncate = false)
    {
        // Convert single post to list, process it, then return the single post
        var posts = await LoadPostInfo([post], currentUser, truncate);
        return posts.First();
    }

    private const string FeaturedPostCacheKey = "posts:featured";

    public async Task<List<SnPost>> ListFeaturedPostsAsync(Account? currentUser = null)
    {
        // Check cache first for featured post IDs
        var featuredIds = await cache.GetAsync<List<Guid>>(FeaturedPostCacheKey);

        if (featuredIds is null)
        {
            // The previous day the highest rated posts
            var today = SystemClock.Instance.GetCurrentInstant();
            var periodStart = today.InUtc().Date.AtStartOfDayInZone(DateTimeZone.Utc).ToInstant()
                .Minus(Duration.FromDays(1));
            var periodEnd = today.InUtc().Date.AtStartOfDayInZone(DateTimeZone.Utc).ToInstant();

            var postsInPeriod = await db.Posts
                .Where(e => e.Visibility == Shared.Models.PostVisibility.Public)
                .Where(e => e.CreatedAt >= periodStart && e.CreatedAt < periodEnd)
                .Select(e => e.Id)
                .ToListAsync();

            var reactionScores = await db.PostReactions
                .Where(e => postsInPeriod.Contains(e.PostId))
                .GroupBy(e => e.PostId)
                .Select(e => new
                {
                    PostId = e.Key,
                    Score = e.Sum(r => r.Attitude == Shared.Models.PostReactionAttitude.Positive ? 1 : -1)
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
                        (reactionScores.GetValueOrDefault(postId, 0))
                        + (repliesCounts.GetValueOrDefault(postId, 0))
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
                .Select(e => new SnPostFeaturedRecord
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
            .Include(e => e.FeaturedRecords)
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
        Shared.Models.PostReactionAttitude attitude,
        string? message
    )
    {
        var post = await db.Posts.Where(p => p.Id == postId).FirstOrDefaultAsync();
        if (post is null) throw new InvalidOperationException("Post not found");

        var award = new SnPostAward
        {
            Amount = amount,
            Attitude = attitude,
            Message = message,
            PostId = postId,
            AccountId = accountId
        };

        db.PostAwards.Add(award);
        await db.SaveChangesAsync();

        var delta = award.Attitude == Shared.Models.PostReactionAttitude.Positive ? amount : -amount;

        await db.Posts.Where(p => p.Id == postId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.AwardedScore, p => p.AwardedScore + delta));

        _ = Task.Run(async () =>
        {
            using var scope = factory.CreateScope();
            var pub = scope.ServiceProvider.GetRequiredService<Publisher.PublisherService>();
            var nty = scope.ServiceProvider.GetRequiredService<RingService.RingServiceClient>();
            var accounts = scope.ServiceProvider.GetRequiredService<AccountService.AccountServiceClient>();
            var accountsHelper = scope.ServiceProvider.GetRequiredService<RemoteAccountService>();
            try
            {
                var sender = await accountsHelper.GetAccount(accountId);

                if (post.PublisherId == null) return;
                var members = await pub.GetPublisherMembers(post.PublisherId.Value);
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
    public static IQueryable<SnPost> FilterWithVisibility(
        this IQueryable<SnPost> source,
        Account? currentUser,
        List<Guid> userFriends,
        List<Shared.Models.SnPublisher> publishers,
        bool isListing = false
    )
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var publishersId = publishers.Select(e => e.Id).ToList();

        source = isListing switch
        {
            true when currentUser is not null => source.Where(e =>
                e.Visibility != Shared.Models.PostVisibility.Unlisted ||
                (e.PublisherId.HasValue && publishersId.Contains(e.PublisherId.Value))),
            true => source.Where(e => e.Visibility != Shared.Models.PostVisibility.Unlisted),
            _ => source
        };

        if (currentUser is null)
            return source
                .Where(e => e.PublishedAt != null && now >= e.PublishedAt)
                .Where(e => e.Visibility == Shared.Models.PostVisibility.Public);

        return source
            .Where(e => (e.PublishedAt != null && now >= e.PublishedAt) ||
                        (e.PublisherId.HasValue && publishersId.Contains(e.PublisherId.Value)))
            .Where(e => e.Visibility != Shared.Models.PostVisibility.Private ||
                        publishersId.Contains(e.PublisherId.Value))
            .Where(e => e.Visibility != Shared.Models.PostVisibility.Friends ||
                        (e.Publisher.AccountId != null && userFriends.Contains(e.Publisher.AccountId.Value)) ||
                        publishersId.Contains(e.PublisherId.Value));
    }
}