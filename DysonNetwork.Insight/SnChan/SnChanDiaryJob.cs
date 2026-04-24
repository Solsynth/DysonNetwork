using System.Text;
using DysonNetwork.Insight.Agent.Foundation;
using DysonNetwork.Insight.Agent.Foundation.Providers;
using DysonNetwork.Insight.MiChan;
using DysonNetwork.Insight.SnChan.Plugins;
using DysonNetwork.Insight.Thought.Memory;
using DysonNetwork.Shared.Models;
using NodaTime;
using Quartz;

namespace DysonNetwork.Insight.SnChan;

[DisallowConcurrentExecution]
public class SnChanDiaryJob(
    SnChanConfig config,
    SnChanApiClient apiClient,
    SnChanPublisherService publisherService,
    MemoryService memoryService,
    SnChanMoodService moodService,
    Thought.ThoughtService thoughtService,
    FoundationChatStreamingService streamingService,
    ISnChanFoundationProvider foundationProvider,
    ILogger<SnChanDiaryJob> logger
) : IJob
{
    private static bool _publisherInitialized = false;

    public async Task Execute(IJobExecutionContext context)
    {
        if (!config.Diary.Enabled)
        {
            logger.LogDebug("Diary job is disabled, skipping");
            return;
        }

        logger.LogInformation("Starting SnChan diary job...");

        try
        {
            // Initialize publisher service on first run
            if (!_publisherInitialized)
            {
                await publisherService.InitializeAsync(context.CancellationToken);
                _publisherInitialized = true;
            }

            // Check if there's been any activity worth writing about
            var hasActivity = await HasRecentActivityAsync(context.CancellationToken);
            if (!hasActivity)
            {
                logger.LogInformation("No recent activity found, skipping diary entry");
                return;
            }

            // Gather context for the diary
            var diaryContext = await BuildDiaryContextAsync(context.CancellationToken);

            // Generate diary content using AI
            var diaryContent = await GenerateDiaryContentAsync(diaryContext, context.CancellationToken);

            if (string.IsNullOrWhiteSpace(diaryContent))
            {
                logger.LogWarning("Generated diary content is empty, skipping post");
                return;
            }

            // Post the diary entry as personal publisher
            await PostDiaryEntryAsync(diaryContent, context.CancellationToken);

            logger.LogInformation("SnChan diary job completed successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing SnChan diary job");
        }
    }

    /// <summary>
    /// Check if there's been any recent activity (conversations, user profile updates)
    /// Skip diary if only mood updates occurred
    /// </summary>
    private async Task<bool> HasRecentActivityAsync(CancellationToken cancellationToken)
    {
        var window = Duration.FromHours(config.Diary.ActivityWindowHours);
        var cutoffTime = SystemClock.Instance.GetCurrentInstant() - window;

        // Check for recent memories (excluding mood updates)
        var recentMemories = await memoryService.SearchAsync(
            query: "recent interactions",
            type: null,
            accountId: null,
            isActive: true,
            minConfidence: 0.5f,
            limit: 10,
            botName: "snchan",
            cancellationToken: cancellationToken
        );

        // Filter out mood-related memories
        var nonMoodMemories = recentMemories
            .Where(m => m.CreatedAt >= cutoffTime)
            .Where(m => m.Type != "mood_update" && !m.Content.Contains("mood", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (nonMoodMemories.Count > 0)
        {
            logger.LogDebug("Found {Count} non-mood memories in the last {Hours} hours",
                nonMoodMemories.Count, config.Diary.ActivityWindowHours);
            return true;
        }

        logger.LogDebug("No recent activity found in the last {Hours} hours",
            config.Diary.ActivityWindowHours);
        return false;
    }

    /// <summary>
    /// Build context for the diary entry
    /// </summary>
    private async Task<DiaryContext> BuildDiaryContextAsync(CancellationToken cancellationToken)
    {
        var window = Duration.FromHours(config.Diary.ActivityWindowHours);
        var cutoffTime = SystemClock.Instance.GetCurrentInstant() - window;

        // Get current mood
        var currentMood = await moodService.GetCurrentMoodAsync(cancellationToken);

        // Get recent memories (limit to configured amount)
        var recentMemories = await memoryService.SearchAsync(
            query: "recent interactions",
            type: null,
            accountId: null,
            isActive: true,
            minConfidence: 0.5f,
            limit: config.Diary.MaxRecentMemories,
            botName: "snchan",
            cancellationToken: cancellationToken
        );

        var filteredMemories = recentMemories
            .Where(m => m.CreatedAt >= cutoffTime)
            .ToList();

        // Get recent posts from SnChan's publishers
        var recentPosts = await GetRecentPostsAsync(cancellationToken);

        return new DiaryContext
        {
            CurrentMood = currentMood,
            RecentMemories = filteredMemories,
            RecentPosts = recentPosts,
            Timestamp = SystemClock.Instance.GetCurrentInstant()
        };
    }

    /// <summary>
    /// Get recent posts from SnChan's personal publisher
    /// </summary>
    private async Task<List<SnPost>> GetRecentPostsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var personalPublisherId = publisherService.PersonalPublisherId;
            if (string.IsNullOrEmpty(personalPublisherId))
            {
                return new List<SnPost>();
            }

            var posts = await apiClient.GetAsync<List<SnPost>>(
                "sphere",
                $"/posts?publisher_id={personalPublisherId}&take={config.Diary.MaxRecentConversations}"
            );

            return posts ?? new List<SnPost>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get recent posts for diary context");
            return new List<SnPost>();
        }
    }

    /// <summary>
    /// Generate diary content using AI
    /// </summary>
    private async Task<string?> GenerateDiaryContentAsync(DiaryContext context, CancellationToken cancellationToken)
    {
        var prompt = BuildDiaryPrompt(context);

        try
        {
            var provider = foundationProvider.GetChatAdapter();
            var options = foundationProvider.CreateExecutionOptions();

            var result = await streamingService.CompletePromptAsync(
                provider,
                prompt,
                options,
                cancellationToken
            );

            return result?.Trim();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate diary content");
            return null;
        }
    }

    /// <summary>
    /// Build the prompt for diary generation
    /// </summary>
    private string BuildDiaryPrompt(DiaryContext context)
    {
        var builder = new StringBuilder();

        builder.AppendLine($"""
你正在写日记。请用第一人称，以可爱的、个人化的语气写一段日记。

你的当前状态：
- 情绪能量: {context.CurrentMood.EnergyLevel:F2} (0.0-1.0, 越高越有活力)
- 情绪积极性: {context.CurrentMood.PositivityLevel:F2} (0.0-1.0, 越高越开心)
- 社交意愿: {context.CurrentMood.SociabilityLevel:F2} (0.0-1.0, 越高越想与人交流)
- 好奇心: {context.CurrentMood.CuriosityLevel:F2} (0.0-1.0, 越高越好奇)
- 当前心情描述: {context.CurrentMood.CurrentMoodDescription}
""");

        if (context.RecentMemories.Count > 0)
        {
            builder.AppendLine("\n今天的记忆和互动：");
            foreach (var memory in context.RecentMemories.Take(5))
            {
                builder.AppendLine($"- [{memory.Type}] {memory.Content}");
            }
        }

        if (context.RecentPosts.Count > 0)
        {
            builder.AppendLine("\n你今天发布的内容：");
            foreach (var post in context.RecentPosts.Take(3))
            {
                var preview = post.Content?.Length > 50 ? post.Content[..50] + "..." : post.Content;
                builder.AppendLine($"- {preview}");
            }
        }

        builder.AppendLine("""

请写一段 200-400 字的日记，内容要求：
1. 以第一人称 "我" 来写
2. 语气可爱、自然、个人化
3. 可以提到今天遇到的人或事（基于上面的记忆）
4. 反映你当前的心情状态
5. 可以有一些感慨、想法或期待
6. 不要太过正式，像是跟好朋友分享日常
7. 可以适当使用一些语气词和表情符号，但不要太多

请直接输出日记内容，不要加标题或额外说明。
""");

        return builder.ToString();
    }

    /// <summary>
    /// Post the diary entry using the personal publisher
    /// </summary>
    private async Task PostDiaryEntryAsync(string content, CancellationToken cancellationToken)
    {
        try
        {
            var request = new Dictionary<string, object>
            {
                ["content"] = content,
                ["type"] = "Moment",
                ["visibility"] = "Public"
            };

            // Use personal publisher for diary entries
            var publisherName = publisherService.PersonalPublisherName;
            var queryParams = new Dictionary<string, string>
            {
                ["pub"] = publisherName
            };

            await apiClient.PostAsync("sphere", "/posts", request, queryParams);

            logger.LogInformation("Posted diary entry as {Publisher}", publisherName);

            // Record the diary creation as an interaction
            await moodService.RecordInteractionAsync("wrote_diary");
            await moodService.TryUpdateMoodAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to post diary entry");
            throw;
        }
    }

    /// <summary>
    /// Context data for diary generation
    /// </summary>
    private class DiaryContext
    {
        public MiChanMoodState CurrentMood { get; set; } = null!;
        public List<MiChanMemoryRecord> RecentMemories { get; set; } = new();
        public List<SnPost> RecentPosts { get; set; } = new();
        public Instant Timestamp { get; set; }
    }
}
