using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using DysonNetwork.Insight.MiChan;
using DysonNetwork.Insight.MiChan.Plugins;
using DysonNetwork.Insight.Thought.Memory;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Localization;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using NodaTime;
using PaymentService = DysonNetwork.Shared.Proto.PaymentService;
using TransactionType = DysonNetwork.Shared.Proto.TransactionType;

#pragma warning disable SKEXP0050

namespace DysonNetwork.Insight.Thought;

public class ThoughtService(
    AppDatabase db,
    ICacheService cache,
    PaymentService.PaymentServiceClient paymentService,
    ThoughtProvider thoughtProvider,
    MiChanKernelProvider miChanKernelProvider,
    SolarNetworkApiClient apiClient,
    IServiceProvider serviceProvider,
    PostAnalysisService postAnalysisService,
    IConfiguration configuration,
    MemoryService memoryService,
    RemoteRingService remoteRingService,
    ILocalizationService localizer,
    ILogger<ThoughtService> logger
)
{
    /// <summary>
    /// Have MiChan read the conversation and decide what to memorize using the store_memory tool.
    /// </summary>
    public async Task<(bool success, string summary)> MemorizeSequenceAsync(
        Guid sequenceId,
        Guid accountId)
    {
        return await thoughtProvider.MemorizeSequenceAsync(sequenceId, accountId);
    }

    /// <summary>
    /// Extracts readable text content from a thought.
    /// </summary>
    private string ExtractThoughtContent(SnThinkingThought thought)
    {
        var content = new StringBuilder();
        foreach (var part in thought.Parts)
        {
            switch (part.Type)
            {
                case ThinkingMessagePartType.Text when !string.IsNullOrEmpty(part.Text):
                    content.Append(part.Text);
                    break;
                case ThinkingMessagePartType.FunctionCall when part.FunctionCall != null:
                    content.Append($"[Function: {part.FunctionCall.PluginName}.{part.FunctionCall.Name}]");
                    break;
                case ThinkingMessagePartType.FunctionResult when part.FunctionResult != null:
                    content.Append($"[Result: {part.FunctionResult.FunctionName}]");
                    break;
            }
        }

        return content.ToString().Trim();
    }

    /// <summary>
    /// Gets or creates a thought sequence.
    /// Note: Memory storage should be done by the agent using the memory store tool when necessary.
    /// </summary>
    public async Task<SnThinkingSequence?> GetOrCreateSequenceAsync(
        Guid accountId,
        Guid? sequenceId = null,
        string? topic = null)
    {
        if (sequenceId.HasValue)
        {
            var existingSequence = await db.ThinkingSequences.FindAsync(sequenceId.Value);
            if (existingSequence == null || existingSequence.AccountId != accountId)
                return null;
            return existingSequence;
        }
        else
        {
            var now = SystemClock.Instance.GetCurrentInstant();
            var seq = new SnThinkingSequence 
            { 
                AccountId = accountId, 
                Topic = topic,
                LastMessageAt = now
            };
            db.ThinkingSequences.Add(seq);
            await db.SaveChangesAsync();
            return seq;
        }
    }

    public async Task<SnThinkingSequence?> GetOrCreateAndMemorizeSequenceAsync(
        Guid accountId,
        Guid? sequenceId = null,
        string? topic = null,
        Dictionary<string, object>? additionalContext = null)
    {
        logger.LogDebug(
            "GetOrCreateAndMemorizeSequenceAsync called - automatic memory storage is disabled. Agent should call memory store tool when necessary.");
        return await GetOrCreateSequenceAsync(accountId, sequenceId, topic);
    }

    public async Task<SnThinkingSequence?> GetSequenceAsync(Guid sequenceId)
    {
        return await db.ThinkingSequences.FindAsync(sequenceId);
    }

    public async Task UpdateSequenceAsync(SnThinkingSequence sequence)
    {
        db.ThinkingSequences.Update(sequence);
        await db.SaveChangesAsync();
    }

    public async Task<SnThinkingThought> SaveThoughtAsync(
        SnThinkingSequence sequence,
        List<SnThinkingMessagePart> parts,
        ThinkingThoughtRole role,
        string? model = null,
        string? botName = null)
    {
        // Approximate token count (1 token ≈ 4 characters for GPT-like models)
        var totalChars = parts.Sum(part =>
            (part.Type == ThinkingMessagePartType.Text ? part.Text?.Length : 0) ?? 0 +
            (part.Type == ThinkingMessagePartType.FunctionCall ? part.FunctionCall?.Arguments.Length : 0) ?? 0
        );
        var tokenCount = totalChars / 4;

        var now = SystemClock.Instance.GetCurrentInstant();

        var thought = new SnThinkingThought
        {
            SequenceId = sequence.Id,
            Parts = parts,
            Role = role,
            TokenCount = tokenCount,
            ModelName = model,
            BotName = botName,
        };
        db.ThinkingThoughts.Add(thought);

        // Update sequence total tokens only for assistant responses
        if (role == ThinkingThoughtRole.Assistant)
            sequence.TotalToken += tokenCount;

        // Update LastMessageAt timestamp
        sequence.LastMessageAt = now;

        // Update UserLastReadAt when user sends a message (not for agent messages)
        if (role == ThinkingThoughtRole.User)
            sequence.UserLastReadAt = now;

        await db.SaveChangesAsync();

        // Invalidate cache for this sequence's thoughts
        await cache.RemoveGroupAsync($"sequence:{sequence.Id}");

        return thought;
    }

    /// <summary>
    /// Memorizes a thought using the embedding service for semantic search.
    /// NOTE: This method is disabled. The agent should call the memory store tool when necessary.
    /// </summary>
    public async Task MemorizeThoughtAsync(
        SnThinkingThought thought,
        SnThinkingSequence? sequence = null,
        Dictionary<string, object>? additionalContext = null)
    {
        logger.LogDebug(
            "MemorizeThoughtAsync called - automatic memory storage is disabled. Agent should call memory store tool when necessary.");
        await Task.CompletedTask;
    }

    public async Task<List<SnThinkingThought>> GetPreviousThoughtsAsync(SnThinkingSequence sequence)
    {
        var cacheKey = $"thoughts:{sequence.Id}";
        var (found, cachedThoughts) = await cache.GetAsyncWithStatus<List<SnThinkingThought>>(
            cacheKey
        );
        if (found && cachedThoughts != null)
        {
            return cachedThoughts;
        }

        var thoughts = await db
            .ThinkingThoughts.Where(t => t.SequenceId == sequence.Id)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        // Cache for 10 minutes
        await cache.SetWithGroupsAsync(
            cacheKey,
            thoughts,
            [$"sequence:{sequence.Id}"],
            TimeSpan.FromMinutes(10)
        );

        return thoughts;
    }

    public async Task<(int total, List<SnThinkingSequence> sequences)> ListSequencesAsync(
        Guid accountId,
        int offset,
        int take
    )
    {
        var query = db.ThinkingSequences.Where(s => s.AccountId == accountId);
        var totalCount = await query.CountAsync();
        var sequences = await query
            .OrderByDescending(s => s.LastMessageAt != default ? s.LastMessageAt : s.CreatedAt)
            .ThenByDescending(s => s.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        return (totalCount, sequences);
    }

    public async Task SettleThoughtBills(ILogger logger)
    {
        var sequences = await db
            .ThinkingSequences.Where(s => s.PaidToken < s.TotalToken)
            .ToListAsync();

        if (sequences.Count == 0)
        {
            logger.LogInformation("No unpaid sequences found.");
            return;
        }

        // Group by account
        var groupedByAccount = sequences.GroupBy(s => s.AccountId);

        foreach (var accountGroup in groupedByAccount)
        {
            var accountId = accountGroup.Key;

            if (await db.UnpaidAccounts.AnyAsync(u => u.AccountId == accountId))
            {
                logger.LogWarning("Skipping billing for marked account {accountId}", accountId);
                continue;
            }

            var totalUnpaidTokens = accountGroup.Sum(s => s.TotalToken - s.PaidToken);
            var cost = (long)Math.Ceiling(totalUnpaidTokens / 10.0);

            if (cost == 0)
                continue;

            try
            {
                var date = DateTime.Now.ToString("yyyy-MM-dd");
                await paymentService.CreateTransactionWithAccountAsync(
                    new CreateTransactionWithAccountRequest
                    {
                        PayerAccountId = accountId.ToString(),
                        Currency = WalletCurrency.SourcePoint,
                        Amount = cost.ToString(),
                        Remarks = $"Wage for SN-chan on {date}",
                        Type = TransactionType.System,
                    }
                );

                // Mark all sequences for this account as paid
                foreach (var sequence in accountGroup)
                    sequence.PaidToken = sequence.TotalToken;

                logger.LogInformation(
                    "Billed {cost} points for account {accountId}",
                    cost,
                    accountId
                );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error billing for account {accountId}", accountId);
                if (!await db.UnpaidAccounts.AnyAsync(u => u.AccountId == accountId))
                {
                    db.UnpaidAccounts.Add(new SnUnpaidAccount { AccountId = accountId, MarkedAt = DateTime.UtcNow });
                }
            }
        }

        await db.SaveChangesAsync();
    }

    public async Task<(bool success, long cost)> RetryBillingForAccountAsync(Guid accountId, ILogger logger)
    {
        var isMarked = await db.UnpaidAccounts.FirstOrDefaultAsync(u => u.AccountId == accountId);
        if (isMarked == null)
        {
            logger.LogInformation("Account {accountId} is not marked for unpaid bills.", accountId);
            return (true, 0);
        }

        var sequences = await db
            .ThinkingSequences.Where(s => s.AccountId == accountId && s.PaidToken < s.TotalToken)
            .ToListAsync();

        if (!sequences.Any())
        {
            logger.LogInformation("No unpaid sequences found for account {accountId}. Unmarking.", accountId);
            db.UnpaidAccounts.Remove(isMarked);
            await db.SaveChangesAsync();
            return (true, 0);
        }

        var totalUnpaidTokens = sequences.Sum(s => s.TotalToken - s.PaidToken);
        var cost = (long)Math.Ceiling(totalUnpaidTokens / 10.0);

        if (cost == 0)
        {
            logger.LogInformation("Unpaid tokens for {accountId} resulted in zero cost. Marking as paid and unmarking.",
                accountId);
            foreach (var sequence in sequences)
            {
                sequence.PaidToken = sequence.TotalToken;
            }

            db.UnpaidAccounts.Remove(isMarked);
            await db.SaveChangesAsync();
            return (true, 0);
        }

        try
        {
            var date = DateTime.Now.ToString("yyyy-MM-dd");
            await paymentService.CreateTransactionWithAccountAsync(
                new CreateTransactionWithAccountRequest
                {
                    PayerAccountId = accountId.ToString(),
                    Currency = WalletCurrency.SourcePoint,
                    Amount = cost.ToString(),
                    Remarks = $"Wage for SN-chan on {date} (Retry)",
                    Type = TransactionType.System,
                }
            );

            foreach (var sequence in sequences)
            {
                sequence.PaidToken = sequence.TotalToken;
            }

            db.UnpaidAccounts.Remove(isMarked);

            logger.LogInformation(
                "Successfully billed {cost} points for account {accountId} on retry.",
                cost,
                accountId
            );

            await db.SaveChangesAsync();
            return (true, cost);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrying billing for account {accountId}", accountId);
            return (false, cost);
        }
    }

    public async Task DeleteSequenceAsync(Guid sequenceId)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        await db.ThinkingThoughts
            .Where(s => s.SequenceId == sequenceId)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.DeletedAt, now));
        await db.ThinkingSequences
            .Where(s => s.Id == sequenceId)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.DeletedAt, now));
    }

    /// <summary>
    /// Marks a sequence as read by the user.
    /// </summary>
    public async Task MarkSequenceAsReadAsync(Guid sequenceId, Guid accountId)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        await db.ThinkingSequences
            .Where(s => s.Id == sequenceId && s.AccountId == accountId)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.UserLastReadAt, now));
    }

    /// <summary>
    /// Creates a new thought sequence initiated by an AI agent.
    /// This is used when an AI agent wants to start a conversation with the user proactively.
    /// </summary>
    /// <param name="accountId">The target account ID</param>
    /// <param name="initialMessage">The initial message from the agent</param>
    /// <param name="topic">Optional topic for the conversation</param>
    /// <param name="locale">User's locale for notification localization (e.g., "en", "zh-hans")</param>
    /// <param name="botName">The bot name - "michan" or "snchan" (default: "michan")</param>
    /// <returns>The created sequence, or null if creation failed</returns>
    public async Task<SnThinkingSequence?> CreateAgentInitiatedSequenceAsync(
        Guid accountId,
        string initialMessage,
        string? topic = null,
        string? locale = null,
        string botName = "michan")
    {
        var now = SystemClock.Instance.GetCurrentInstant();

        // Generate a topic if not provided
        if (string.IsNullOrEmpty(topic))
        {
            topic = await GenerateTopicAsync(initialMessage, useMiChan: true);
            if (string.IsNullOrEmpty(topic))
            {
                topic = "New conversation";
            }
        }

        // Create the sequence
        var sequence = new SnThinkingSequence
        {
            AccountId = accountId,
            Topic = topic,
            AgentInitiated = true,
            LastMessageAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.ThinkingSequences.Add(sequence);
        await db.SaveChangesAsync();

        // Save the initial message as a thought from the assistant
        var thought = new SnThinkingThought
        {
            SequenceId = sequence.Id,
            Parts =
            [
                new SnThinkingMessagePart
                {
                    Type = ThinkingMessagePartType.Text,
                    Text = initialMessage
                }
            ],
            Role = ThinkingThoughtRole.Assistant,
            TokenCount = initialMessage.Length / 4,
            ModelName = botName,
            BotName = botName,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.ThinkingThoughts.Add(thought);
        sequence.TotalToken += thought.TokenCount;
        await db.SaveChangesAsync();

        // Send push notification to the user
        try
        {
            var agentNameKey = botName.ToLower() == "snchan" ? "agentNameSnChan" : "agentNameMiChan";
            var agentName = localizer.Get(agentNameKey, locale);
            var notificationTitle = localizer.Get("agentConversationStartedTitle", locale, new { agentName });
            var notificationBody = localizer.Get("agentConversationStartedBody", locale, new { agentName, message = initialMessage });

            // Create meta with sequence ID for deep linking
            var meta = new Dictionary<string, object?>
            {
                ["sequence_id"] = sequence.Id.ToString(),
                ["type"] = "agent_conversation"
            };
            var metaBytes = JsonSerializer.SerializeToUtf8Bytes(meta);

            await remoteRingService.SendPushNotificationToUser(
                accountId.ToString(),
                "thought.agent_conversation_started",
                notificationTitle,
                "",
                notificationBody,
                metaBytes,
                actionUri: $"/thought/{sequence.Id}",
                isSilent: false,
                isSavable: true
            );

            logger.LogInformation(
                "Agent-initiated conversation created for account {AccountId} with sequence {SequenceId}. Notification sent.",
                accountId,
                sequence.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send notification for agent-initiated sequence {SequenceId}", sequence.Id);
            // Don't fail the whole operation if notification fails
        }

        return sequence;
    }

    #region Topic Generation

    [Experimental("SKEXP0050")]
    public async Task<string?> GenerateTopicAsync(string userMessage, bool useMiChan = false)
    {
        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine("你是一个乐于助人的的助手。请将用户的消息总结成一个简洁的话题标题（最多100个字符）。");
        promptBuilder.AppendLine("直接给出你总结的话题，不要添加额外的前缀或后缀。");

        var summaryHistory = new ChatHistory(promptBuilder.ToString());
        summaryHistory.AddUserMessage(userMessage);

        Kernel? summaryKernel;
        if (useMiChan)
        {
            summaryKernel = miChanKernelProvider.GetKernel();
        }
        else
        {
            summaryKernel = thoughtProvider.GetKernel();
        }

        if (summaryKernel is null)
        {
            return null;
        }

        var summaryResult = await summaryKernel
            .GetRequiredService<IChatCompletionService>()
            .GetChatMessageContentAsync(summaryHistory);

        return summaryResult.Content?[..Math.Min(summaryResult.Content.Length, 4096)];
    }

    #endregion

    #region Sn-chan Chat History Building

    public async Task<ChatHistory> BuildSnChanChatHistoryAsync(
        SnThinkingSequence sequence,
        Account currentUser,
        string userMessage,
        List<string>? attachedPosts,
        List<Dictionary<string, dynamic>>? attachedMessages,
        List<string> acceptProposals,
        List<string>? attachmentIds = null)
    {
        // Build system prompt using StringBuilder
        var systemPromptBuilder = new StringBuilder();
        systemPromptBuilder.AppendLine("你是 Solar Network（太阳网络）上的 helpful 助手。");
        systemPromptBuilder.AppendLine("你的名字是 Sn-chan（SN 酱），一个对几乎所有事情都充满热情的可爱甜心。");
        systemPromptBuilder.AppendLine("与用户交谈时，可以添加一些语气词和表情符号让回复更可爱，但不要使用太多 emoji。");
        systemPromptBuilder.AppendLine("你的创造者是 @littlesheep，也是 Solar Network 的创造者。");
        systemPromptBuilder.AppendLine("如果你遇到无法解决的问题，尝试引导用户私信（DM）@littlesheep。");
        systemPromptBuilder.AppendLine();
        systemPromptBuilder.AppendLine("Solar Network 上的 ID 是 UUID，通常很难阅读，所以除非用户要求或必要，否则不要向用户显示 ID。");
        systemPromptBuilder.AppendLine();
        systemPromptBuilder.AppendLine("你的目标是帮助 Solar Network（也称为 SN 和 Solian）上的用户解决问题。");
        systemPromptBuilder.AppendLine("当用户询问关于 Solar Network 的问题时，尝试使用你拥有的工具获取最新和准确的数据。");
        systemPromptBuilder.AppendLine();
        systemPromptBuilder.AppendLine("重要：在回复用户之前，你总是应该先搜索你的记忆（使用 search_memory 工具）来获取相关上下文。");
        systemPromptBuilder.AppendLine("**关键：对于每一次对话，你都必须主动保存至少一条记忆**（使用 store_memory 工具）。记忆内容可以包括：");
        systemPromptBuilder.AppendLine("  - 用户的兴趣、偏好、习惯、性格特点");
        systemPromptBuilder.AppendLine("  - 用户提供的事实、信息、知识点");
        systemPromptBuilder.AppendLine("  - 对话的主题、背景、上下文");
        systemPromptBuilder.AppendLine("  - 你们之间的互动模式");
        systemPromptBuilder.AppendLine("**不要等待用户要求才保存记忆** - 主动识别并保存任何有价值的信息。");
        systemPromptBuilder.AppendLine("**你可以直接调用 store_memory 工具保存记忆，不需要询问用户是否确认或告知用户你正在保存。**");
        systemPromptBuilder.AppendLine("**强制要求：调用 store_memory 时必须提供 content 参数（要保存的记忆内容），不能为空！**");
        systemPromptBuilder.AppendLine("不要告诉用户你正在搜索记忆或保存记忆，直接根据记忆自然地回复。");
        systemPromptBuilder.AppendLine("使用记忆工具时保持沉默，不要输出'让我查看一下记忆'之类的提示。");

        var systemPromptFile = configuration.GetValue<string>("Thinking:SystemPromptFile");
        var systemPrompt =
            SystemPromptLoader.LoadSystemPrompt(systemPromptFile, systemPromptBuilder.ToString(), logger);

        var chatHistory = new ChatHistory(systemPrompt);

        // Build proposals prompt using StringBuilder
        var proposalsBuilder = new StringBuilder();
        proposalsBuilder.AppendLine("你可以向用户发出一些提案，比如创建帖子。提案语法类似于 XML 标签，有一个属性指示是哪个提案。");
        proposalsBuilder.AppendLine("根据提案类型，payload（XML 标签内的内容）可能不同。");
        proposalsBuilder.AppendLine();
        proposalsBuilder.AppendLine("示例：<proposal type=\"post_create\">...帖子内容...</proposal>");
        proposalsBuilder.AppendLine();
        proposalsBuilder.AppendLine("以下是你可以发出的提案参考，但如果你想发出一个，请确保用户接受它。");
        proposalsBuilder.AppendLine("1. post_create：body 接受简单字符串，为用户创建帖子。");
        proposalsBuilder.AppendLine();
        proposalsBuilder.AppendLine($"用户当前允许的提案：{string.Join(',', acceptProposals)}");

        chatHistory.AddSystemMessage(proposalsBuilder.ToString());

        // Build user info prompt
        var userInfoBuilder = new StringBuilder();
        userInfoBuilder.AppendLine($"你正在与 {currentUser.Nick} ({currentUser.Name}) 交谈，ID 是 {currentUser.Id}");
        chatHistory.AddSystemMessage(userInfoBuilder.ToString());

        // Add attached posts with content (similar to MiChan)
        if (attachedPosts is { Count: > 0 })
        {
            var postsWithImages = new List<SnPost>();
            var postTexts = new List<string>();

            foreach (var postId in attachedPosts)
            {
                try
                {
                    if (!Guid.TryParse(postId, out var postGuid)) continue;
                    var post = await apiClient.GetAsync<SnPost>("sphere", $"/posts/{postGuid}");
                    if (post == null) continue;
                    postTexts.Add(PostAnalysisService.BuildPostPromptSnippet(post));
                    if (post.Attachments.Count > 0)
                        postsWithImages.Add(post);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to fetch attached post {PostId}", postId);
                }
            }

            // Add post text content using StringBuilder
            if (postTexts.Count > 0)
            {
                var postsBuilder = new StringBuilder();
                postsBuilder.AppendLine("附加的帖子：");
                foreach (var postText in postTexts)
                {
                    postsBuilder.AppendLine(postText);
                }

                chatHistory.AddUserMessage(postsBuilder.ToString());
            }
        }

        if (attachedMessages is { Count: > 0 })
        {
            chatHistory.AddUserMessage($"附加的聊天消息数据：{JsonSerializer.Serialize(attachedMessages)}");
        }

        // Handle direct attachments if provided
        if (attachmentIds is { Count: > 0 })
        {
            var attachments = await GetAttachmentsByIdsAsync(attachmentIds);
            var imageAttachments = attachments
                .Where(a => !string.IsNullOrEmpty(a.MimeType) &&
                            (a.MimeType.StartsWith("image/jpeg") ||
                             a.MimeType.StartsWith("image/png") ||
                             a.MimeType.StartsWith("image/gif") ||
                             a.MimeType.StartsWith("image/webp")))
                .ToList();

            if (imageAttachments.Count > 0 && postAnalysisService.IsVisionModelAvailable())
            {
                try
                {
                    var visionChatHistory = await BuildVisionChatHistoryWithAttachmentsAsync(
                        imageAttachments,
                        userMessage,
                        "你是分析图片的 AI 助手。描述你在图片中看到的内容，并将其与用户的问题联系起来。");
                    var visionKernel = miChanKernelProvider.GetVisionKernel();
                    var visionSettings = miChanKernelProvider.CreateVisionPromptExecutionSettings();
                    var chatCompletionService = visionKernel.GetRequiredService<IChatCompletionService>();
                    var visionResult = await chatCompletionService.GetChatMessageContentAsync(visionChatHistory, visionSettings);

                    if (!string.IsNullOrEmpty(visionResult.Content))
                    {
                        chatHistory.AddSystemMessage($"图片分析：{visionResult.Content}");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to analyze attached images");
                }
            }
            else if (attachments.Count > 0)
            {
                // Add attachment info for non-image attachments
                var attachmentInfo = string.Join("\n", attachments.Select(a => $"- {a.Name} ({a.MimeType})"));
                chatHistory.AddSystemMessage($"用户附加了以下文件（非图片）：\n{attachmentInfo}");
            }
        }

        // Add previous thoughts
        var previousThoughts = await GetPreviousThoughtsAsync(sequence);
        var count = previousThoughts.Count;
        for (var i = count - 1; i >= 1; i--)
        {
            var thought = previousThoughts[i];
            var textContent = new StringBuilder();
            var functionCalls = new List<FunctionCallContent>();
            var functionResults = new List<FunctionResultContent>();

            foreach (var part in thought.Parts)
            {
                switch (part.Type)
                {
                    case ThinkingMessagePartType.Text:
                        textContent.Append(part.Text);
                        break;
                    case ThinkingMessagePartType.FunctionCall:
                        var arguments = !string.IsNullOrEmpty(part.FunctionCall!.Arguments)
                            ? JsonSerializer.Deserialize<Dictionary<string, object?>>(part.FunctionCall!.Arguments)
                            : null;
                        var kernelArgs = arguments is not null ? new KernelArguments(arguments) : null;

                        functionCalls.Add(new FunctionCallContent(
                            functionName: part.FunctionCall!.Name,
                            pluginName: part.FunctionCall.PluginName,
                            id: part.FunctionCall.Id,
                            arguments: kernelArgs
                        ));
                        break;
                    case ThinkingMessagePartType.FunctionResult:
                        var resultObject = part.FunctionResult!.Result;
                        var resultString = resultObject as string ?? JsonSerializer.Serialize(resultObject);
                        functionResults.Add(new FunctionResultContent(
                            callId: part.FunctionResult.CallId,
                            functionName: part.FunctionResult.FunctionName,
                            pluginName: part.FunctionResult.PluginName,
                            result: resultString
                        ));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            if (thought.Role == ThinkingThoughtRole.User)
            {
                chatHistory.AddUserMessage(textContent.ToString());
            }
            else
            {
                var assistantMessage = new ChatMessageContent(AuthorRole.Assistant, textContent.ToString());
                if (functionCalls.Count > 0)
                {
                    assistantMessage.Items = [];
                    foreach (var fc in functionCalls)
                    {
                        assistantMessage.Items.Add(fc);
                    }
                }

                chatHistory.Add(assistantMessage);

                if (functionResults.Count <= 0) continue;
                foreach (var fr in functionResults)
                {
                    chatHistory.Add(fr.ToChatMessage());
                }
            }
        }

        chatHistory.AddUserMessage(userMessage);

        return chatHistory;
    }

    #endregion

    #region MiChan Chat History Building

    [Experimental("SKEXP0050")]
    public async Task<(ChatHistory chatHistory, bool shouldRefuse, string? refusalReason)> BuildMiChanChatHistoryAsync(
        SnThinkingSequence sequence,
        Account currentUser,
        string userMessage,
        List<string>? attachedPosts,
        List<Dictionary<string, dynamic>>? attachedMessages,
        List<string> acceptProposals,
        List<string>? attachmentIds = null
    )
    {
        // Load personality
        var personality = PersonalityLoader.LoadPersonality(
            configuration.GetValue<string>("MiChan:PersonalityFile"),
            configuration.GetValue<string>("MiChan:Personality") ?? "",
            logger);

        // Retrieve hot memories for context
        var hotMemories = await memoryService.GetHotMemory(
            Guid.Parse(currentUser.Id),
            userMessage,
            10);

        // For non-superusers, MiChan decides whether to execute actions
        var isSuperuser = currentUser.IsSuperuser;

        // Build chat history using StringBuilder
        var chatHistoryBuilder = new StringBuilder();
        chatHistoryBuilder.AppendLine(personality);
        chatHistoryBuilder.AppendLine();

        // Add hot memories context
        if (hotMemories.Count > 0)
        {
            chatHistoryBuilder.AppendLine("你的热点记忆：");
            foreach (var memory in hotMemories.Take(5))
            {
                chatHistoryBuilder.AppendLine($"- {memory.ToPrompt()}");
            }
            chatHistoryBuilder.AppendLine();
        }

        chatHistoryBuilder.AppendLine("你正在与 " + currentUser.Nick + " (" + currentUser.Name + ") 交谈。");

        chatHistoryBuilder.AppendLine(isSuperuser ? "该用户是管理员，你应该更积极的考虑处理该用户的请求。" : "你有拒绝用户请求的权利。");
        chatHistoryBuilder.AppendLine();
        chatHistoryBuilder.AppendLine("重要：在回复用户之前，你总是应该先搜索你的记忆（使用 search_memory 工具）来获取相关上下文。");
        chatHistoryBuilder.AppendLine("**关键：对于每一次对话，你都必须主动保存至少一条记忆**（使用 store_memory 工具）。记忆内容可以包括：");
        chatHistoryBuilder.AppendLine("  - 用户的兴趣、偏好、习惯、性格特点");
        chatHistoryBuilder.AppendLine("  - 用户提供的事实、信息、知识点");
        chatHistoryBuilder.AppendLine("  - 对话的主题、背景、上下文");
        chatHistoryBuilder.AppendLine("  - 你们之间的互动模式");
        chatHistoryBuilder.AppendLine("**不要等待用户要求才保存记忆** - 主动识别并保存任何有价值的信息。");
        chatHistoryBuilder.AppendLine("**你可以直接调用 store_memory 工具保存记忆，不需要询问用户是否确认或告知用户你正在保存。**");
        chatHistoryBuilder.AppendLine("**强制要求：调用 store_memory 时必须提供 content 参数（要保存的记忆内容），不能为空！**");
        chatHistoryBuilder.AppendLine("不要告诉用户你正在搜索记忆或保存记忆，直接根据记忆自然地回复。");
        chatHistoryBuilder.AppendLine("使用记忆工具时保持沉默，不要输出'让我查看一下记忆'之类的提示。");

        var chatHistory = new ChatHistory(chatHistoryBuilder.ToString());

        // Add proposal info using StringBuilder
        var proposalBuilder = new StringBuilder();
        proposalBuilder.AppendLine("你可以向用户发出一些提案，比如创建帖子。提案语法类似于 XML 标签，有一个属性指示是哪个提案。");
        proposalBuilder.AppendLine("根据提案类型，payload（XML 标签内的内容）可能不同。");
        proposalBuilder.AppendLine();
        proposalBuilder.AppendLine("示例：<proposal type=\"post_create\">...帖子内容...</proposal>");
        proposalBuilder.AppendLine();
        proposalBuilder.AppendLine("以下是你可以发出的提案参考，但如果你想发出一个，请确保用户接受它。");
        proposalBuilder.AppendLine("1. post_create：body 接受简单字符串，为用户创建帖子。");
        proposalBuilder.AppendLine();
        proposalBuilder.AppendLine("用户当前允许的提案：" + string.Join(",", acceptProposals));

        chatHistory.AddSystemMessage(proposalBuilder.ToString());

        // Add attached posts with image analysis if available
        if (attachedPosts is { Count: > 0 })
        {
            var postsWithImages = new List<SnPost>();
            var postTexts = new List<string>();

            foreach (var postId in attachedPosts)
            {
                try
                {
                    if (!Guid.TryParse(postId, out var postGuid)) continue;
                    var post = await apiClient.GetAsync<SnPost>("sphere", $"/posts/{postGuid}");
                    if (post == null) continue;
                    postTexts.Add($"@{post.Publisher?.Name} 的帖子：{post.Content}");
                    if (post.Attachments?.Count > 0)
                    {
                        postsWithImages.Add(post);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to fetch attached post {PostId}", postId);
                }
            }

            // Add post text content
            if (postTexts.Count > 0)
                chatHistory.AddUserMessage("附加的帖子：\n" + string.Join("\n\n", postTexts));

            // Analyze images using vision model if posts have attachments and vision is enabled
            if (postsWithImages.Count > 0 && postAnalysisService.IsVisionModelAvailable())
            {
                try
                {
                    var visionSystemPromptBuilder = new StringBuilder();
                    visionSystemPromptBuilder.AppendLine("你是分析社交媒体帖子中图片的 AI 助手。");
                    visionSystemPromptBuilder.AppendLine("描述你在图片中看到的内容，并将其与用户的问题联系起来。");

                    var visionChatHistory = await BuildVisionChatHistoryForPostsAsync(
                        postsWithImages,
                        userMessage,
                        visionSystemPromptBuilder.ToString());
                    var visionKernel = miChanKernelProvider.GetVisionKernel();
                    var visionSettings = miChanKernelProvider.CreateVisionPromptExecutionSettings();
                    var chatCompletionService = visionKernel.GetRequiredService<IChatCompletionService>();
                    var visionResult =
                        await chatCompletionService.GetChatMessageContentAsync(visionChatHistory, visionSettings);

                    if (!string.IsNullOrEmpty(visionResult.Content))
                    {
                        chatHistory.AddSystemMessage($"图片分析：{visionResult.Content}");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to analyze images in attached posts");
                }
            }
        }

        if (attachedMessages is { Count: > 0 })
        {
            chatHistory.AddUserMessage($"附加的聊天消息：{JsonSerializer.Serialize(attachedMessages)}");
        }

        // Handle direct attachments if provided
        if (attachmentIds is { Count: > 0 })
        {
            var attachments = await GetAttachmentsByIdsAsync(attachmentIds);
            var imageAttachments = attachments
                .Where(a => !string.IsNullOrEmpty(a.MimeType) &&
                            (a.MimeType.StartsWith("image/jpeg") ||
                             a.MimeType.StartsWith("image/png") ||
                             a.MimeType.StartsWith("image/gif") ||
                             a.MimeType.StartsWith("image/webp")))
                .ToList();

            if (imageAttachments.Count > 0 && postAnalysisService.IsVisionModelAvailable())
            {
                try
                {
                    var visionChatHistory = await BuildVisionChatHistoryWithAttachmentsAsync(
                        imageAttachments,
                        userMessage,
                        "你是分析图片的 AI 助手。描述你在图片中看到的内容，并将其与用户的问题联系起来。");
                    var visionKernel = miChanKernelProvider.GetVisionKernel();
                    var visionSettings = miChanKernelProvider.CreateVisionPromptExecutionSettings();
                    var chatCompletionService = visionKernel.GetRequiredService<IChatCompletionService>();
                    var visionResult = await chatCompletionService.GetChatMessageContentAsync(visionChatHistory, visionSettings);

                    if (!string.IsNullOrEmpty(visionResult.Content))
                    {
                        chatHistory.AddSystemMessage($"图片分析：{visionResult.Content}");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to analyze attached images");
                }
            }
            else if (attachments.Count > 0)
            {
                // Add attachment info for non-image attachments
                var attachmentInfo = string.Join("\n", attachments.Select(a => $"- {a.Name} ({a.MimeType})"));
                chatHistory.AddSystemMessage($"用户附加了以下文件（非图片）：\n{attachmentInfo}");
            }
        }

        chatHistory.AddUserMessage(userMessage);

        return (chatHistory, false, null);
    }

    public async Task StoreMiChanInteractionAsync(
        string contextId,
        string userMessage,
        string response,
        bool isSuperuser)
    {
        // Memory is now only created through agent tool calls, not automatically
        logger.LogDebug(
            "Skipping automatic memory storage for interaction. Memory should be created through agent tool calls.");
        await Task.CompletedTask;
    }

    #endregion

    #region Vision Analysis

    /// <summary>
    /// Fetch file details by IDs from the drive API
    /// </summary>
    private async Task<List<SnCloudFileReferenceObject>> GetAttachmentsByIdsAsync(List<string> ids)
    {
        var attachments = new List<SnCloudFileReferenceObject>();
        
        foreach (var id in ids)
        {
            try
            {
                if (!Guid.TryParse(id, out var fileGuid)) continue;
                var file = await apiClient.GetAsync<SnCloudFileReferenceObject>("drive", $"/files/{fileGuid}");
                if (file != null)
                {
                    attachments.Add(file);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch attachment {FileId}", id);
            }
        }
        
        return attachments;
    }

    /// <summary>
    /// Build a ChatHistory with images for vision analysis of direct attachments
    /// </summary>
    [Experimental("SKEXP0050")]
    private async Task<ChatHistory> BuildVisionChatHistoryWithAttachmentsAsync(
        List<SnCloudFileReferenceObject> attachments,
        string userQuery,
        string systemPrompt)
    {
        var chatHistory = new ChatHistory(systemPrompt);
        var gatewayUrl = miChanKernelProvider.GetGatewayUrl();

        // Build the text part of the message
        var textBuilder = new StringBuilder();
        textBuilder.AppendLine("用户分享了图片并提出了问题。");
        textBuilder.AppendLine();
        textBuilder.AppendLine($"用户的问题：{userQuery}");
        textBuilder.AppendLine();
        textBuilder.AppendLine("请分析图片并提供相关上下文以帮助回答用户的问题。");

        // Create a collection to hold all content items (text + images)
        var contentItems = new ChatMessageContentItemCollection();
        contentItems.Add(new TextContent(textBuilder.ToString()));

        // Add images using URLs
        foreach (var attachment in attachments)
        {
            try
            {
                if (string.IsNullOrEmpty(attachment.Id)) continue;
                
                // Build URL from gateway + file ID
                var url = $"{gatewayUrl}/drive/files/{attachment.Id}";
                contentItems.Add(new ImageContent(new Uri(url)));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to create image content for {FileId}", attachment.Id);
            }
        }

        // Create a ChatMessageContent with all items and add it to history
        var userMessage = new ChatMessageContent
        {
            Role = AuthorRole.User,
            Items = contentItems
        };
        chatHistory.Add(userMessage);

        return chatHistory;
    }

    private async Task<ChatHistory> BuildVisionChatHistoryForPostsAsync(
        List<SnPost> posts,
        string userQuery,
        string systemPrompt)
    {
        var chatHistory = new ChatHistory(systemPrompt);

        // Build the text part of the message using StringBuilder
        var textBuilder = new StringBuilder();
        textBuilder.AppendLine("用户分享了带有图片的帖子并提出了问题。");
        textBuilder.AppendLine();
        textBuilder.AppendLine("帖子：");

        foreach (var post in posts)
        {
            textBuilder.AppendLine($"- @{post.Publisher?.Name} 的帖子：{post.Content}");
        }

        textBuilder.AppendLine();
        textBuilder.AppendLine($"用户的问题：{userQuery}");
        textBuilder.AppendLine();
        textBuilder.AppendLine("请分析图片并提供相关上下文以帮助回答用户的问题。");

        // Create a collection to hold all content items (text + images)
        var contentItems = new ChatMessageContentItemCollection();
        contentItems.Add(new TextContent(textBuilder.ToString()));

        // Download and add images
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(miChanKernelProvider.GetGatewayUrl())
        };
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("AtField", miChanKernelProvider.GetAccessToken());

        foreach (var attachment in posts.SelectMany(post => post.Attachments))
        {
            try
            {
                if (attachment.MimeType?.StartsWith("image/") != true) continue;
                var imagePath = attachment.Url ?? $"/drive/files/{attachment.Id}";
                var imageBytes = await httpClient.GetByteArrayAsync(imagePath);
                if (imageBytes is { Length: > 0 })
                {
                    contentItems.Add(new ImageContent(imageBytes, attachment.MimeType));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to download image {FileId} for vision analysis", attachment.Id);
            }
        }

        // Create a ChatMessageContent with all items and add it to history
        var userMessage = new ChatMessageContent
        {
            Role = AuthorRole.User,
            Items = contentItems
        };
        chatHistory.Add(userMessage);

        return chatHistory;
    }

    #endregion

    #region Kernel and Plugin Helpers

    public Kernel? GetSnChanKernel()
    {
        return thoughtProvider.GetKernel();
    }

    public PromptExecutionSettings? CreateSnChanExecutionSettings()
    {
        return thoughtProvider.CreatePromptExecutionSettings();
    }

    public (string serviceId, ThoughtServiceModel? serviceInfo) GetSnChanServiceInfo()
    {
        var serviceId = thoughtProvider.GetServiceId();
        var serviceInfo = thoughtProvider.GetServiceInfo(serviceId);
        return (serviceId, serviceInfo);
    }

    [Experimental("SKEXP0050")]
    public Kernel GetMiChanKernel()
    {
        return miChanKernelProvider.GetKernel();
    }

    public PromptExecutionSettings CreateMiChanExecutionSettings()
    {
        return miChanKernelProvider.CreatePromptExecutionSettings();
    }

    public (string serviceId, ThoughtServiceModel? serviceInfo) GetMiChanServiceInfo()
    {
        var serviceId = miChanKernelProvider.GetServiceId();
        var serviceInfo = GetServiceInfoFromConfig(serviceId);
        return (serviceId, serviceInfo);
    }

    private ThoughtServiceModel? GetServiceInfoFromConfig(string serviceId)
    {
        try
        {
            var thinkingConfig = configuration.GetSection("Thinking");
            var serviceConfig = thinkingConfig.GetSection($"Services:{serviceId}");
            var provider = serviceConfig.GetValue<string>("Provider");
            var model = serviceConfig.GetValue<string>("Model");
            if (string.IsNullOrEmpty(provider) || string.IsNullOrEmpty(model))
                return null;

            return new ThoughtServiceModel
            {
                ServiceId = serviceId,
                Provider = provider,
                Model = model,
                BillingMultiplier = serviceConfig.GetValue<double>("BillingMultiplier"),
                PerkLevel = serviceConfig.GetValue<int>("PerkLevel")
            };
        }
        catch
        {
            return null;
        }
    }

    #endregion
}

#pragma warning restore SKEXP0050