using System.Text.Json;
using System.Text.RegularExpressions;
using DysonNetwork.Insight.Thought;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Registry;
using Quartz;

namespace DysonNetwork.Insight.SnChan;

public class SnChanReplyMonitorService(
    SnChanApiClient apiClient,
    ThoughtService thoughtService,
    SnChanConfig config,
    RemoteRingService remoteRingService,
    SnChanMoodService moodService,
    SnChanPublisherService publisherService,
    ILogger<SnChanReplyMonitorService> logger)
{
    private static readonly Regex SnChanMentionRegex = new(@"@snchan\b", RegexOptions.IgnoreCase);

    public async Task CheckAndRespondToMentionsAsync(CancellationToken cancellationToken = default)
    {
        if (!config.ReplyMonitoring.Enabled)
        {
            return;
        }

        logger.LogDebug("Checking for mentions...");

        try
        {
            var mentionedPosts = await apiClient.GetAsync<List<SnPost>>(
                "sphere",
                $"/posts?mentioned=snchan&take=20"
            );

            if (mentionedPosts == null || mentionedPosts.Count == 0)
            {
                logger.LogDebug("No mentions found.");
                return;
            }

            logger.LogInformation("Found {Count} posts mentioning SnChan", mentionedPosts.Count);

            foreach (var post in mentionedPosts.Take(5))
            {
                if (cancellationToken.IsCancellationRequested) break;

                var isMentioned = ContainsMention(post);
                if (isMentioned)
                {
                    await HandleMentionAsync(post, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking mentions");
        }
    }

    public async Task CheckAndRespondToRepliesAsync(CancellationToken cancellationToken = default)
    {
        if (!config.ReplyMonitoring.Enabled || !config.ReplyMonitoring.ReplyNotificationEnabled)
        {
            return;
        }

        logger.LogDebug("Checking for replies to SnChan posts...");

        try
        {
            var botPublisher = await apiClient.GetAsync<SnPublisher>("sphere", "/publishers/me");
            if (botPublisher == null)
            {
                logger.LogDebug("SnChan publisher not found");
                return;
            }

            var myPosts = await apiClient.GetAsync<List<SnPost>>(
                "sphere",
                $"/posts?publisher_id={botPublisher.Id}&take=50"
            );

            if (myPosts == null || myPosts.Count == 0)
            {
                logger.LogDebug("No posts from SnChan found.");
                return;
            }

            foreach (var post in myPosts.Take(20))
            {
                if (cancellationToken.IsCancellationRequested) break;

                await CheckRepliesAsync(post, botPublisher.Id, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking replies");
        }
    }

    private async Task HandleMentionAsync(SnPost post, CancellationToken cancellationToken)
    {
        logger.LogInformation("Handling mention in post {PostId}", post.Id);

        try
        {
            var publisher = post.Publisher;
            if (publisher == null)
            {
                logger.LogWarning("Mention post has no publisher, skipping");
                return;
            }

            if (!publisher.AccountId.HasValue)
            {
                logger.LogWarning("Mention post publisher has no account ID, skipping");
                return;
            }

            var accountId = publisher.AccountId.Value;

            // Build context with publisher information to help AI decide
            var isOfficialPost = publisherService.IsOfficialPost(post);
            var publisherContext = publisherService.GetPublisherContext();

            var message = $"""
你被提到了！

{publisherContext}

原始帖子信息：
- 帖子作者: @{publisher.Name}
- 是否官方帖子: {(isOfficialPost ? "是" : "否")}
- 帖子内容:
{post.Content}

请根据上下文决定使用哪个发布者来回复。如果用户询问关于 Solar Network 的问题或寻求支持，考虑使用官方发布者；否则使用个人发布者。
""";

            var sequence = await thoughtService.CreateAgentInitiatedSequenceAsync(
                accountId,
                message,
                topic: $"来自 @{publisher.Name} 的提及",
                locale: "en",
                botName: "snchan"
            );

            if (sequence != null)
            {
                logger.LogInformation("Created mention response sequence {SequenceId} for account {AccountId}",
                    sequence.Id, accountId);

                // Record emotional event and trigger mood update
                await moodService.RecordInteractionAsync("mentioned_by_user");
                await moodService.TryUpdateMoodAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling mention for post {PostId}", post.Id);
        }
    }

    private async Task CheckRepliesAsync(SnPost post, Guid botPublisherId, CancellationToken cancellationToken)
    {
        try
        {
            var replies = await apiClient.GetAsync<List<SnPost>>(
                "sphere",
                $"/posts?reply_to={post.Id}&take=10"
            );

            if (replies == null || replies.Count == 0)
            {
                return;
            }

            var newReplies = replies
                .Where(r => r.Publisher != null && r.Publisher.Id != botPublisherId)
                .ToList();

            if (newReplies.Count > 0)
            {
                logger.LogInformation("Post {PostId} has {ReplyCount} new replies", post.Id, newReplies.Count);

                var firstReply = newReplies.First();
                if (firstReply.Publisher?.AccountId.HasValue == true)
                {
                    var accountId = firstReply.Publisher.AccountId.Value;

                    var meta = new Dictionary<string, object?>
                    {
                        ["original_post_id"] = post.Id.ToString(),
                        ["type"] = "insight.snchan.reply"
                    };

                    await remoteRingService.SendPushNotificationToUser(
                        accountId.ToString(),
                        "insight.snchan.reply",
                        "Someone replied to your post",
                        null,
                        firstReply.Content,
                        JsonSerializer.SerializeToUtf8Bytes(meta),
                        isSilent: true,
                        isSavable: false
                    );
                }
                
                // Record emotional event and trigger mood update
                await moodService.RecordInteractionAsync("received_reply");
                await moodService.TryUpdateMoodAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking replies for post {PostId}", post.Id);
        }
    }

    private bool ContainsMention(SnPost post)
    {
        if (SnChanMentionRegex.IsMatch(post.Content ?? ""))
            return true;

        if (post.Mentions == null)
            return false;

        return post.Mentions.Any(m =>
            m.Username?.Equals("snchan", StringComparison.OrdinalIgnoreCase) == true);
    }
}