using System.Diagnostics.CodeAnalysis;
#pragma warning disable SKEXP0050
using System.Text;
using System.Text.Json;
using DysonNetwork.Insight.MiChan;
using DysonNetwork.Insight.MiChan.Plugins;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using PaymentService = DysonNetwork.Shared.Proto.PaymentService;
using TransactionType = DysonNetwork.Shared.Proto.TransactionType;
using WalletService = DysonNetwork.Shared.Proto.WalletService;

namespace DysonNetwork.Insight.Thought;

public class ThoughtService(
    AppDatabase db,
    ICacheService cache,
    PaymentService.PaymentServiceClient paymentService,
    ThoughtProvider thoughtProvider,
    MiChanKernelProvider miChanKernelProvider,
    MiChanMemoryService miChanMemoryService,
    SolarNetworkApiClient apiClient,
    IServiceProvider serviceProvider,
    PostAnalysisService postAnalysisService,
    IConfiguration configuration,
    ILogger<ThoughtService> logger
)
{
    public async Task<SnThinkingSequence?> GetOrCreateSequenceAsync(
        Guid accountId,
        Guid? sequenceId,
        string? topic = null
    )
    {
        if (sequenceId.HasValue)
        {
            var seq = await db.ThinkingSequences.FindAsync(sequenceId.Value);
            if (seq == null || seq.AccountId != accountId)
                return null;
            return seq;
        }
        else
        {
            var seq = new SnThinkingSequence { AccountId = accountId, Topic = topic };
            db.ThinkingSequences.Add(seq);
            await db.SaveChangesAsync();
            return seq;
        }
    }

    /// <summary>
    /// Memorizes a thought sequence using the embedding service for semantic search.
    /// </summary>
    public async Task MemorizeSequenceAsync(
        SnThinkingSequence sequence,
        Dictionary<string, object>? additionalContext = null)
    {
        try
        {
            var memoryContext = new Dictionary<string, object>
            {
                ["sequence_id"] = sequence.Id,
                ["account_id"] = sequence.AccountId,
                ["topic"] = sequence.Topic ?? "No topic",
                ["total_tokens"] = sequence.TotalToken,
                ["paid_tokens"] = sequence.PaidToken,
                ["created_at"] = sequence.CreatedAt,
                ["updated_at"] = sequence.UpdatedAt,
                ["timestamp"] = DateTime.UtcNow
            };

            // Add any additional context
            if (additionalContext != null)
            {
                foreach (var kvp in additionalContext)
                {
                    memoryContext[kvp.Key] = kvp.Value;
                }
            }

            await miChanMemoryService.StoreInteractionAsync(
                type: "thought_sequence",
                contextId: sequence.Id.ToString(),
                context: memoryContext,
                memory: new Dictionary<string, object>
                {
                    ["sequence_id"] = sequence.Id,
                    ["topic"] = sequence.Topic ?? "No topic",
                    ["account_id"] = sequence.AccountId
                }
            );

            logger.LogInformation(
                "Memorized thought sequence {SequenceId} for account {AccountId} with topic: {Topic}",
                sequence.Id,
                sequence.AccountId,
                sequence.Topic ?? "No topic"
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error memorizing thought sequence {SequenceId}", sequence.Id);
        }
    }

    /// <summary>
    /// Gets or creates a thought sequence and memorizes it using the embedding service.
    /// Only memorizes if a new sequence was created (not when an existing one is retrieved).
    /// </summary>
    public async Task<SnThinkingSequence?> GetOrCreateAndMemorizeSequenceAsync(
        Guid accountId,
        Guid? sequenceId = null,
        string? topic = null,
        Dictionary<string, object>? additionalContext = null)
    {
        // If sequenceId is provided, just retrieve the existing sequence without memorizing
        if (sequenceId.HasValue)
        {
            var existingSequence = await GetOrCreateSequenceAsync(accountId, sequenceId, topic);
            return existingSequence;
        }

        // Create a new sequence
        var newSequence = await GetOrCreateSequenceAsync(accountId, null, topic);
        if (newSequence != null)
        {
            // Only memorize newly created sequences to avoid duplicates
            await MemorizeSequenceAsync(newSequence, additionalContext);
        }
        return newSequence;
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
        string? botName = null,
        bool memorize = false
    )
    {
        // Approximate token count (1 token ≈ 4 characters for GPT-like models)
        var totalChars = parts.Sum(part =>
            (part.Type == ThinkingMessagePartType.Text ? part.Text?.Length : 0) ?? 0 +
            (part.Type == ThinkingMessagePartType.FunctionCall ? part.FunctionCall?.Arguments.Length : 0) ?? 0
        );
        var tokenCount = totalChars / 4;

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

        await db.SaveChangesAsync();

        // Invalidate cache for this sequence's thoughts
        await cache.RemoveGroupAsync($"sequence:{sequence.Id}");

        // Memorize the thought if requested
        if (memorize)
        {
            await MemorizeThoughtAsync(thought, sequence);
        }

        return thought;
    }

    /// <summary>
    /// Memorizes a thought using the embedding service for semantic search.
    /// </summary>
    public async Task MemorizeThoughtAsync(
        SnThinkingThought thought,
        SnThinkingSequence? sequence = null,
        Dictionary<string, object>? additionalContext = null)
    {
        try
        {
            // Get sequence info if not provided
            sequence ??= await db.ThinkingSequences.FindAsync(thought.SequenceId);
            if (sequence == null)
            {
                logger.LogWarning("Cannot memorize thought {ThoughtId}: sequence not found", thought.Id);
                return;
            }

            // Extract text content from thought parts
            var textContent = new StringBuilder();
            foreach (var part in thought.Parts)
            {
                switch (part.Type)
                {
                    case ThinkingMessagePartType.Text when !string.IsNullOrEmpty(part.Text):
                        textContent.AppendLine(part.Text);
                        break;
                    case ThinkingMessagePartType.FunctionCall when part.FunctionCall != null:
                        textContent.AppendLine($"[Function Call: {part.FunctionCall.PluginName}.{part.FunctionCall.Name}]");
                        if (!string.IsNullOrEmpty(part.FunctionCall.Arguments))
                            textContent.AppendLine($"Arguments: {part.FunctionCall.Arguments}");
                        break;
                    case ThinkingMessagePartType.FunctionResult when part.FunctionResult != null:
                        textContent.AppendLine($"[Function Result: {part.FunctionResult.FunctionName}]");
                        var result = part.FunctionResult.Result?.ToString() ?? "null";
                        textContent.AppendLine($"Result: {result}");
                        break;
                }
            }

            var content = textContent.ToString().Trim();
            if (string.IsNullOrEmpty(content))
            {
                logger.LogDebug("Thought {ThoughtId} has no text content to memorize", thought.Id);
                return;
            }

            var memoryContext = new Dictionary<string, object>
            {
                ["thought_id"] = thought.Id,
                ["sequence_id"] = thought.SequenceId,
                ["account_id"] = sequence.AccountId,
                ["role"] = thought.Role.ToString(),
                ["content"] = content.Length > 2000 ? content[..2000] + "..." : content,
                ["token_count"] = thought.TokenCount,
                ["model"] = thought.ModelName ?? "unknown",
                ["bot_name"] = thought.BotName ?? "unknown",
                ["created_at"] = thought.CreatedAt,
                ["timestamp"] = DateTime.UtcNow
            };

            // Add any additional context
            if (additionalContext != null)
            {
                foreach (var kvp in additionalContext)
                {
                    memoryContext[kvp.Key] = kvp.Value;
                }
            }

            await miChanMemoryService.StoreInteractionAsync(
                type: "thought",
                contextId: thought.SequenceId.ToString(),
                context: memoryContext,
                memory: new Dictionary<string, object>
                {
                    ["thought_id"] = thought.Id,
                    ["sequence_id"] = thought.SequenceId,
                    ["role"] = thought.Role.ToString(),
                    ["topic"] = sequence.Topic ?? "No topic"
                }
            );

            logger.LogDebug(
                "Memorized thought {ThoughtId} from sequence {SequenceId} with {TokenCount} tokens",
                thought.Id,
                thought.SequenceId,
                thought.TokenCount
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error memorizing thought {ThoughtId}", thought.Id);
        }
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
            .OrderByDescending(s => s.CreatedAt)
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
        var sequence = await db.ThinkingSequences.FindAsync(sequenceId);
        if (sequence != null)
        {
            db.ThinkingSequences.Remove(sequence);
            await db.SaveChangesAsync();
        }
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
        List<string> acceptProposals)
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
        string contextId)
    {
        // Load personality
        var personality = PersonalityLoader.LoadPersonality(
            configuration.GetValue<string>("MiChan:PersonalityFile"),
            configuration.GetValue<string>("MiChan:Personality") ?? "",
            logger);

        // Retrieve relevant memories before thinking
        var relevantMemories = await miChanMemoryService.SearchSimilarInteractionsAsync(
            userMessage,
            limit: 5,
            minSimilarity: 0.7);

        // For non-superusers, MiChan decides whether to execute actions
        var isSuperuser = currentUser.IsSuperuser;

        // Decision gate for non-superusers
        if (!isSuperuser)
        {
            var decisionPromptBuilder = new StringBuilder();
            decisionPromptBuilder.AppendLine($"用户请求你：\"{userMessage}\"");
            decisionPromptBuilder.AppendLine();
            decisionPromptBuilder.AppendLine("你有以下可用工具：");
            decisionPromptBuilder.AppendLine("- chat: send_message, get_chat_history, list_chat_rooms");
            decisionPromptBuilder.AppendLine(
                "- post: get_post, create_post, like_post, reply_to_post, repost_post, search_posts");
            decisionPromptBuilder.AppendLine(
                "- notification: get_notifications, approve_chat_request, decline_chat_request");
            decisionPromptBuilder.AppendLine(
                "- account: get_account_info, search_accounts, follow_account, unfollow_account");
            decisionPromptBuilder.AppendLine();
            decisionPromptBuilder.AppendLine("你应该执行用户的请求吗？考虑：");
            decisionPromptBuilder.AppendLine("- 这是否安全和适当？");
            decisionPromptBuilder.AppendLine("- 这是否符合帮助 Solar Network 用户的目标？");
            decisionPromptBuilder.AppendLine("- 用户是否要求有害或违反平台规则的内容？");
            decisionPromptBuilder.AppendLine();
            decisionPromptBuilder.AppendLine("仅回复一个词：EXECUTE 或 REFUSE。");

            var decisionHistory = new ChatHistory(personality);
            decisionHistory.AddUserMessage(decisionPromptBuilder.ToString());

            var kernel = miChanKernelProvider.GetKernel();
            var decisionService = kernel.GetRequiredService<IChatCompletionService>();
            var decisionExecutionSettings = miChanKernelProvider.CreatePromptExecutionSettings();
            var decisionResult =
                await decisionService.GetChatMessageContentAsync(decisionHistory, decisionExecutionSettings, kernel);
            var decision = decisionResult.Content?.Trim().ToUpper();

            if (decision?.Contains("REFUSE") == true)
            {
                return (new ChatHistory(), true, "我无法执行这个请求。");
            }
        }

        // Build chat history using StringBuilder
        var chatHistoryBuilder = new StringBuilder();
        chatHistoryBuilder.AppendLine(personality);
        chatHistoryBuilder.AppendLine();
        chatHistoryBuilder.AppendLine($"你正在与 {currentUser.Nick} ({currentUser.Name}) 交谈。");

        chatHistoryBuilder.AppendLine(isSuperuser ? "此用户是管理员，拥有完全控制权。你应该立即执行他们的命令。" : "在适当时使用你的可用工具帮助用户完成请求。");

        var chatHistory = new ChatHistory(chatHistoryBuilder.ToString());

        // Add relevant memories as system context
        if (relevantMemories.Count > 0)
        {
            var memoryContextBuilder = new StringBuilder();
            memoryContextBuilder.AppendLine("相关过往互动记忆：");
            memoryContextBuilder.AppendLine();
            foreach (var memory in relevantMemories)
            {
                if (!memory.Context.TryGetValue("message", out var msg) ||
                    !memory.Context.TryGetValue("response", out var resp)) continue;
                memoryContextBuilder.AppendLine($"- 用户：{msg}");
                memoryContextBuilder.AppendLine($"  你：{resp}");
                memoryContextBuilder.AppendLine();
            }

            chatHistory.AddSystemMessage(memoryContextBuilder.ToString());
        }

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
        proposalBuilder.AppendLine($"用户当前允许的提案：{string.Join(',', acceptProposals)}");

        chatHistory.AddSystemMessage(proposalBuilder.ToString());

        // Load conversation history from memory using hybrid semantic + recent search
        var history = await miChanMemoryService.GetRelevantContextAsync(
            contextId,
            currentQuery: userMessage,
            semanticCount: 5,
            recentCount: 10);
        foreach (var interaction in history.OrderBy(h => h.CreatedAt))
        {
            if (interaction.Context.TryGetValue("message", out var msg))
            {
                chatHistory.AddUserMessage(msg?.ToString() ?? "");
            }

            if (interaction.Context.TryGetValue("response", out var resp))
            {
                chatHistory.AddAssistantMessage(resp?.ToString() ?? "");
            }
        }

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

        chatHistory.AddUserMessage(userMessage);

        return (chatHistory, false, null);
    }

    public async Task StoreMiChanInteractionAsync(
        string contextId,
        string userMessage,
        string response,
        bool isSuperuser)
    {
        await miChanMemoryService.StoreInteractionAsync(
            "thought",
            contextId,
            new Dictionary<string, object>
            {
                ["message"] = userMessage,
                ["response"] = response,
                ["timestamp"] = DateTime.UtcNow,
                ["is_superuser"] = isSuperuser
            }
        );
    }

    #endregion

    #region Vision Analysis

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

    public void EnsureMiChanPluginsRegistered(Kernel kernel)
    {
        var chatPlugin = serviceProvider.GetRequiredService<ChatPlugin>();
        var postPlugin = serviceProvider.GetRequiredService<PostPlugin>();
        var notificationPlugin = serviceProvider.GetRequiredService<NotificationPlugin>();
        var accountPlugin = serviceProvider.GetRequiredService<AccountPlugin>();

        if (!kernel.Plugins.Contains("chat"))
            kernel.Plugins.AddFromObject(chatPlugin, "chat");
        if (!kernel.Plugins.Contains("post"))
            kernel.Plugins.AddFromObject(postPlugin, "post");
        if (!kernel.Plugins.Contains("notification"))
            kernel.Plugins.AddFromObject(notificationPlugin, "notification");
        if (!kernel.Plugins.Contains("account"))
            kernel.Plugins.AddFromObject(accountPlugin, "account");
    }

    #endregion
}

#pragma warning restore SKEXP0050