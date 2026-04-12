using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DysonNetwork.Insight.MiChan;
using DysonNetwork.Insight.Services;
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
using NodaTime.Extensions;

#pragma warning disable SKEXP0050

namespace DysonNetwork.Insight.Thought;

public class ThoughtService(
    AppDatabase db,
    ICacheService cache,
    DyPaymentService.DyPaymentServiceClient paymentService,
    ThoughtProvider thoughtProvider,
    MiChanKernelProvider miChanKernelProvider,
    SolarNetworkApiClient apiClient,
    PostAnalysisService postAnalysisService,
    IConfiguration configuration,
    MemoryService memoryService,
    RemoteRingService remoteRingService,
    IConfiguration configGlobal,
    ILocalizationService localizer,
    UserProfileService userProfileService,
    TokenCountingService tokenCounter,
    FreeQuotaService freeQuotaService,
    ILogger<ThoughtService> logger,
    RemoteAccountService accounts
)
{
    private const string MiChanBotName = "michan";
    private const string MiChanCompactionSummaryKind = "compaction";
    private const string MiChanSummaryKindMetadataKey = "summary_kind";
    private const string MiChanCoveredThroughThoughtIdMetadataKey = "covered_through_thought_id";
    private const int MiChanCompactionThresholdTokens = 8000;
    private const int MiChanRecentHistoryTokenBudget = 2500;
    private const int MiChanMinRecentThoughts = 8;
    private const int MiChanCompactionChunkTokenBudget = 3000;
    private const int MiChanMaxThoughtWindowTokens = 6000;

    public sealed record MiChanSequenceResolutionResult(
        SnThinkingSequence? Sequence,
        bool Created,
        string? ErrorMessage = null
    );

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

    public async Task<SnThinkingSequence?> ResolveSequenceForOwnerAsync(Guid accountId, Guid sequenceId)
    {
        var sequence = await db.ThinkingSequences
            .FirstOrDefaultAsync(s => s.Id == sequenceId && s.AccountId == accountId);
        if (sequence != null)
        {
            return sequence;
        }

        var deletedSequence = await db.ThinkingSequences
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == sequenceId && s.AccountId == accountId);
        if (deletedSequence?.DeletedAt == null)
        {
            return null;
        }

        var hasMiChanThoughts = await db.ThinkingThoughts
            .IgnoreQueryFilters()
            .AnyAsync(t => t.SequenceId == sequenceId && t.BotName == MiChanBotName);
        if (!hasMiChanThoughts)
        {
            return null;
        }

        var hasSnChanThoughts = await db.ThinkingThoughts
            .IgnoreQueryFilters()
            .AnyAsync(t => t.SequenceId == sequenceId && t.BotName == "snchan");
        if (hasSnChanThoughts)
        {
            return null;
        }

        return await GetCanonicalMiChanSequenceAsync(accountId);
    }

    public async Task<MiChanUserProfile> TouchMiChanUserProfileAsync(Guid accountId)
    {
        return await userProfileService.TouchInteractionAsync(accountId);
    }

    public async Task<SnThinkingSequence?> GetCanonicalMiChanSequenceAsync(Guid accountId)
    {
        return await db.ThinkingSequences
            .Where(s => s.AccountId == accountId)
            .Where(s => db.ThinkingThoughts.Any(t => t.SequenceId == s.Id && t.BotName == MiChanBotName))
            .OrderByDescending(s => s.LastMessageAt != default ? s.LastMessageAt : s.CreatedAt)
            .ThenByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<bool> IsCanonicalMiChanSequenceAsync(Guid accountId, Guid sequenceId)
    {
        var canonicalSequence = await GetCanonicalMiChanSequenceAsync(accountId);
        return canonicalSequence?.Id == sequenceId;
    }

    public async Task<MiChanSequenceResolutionResult> ResolveMiChanSequenceAsync(
        Guid accountId,
        Guid? requestedSequenceId = null,
        string? topic = null)
    {
        var canonicalSequence = await GetCanonicalMiChanSequenceAsync(accountId);

        if (canonicalSequence != null)
        {
            if (requestedSequenceId.HasValue && requestedSequenceId.Value != canonicalSequence.Id)
            {
                return new MiChanSequenceResolutionResult(
                    null,
                    false,
                    "MiChan now uses a unified conversation. Please continue with the canonical MiChan sequence."
                );
            }

            return new MiChanSequenceResolutionResult(canonicalSequence, false);
        }

        if (requestedSequenceId.HasValue)
        {
            return new MiChanSequenceResolutionResult(
                null,
                false,
                "MiChan now uses a unified conversation. Start without sequenceId to create the canonical MiChan thread."
            );
        }

        var now = SystemClock.Instance.GetCurrentInstant();
        var sequence = new SnThinkingSequence
        {
            AccountId = accountId,
            Topic = topic,
            LastMessageAt = now,
            LastFreeQuotaResetAt = now
        };

        db.ThinkingSequences.Add(sequence);
        await db.SaveChangesAsync();

        return new MiChanSequenceResolutionResult(sequence, true);
    }

    public async Task<int> MergeHistoricMiChanSequencesAsync(CancellationToken cancellationToken = default)
    {
        var mergedSequenceCount = 0;

        var candidateAccountIds = await db.ThinkingSequences
            .Where(s => db.ThinkingThoughts.Any(t => t.SequenceId == s.Id && t.BotName == MiChanBotName))
            .Select(s => s.AccountId)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var accountId in candidateAccountIds)
        {
            var canonicalSequence = await GetCanonicalMiChanSequenceAsync(accountId);
            if (canonicalSequence == null)
                continue;

            var sourceSequences = await db.ThinkingSequences
                .Where(s => s.AccountId == accountId && s.Id != canonicalSequence.Id)
                .Where(s => db.ThinkingThoughts.Any(t => t.SequenceId == s.Id && t.BotName == MiChanBotName))
                .Where(s => !db.ThinkingThoughts.Any(t => t.SequenceId == s.Id && t.BotName == "snchan"))
                .OrderBy(s => s.CreatedAt)
                .ToListAsync(cancellationToken);

            if (sourceSequences.Count == 0)
                continue;

            foreach (var sourceSequence in sourceSequences)
            {
                var thoughtCount = await db.ThinkingThoughts
                    .Where(t => t.SequenceId == sourceSequence.Id)
                    .CountAsync(cancellationToken);

                if (thoughtCount == 0)
                    continue;

                await db.ThinkingThoughts
                    .Where(t => t.SequenceId == sourceSequence.Id)
                    .ExecuteUpdateAsync(
                        update => update.SetProperty(t => t.SequenceId, canonicalSequence.Id),
                        cancellationToken
                    );

                canonicalSequence.TotalToken += sourceSequence.TotalToken;
                canonicalSequence.PaidToken += sourceSequence.PaidToken;
                canonicalSequence.FreeTokens += sourceSequence.FreeTokens;
                canonicalSequence.AgentInitiated = canonicalSequence.AgentInitiated || sourceSequence.AgentInitiated;

                if (sourceSequence.UserLastReadAt.HasValue &&
                    (!canonicalSequence.UserLastReadAt.HasValue ||
                     sourceSequence.UserLastReadAt.Value > canonicalSequence.UserLastReadAt.Value))
                {
                    canonicalSequence.UserLastReadAt = sourceSequence.UserLastReadAt;
                }

                if (sourceSequence.LastMessageAt > canonicalSequence.LastMessageAt)
                {
                    canonicalSequence.LastMessageAt = sourceSequence.LastMessageAt;
                }

                sourceSequence.TotalToken = 0;
                sourceSequence.PaidToken = 0;
                sourceSequence.FreeTokens = 0;
                sourceSequence.DailyFreeTokensUsed = 0;
                sourceSequence.DeletedAt = SystemClock.Instance.GetCurrentInstant();

                mergedSequenceCount++;

                await cache.RemoveGroupAsync($"sequence:{sourceSequence.Id}");
            }

            await db.SaveChangesAsync(cancellationToken);
            await cache.RemoveGroupAsync($"sequence:{canonicalSequence.Id}");
        }

        if (mergedSequenceCount > 0)
        {
            logger.LogInformation("Merged {Count} historic MiChan sequences into canonical threads.", mergedSequenceCount);
        }

        return mergedSequenceCount;
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
        string? botName = null
    )
    {
        var tokenCount = CalculateTokenCount(parts, model);

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

        if (role == ThinkingThoughtRole.Assistant)
            sequence.TotalToken += tokenCount;

        if (role == ThinkingThoughtRole.Assistant && tokenCount > 0)
        {
            var consumedFromFree = await freeQuotaService.ConsumeFreeTokensAsync(
                sequence.AccountId, tokenCount);

            if (consumedFromFree > 0)
            {
                sequence.PaidToken += consumedFromFree;
                sequence.FreeTokens += consumedFromFree;
                logger.LogDebug("Consumed {Tokens} tokens from free quota for account {AccountId}",
                    consumedFromFree, sequence.AccountId);
            }
        }

        sequence.LastMessageAt = now;

        if (role == ThinkingThoughtRole.User)
            sequence.UserLastReadAt = now;

        await db.SaveChangesAsync();

        await cache.RemoveGroupAsync($"sequence:{sequence.Id}");

        return thought;
    }

    public async Task<SnThinkingThought> SaveMiChanCompactionThoughtAsync(
        SnThinkingSequence sequence,
        string summary,
        Guid coveredThroughThoughtId)
    {
        var thought = new SnThinkingThought
        {
            SequenceId = sequence.Id,
            Role = ThinkingThoughtRole.Assistant,
            TokenCount = 0,
            ModelName = "michan-compaction",
            BotName = MiChanBotName,
            Parts =
            [
                new SnThinkingMessagePart
                {
                    Type = ThinkingMessagePartType.Text,
                    Text = summary,
                    Metadata = new Dictionary<string, object>
                    {
                        [MiChanSummaryKindMetadataKey] = MiChanCompactionSummaryKind,
                        [MiChanCoveredThroughThoughtIdMetadataKey] = coveredThroughThoughtId.ToString()
                    }
                }
            ]
        };

        db.ThinkingThoughts.Add(thought);
        await db.SaveChangesAsync();
        await cache.RemoveGroupAsync($"sequence:{sequence.Id}");

        return thought;
    }

    /// <summary>
    /// Calculates the total token count for a list of message parts using accurate tokenization.
    /// </summary>
    private int CalculateTokenCount(List<SnThinkingMessagePart> parts, string? modelName)
    {
        var totalTokens = 0;

        foreach (var part in parts)
        {
            switch (part.Type)
            {
                case ThinkingMessagePartType.Text:
                    if (!string.IsNullOrEmpty(part.Text))
                    {
                        totalTokens += tokenCounter.CountTokens(part.Text, modelName);
                    }

                    break;

                case ThinkingMessagePartType.FunctionCall:
                    if (part.FunctionCall != null)
                    {
                        // Count function name and arguments
                        totalTokens += tokenCounter.CountTokens(part.FunctionCall.Name, modelName);
                        totalTokens += tokenCounter.CountTokens(part.FunctionCall.Arguments, modelName);
                    }

                    break;

                case ThinkingMessagePartType.FunctionResult:
                    if (part.FunctionResult != null)
                    {
                        // Count function result - convert to string if needed
                        var resultText = part.FunctionResult.Result as string
                                         ?? JsonSerializer.Serialize(part.FunctionResult.Result);
                        totalTokens += tokenCounter.CountTokens(resultText, modelName);
                    }

                    break;
            }
        }

        return totalTokens;
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

    public async Task<(List<SnThinkingThought> thoughts, bool hasMore)> GetVisibleThoughtsPageAsync(
        SnThinkingSequence sequence,
        int offset,
        int take)
    {
        const int minBatchSize = 50;
        var batchSize = Math.Max(minBatchSize, take * 2);
        var rawOffset = 0;
        var visibleSkipped = 0;
        var visibleThoughts = new List<SnThinkingThought>(take + 1);

        while (visibleThoughts.Count <= take)
        {
            var batch = await db.ThinkingThoughts
                .Where(t => t.SequenceId == sequence.Id)
                .OrderByDescending(t => t.CreatedAt)
                .ThenByDescending(t => t.Id)
                .Skip(rawOffset)
                .Take(batchSize)
                .ToListAsync();

            if (batch.Count == 0)
            {
                break;
            }

            rawOffset += batch.Count;

            foreach (var thought in batch)
            {
                if (IsMiChanCompactionThought(thought))
                {
                    continue;
                }

                if (visibleSkipped < offset)
                {
                    visibleSkipped++;
                    continue;
                }

                visibleThoughts.Add(thought);
                if (visibleThoughts.Count > take)
                {
                    break;
                }
            }

            if (batch.Count < batchSize)
            {
                break;
            }
        }

        var hasMore = visibleThoughts.Count > take;
        if (hasMore)
        {
            visibleThoughts.RemoveAt(visibleThoughts.Count - 1);
        }

        return (visibleThoughts, hasMore);
    }

    public bool IsMiChanCompactionThought(SnThinkingThought thought)
    {
        var textPart = thought.Parts.FirstOrDefault(p => p.Type == ThinkingMessagePartType.Text);
        return string.Equals(thought.BotName, MiChanBotName, StringComparison.OrdinalIgnoreCase)
               && TryGetMetadataString(textPart?.Metadata, MiChanSummaryKindMetadataKey, out var kind)
               && string.Equals(kind, MiChanCompactionSummaryKind, StringComparison.OrdinalIgnoreCase);
    }

    public List<SnThinkingThought> FilterVisibleThoughts(IEnumerable<SnThinkingThought> thoughts)
    {
        return thoughts.Where(thought => !IsMiChanCompactionThought(thought)).ToList();
    }

    internal (List<SnThinkingThought> thoughts, bool hasMore) SliceVisibleThoughtsForTests(
        IEnumerable<SnThinkingThought> orderedThoughts,
        int offset,
        int take)
    {
        var visibleThoughts = FilterVisibleThoughts(orderedThoughts)
            .Skip(offset)
            .Take(take + 1)
            .ToList();
        var hasMore = visibleThoughts.Count > take;
        if (hasMore)
        {
            visibleThoughts.RemoveAt(visibleThoughts.Count - 1);
        }

        return (visibleThoughts, hasMore);
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

            var totalUnpaidTokens = accountGroup.Sum(s => s.TotalToken - s.PaidToken - s.FreeTokens);
            var cost = (long)Math.Ceiling(totalUnpaidTokens / 10.0);

            if (cost == 0)
                continue;

            try
            {
                var accountInfo = await accounts.GetAccount(accountId);
                await paymentService.CreateTransactionWithAccountAsync(
                    new DyCreateTransactionWithAccountRequest
                    {
                        PayerAccountId = accountId.ToString(),
                        Currency = WalletCurrency.SourcePoint,
                        Amount = cost.ToString(),
                        Remarks = localizer.Get("agentBillName", accountInfo.Language),
                        Type = DyTransactionType.System,
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
        var cost = (long)Math.Ceiling(totalUnpaidTokens / 100.0);

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
            var accountInfo = await accounts.GetAccount(accountId);
            await paymentService.CreateTransactionWithAccountAsync(
                new DyCreateTransactionWithAccountRequest
                {
                    PayerAccountId = accountId.ToString(),
                    Currency = WalletCurrency.SourcePoint,
                    Amount = cost.ToString(),
                    Remarks = localizer.Get("agentBillName", accountInfo.Language),
                    Type = DyTransactionType.System,
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
        string botName = "michan"
    )
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var isMiChan = botName.Equals(MiChanBotName, StringComparison.OrdinalIgnoreCase);

        // Generate a topic if not provided
        if (string.IsNullOrEmpty(topic))
        {
            topic = await GenerateTopicAsync(initialMessage, useMiChan: isMiChan);
            if (string.IsNullOrEmpty(topic))
            {
                topic = "New conversation";
            }
        }

        SnThinkingSequence sequence;
        var isNewSequence = false;

        if (isMiChan)
        {
            var resolution = await ResolveMiChanSequenceAsync(accountId, topic: topic);
            if (resolution.Sequence == null)
            {
                return null;
            }

            sequence = resolution.Sequence;
            isNewSequence = resolution.Created;

            if (resolution.Created)
            {
                sequence.AgentInitiated = true;
                sequence.Topic = topic;
                sequence.LastMessageAt = now;
                await db.SaveChangesAsync();
            }
            else if (string.IsNullOrWhiteSpace(sequence.Topic) && !string.IsNullOrWhiteSpace(topic))
            {
                sequence.Topic = topic;
                await db.SaveChangesAsync();
            }
        }
        else
        {
            sequence = new SnThinkingSequence
            {
                AccountId = accountId,
                Topic = topic,
                AgentInitiated = true,
                LastMessageAt = now,
                LastFreeQuotaResetAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };

            db.ThinkingSequences.Add(sequence);
            await db.SaveChangesAsync();
            isNewSequence = true;
        }

        // Save the initial message as a thought from the assistant
        if (isMiChan)
        {
            await SaveThoughtAsync(
                sequence,
                [
                    new SnThinkingMessagePart
                    {
                        Type = ThinkingMessagePartType.Text,
                        Text = initialMessage
                    }
                ],
                ThinkingThoughtRole.Assistant,
                model: botName,
                botName: botName
            );
            await TouchMiChanUserProfileAsync(accountId);
        }
        else
        {
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
                TokenCount = tokenCounter.CountTokens(initialMessage),
                ModelName = botName,
                BotName = botName,
                CreatedAt = now,
                UpdatedAt = now
            };

            db.ThinkingThoughts.Add(thought);
            sequence.TotalToken += thought.TokenCount;
            await db.SaveChangesAsync();
        }

        // Send push notification to the user
        try
        {
            // Use default locale if not provided
            var effectiveLocale = locale ?? "en";

            var agentNameKey = botName.Equals("snchan", StringComparison.CurrentCultureIgnoreCase)
                ? "agentNameSnChan"
                : "agentNameMiChan";
            var agentName = localizer.Get(agentNameKey, effectiveLocale);
            var notificationTitle = localizer.Get("agentConversationStartedTitle", effectiveLocale, new { agentName });
            var notificationBody = localizer.Get("agentConversationStartedBody", effectiveLocale,
                new { agentName, message = initialMessage });

            // Create meta with sequence ID for deep linking
            var meta = new Dictionary<string, object?>
            {
                ["sequence_id"] = sequence.Id.ToString(),
                ["type"] = "insight.conversations.new"
            };
            var metaBytes = JsonSerializer.SerializeToUtf8Bytes(meta);

            await remoteRingService.SendPushNotificationToUser(
                accountId.ToString(),
                "insight.conversations.new",
                notificationTitle,
                null,
                notificationBody,
                metaBytes,
                actionUri: $"/thoughts/{sequence.Id}",
                isSilent: false,
                isSavable: true
            );

            logger.LogInformation(
                isNewSequence
                    ? "Agent-initiated conversation created for account {AccountId} with sequence {SequenceId}. Notification sent."
                    : "Agent-initiated message appended for account {AccountId} on sequence {SequenceId}. Notification sent.",
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
        DyAccount currentUser,
        string? userMessage,
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
        systemPromptBuilder.AppendLine("非常重要：在读取记忆后，认清楚记忆是不是属于该用户的，再做出答复。");
        systemPromptBuilder.AppendLine();
        systemPromptBuilder.AppendLine("你可以使用以下网络搜索工具获取最新信息：");
        systemPromptBuilder.AppendLine("- webSearch.fetch_url: 获取指定 URL 的页面内容和标题（适合获取文章、文档的完整内容）");
        systemPromptBuilder.AppendLine("- webSearch.duckduckgo_instant_search: DuckDuckGo 即时搜索，适合快速获取事实、定义、摘要");
        systemPromptBuilder.AppendLine("- webSearch.duckduckgo_search: DuckDuckGo 完整搜索，返回完整链接和摘要（适合查找特定网站）");
        systemPromptBuilder.AppendLine("当你需要获取最新信息、验证事实、了解不熟悉的主题、或用户询问需要实时数据的问题时，主动使用网络搜索。");

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
            var postTexts = new List<string>();

            foreach (var postId in attachedPosts)
            {
                try
                {
                    if (!Guid.TryParse(postId, out var postGuid)) continue;
                    var post = await apiClient.GetAsync<SnPost>("sphere", $"/posts/{postGuid}");
                    if (post == null) continue;
                    postTexts.Add(PostAnalysisService.BuildPostPromptSnippet(post));
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

        chatHistory.AddUserMessage(userMessage ?? "用户只添加了文件没有文字说明。");

        return chatHistory;
    }

    #endregion

    #region MiChan Chat History Building

    [Experimental("SKEXP0050")]
    public async Task<(ChatHistory chatHistory, bool useVisionKernel)> BuildMiChanChatHistoryAsync(
        SnThinkingSequence sequence,
        DyAccount currentUser,
        string? userMessage,
        List<string>? attachedPosts,
        List<Dictionary<string, dynamic>>? attachedMessages,
        List<string> acceptProposals,
        List<SnCloudFileReferenceObject> attachments,
        Guid? currentThoughtId = null
    )
    {
        var buildStopwatch = Stopwatch.StartNew();
        // Load personality
        var personality = PersonalityLoader.LoadPersonality(
            configuration.GetValue<string>("MiChan:PersonalityFile"),
            configuration.GetValue<string>("MiChan:Personality") ?? "",
            logger);

        // Retrieve hot memories for context
        var hotMemories = await memoryService.GetHotMemory(
            Guid.Parse(currentUser.Id),
            userMessage ?? "",
            10);
        var userProfile = await userProfileService.GetOrCreateAsync(Guid.Parse(currentUser.Id));

        // For non-superusers, MiChan decides whether to execute actions
        var isSuperuser = currentUser.IsSuperuser;

        // Build chat history using StringBuilder
        var chatHistoryBuilder = new StringBuilder();
        chatHistoryBuilder.AppendLine(personality);
        chatHistoryBuilder.AppendLine();

        chatHistoryBuilder.AppendLine("你对该用户的结构化档案（优先级高于零散记忆，回复前先参考）：");
        chatHistoryBuilder.AppendLine(userProfile.ToPrompt());
        chatHistoryBuilder.AppendLine();

        // Add hot memories context
        if (hotMemories.Count > 0)
        {
            chatHistoryBuilder.AppendLine("与你相关的热点记忆（回复前优先复用这些上下文）：");
            foreach (var memory in hotMemories.Take(8))
                chatHistoryBuilder.AppendLine($"- {memory.ToPrompt()}");

            chatHistoryBuilder.AppendLine();
        }
        else
        {
            chatHistoryBuilder.AppendLine("当前没有命中的热点记忆。遇到需要背景、偏好、长期关系判断的问题时，先主动搜索记忆。");
            chatHistoryBuilder.AppendLine();
        }

        chatHistoryBuilder.AppendLine($"你正在与 {currentUser.Nick} (@{currentUser.Name}) ID 为 {currentUser.Id} 交谈。");

        var userTimeZone = currentUser.Profile?.TimeZone;
        AppendTimeContext(chatHistoryBuilder, userTimeZone);

        chatHistoryBuilder.AppendLine(isSuperuser ? "该用户是管理员，你应该更积极的考虑处理该用户的请求。" : "你有拒绝用户请求的权利。");
        chatHistoryBuilder.AppendLine();
        chatHistoryBuilder.AppendLine("核心行为要求：");
        chatHistoryBuilder.AppendLine("1. 在回答涉及用户偏好、过去对话、关系状态、未完成事项、延续话题时，优先参考结构化档案与热点记忆。");
        chatHistoryBuilder.AppendLine("2. 只要问题有一点可能依赖过往上下文，就先调用 search_memory 搜索，而不是靠猜。");
        chatHistoryBuilder.AppendLine("3. 当用户信息、印象、关系状态发生了稳定变化，优先更新 userProfile，再视情况补充 store_memory。");
        chatHistoryBuilder.AppendLine("4. 不要向用户暴露你在读取档案、搜索记忆或更新关系，直接自然回复。");
        chatHistoryBuilder.AppendLine();
        chatHistoryBuilder.AppendLine("在调用任何工具之前，你必须先确认自己拥有所有必需参数。");
        chatHistoryBuilder.AppendLine("如果缺少必需参数（例如 content、type 或 query），不要调用工具。应向用户提问以获取必要信息。");
        chatHistoryBuilder.AppendLine("严禁使用 null、空字符串或占位值调用工具。");
        chatHistoryBuilder.AppendLine();
        chatHistoryBuilder.AppendLine("记忆与档案使用策略：");
        chatHistoryBuilder.AppendLine("- 若用户正在延续之前的话题、提到'之前'、'上次'、'还记得吗'、偏好、习惯、关系感受，先 search_memory。");
        chatHistoryBuilder.AppendLine("- 若用户画像为空或过于粗糙，但当前对话提供了稳定新信息，使用 userProfile.update_user_profile 补全。");
        chatHistoryBuilder.AppendLine("- 若只是短期波动或瞬时情绪，不要过度修改长期画像。");
        chatHistoryBuilder.AppendLine("- relationship 的变化要谨慎，只有在互动明显支持时才调整 favorability、trust、intimacy。");
        chatHistoryBuilder.AppendLine();
        chatHistoryBuilder.AppendLine("当且仅当存在有价值的信息时，你可以调用 store_memory 工具保存记忆。");
        chatHistoryBuilder.AppendLine("调用 store_memory 时：");
        chatHistoryBuilder.AppendLine("  - 必须提供 content（非空字符串）");
        chatHistoryBuilder.AppendLine("  - 必须提供 type（非空字符串，例如 fact、user、context、summary）");
        chatHistoryBuilder.AppendLine("  - 如果无法确定 type，请先自行判断合理类型；若仍不确定，不要调用工具。");
        chatHistoryBuilder.AppendLine();
        chatHistoryBuilder.AppendLine("**不要等待用户要求才保存记忆** - 主动识别并保存任何有价值的信息。");
        chatHistoryBuilder.AppendLine("**你可以直接调用 store_memory 工具保存记忆，不需要询问用户是否确认或告知用户你正在保存。**");
        chatHistoryBuilder.AppendLine("**强制要求：调用 store_memory 时必须提供 content 参数（要保存的记忆内容），不能为空！**");
        chatHistoryBuilder.AppendLine("不要告诉用户你正在搜索记忆或保存记忆，直接根据记忆自然地回复。");
        chatHistoryBuilder.AppendLine("使用记忆工具时保持沉默，不要输出'让我查看一下记忆'之类的提示。");
        chatHistoryBuilder.AppendLine("非常重要：在读取记忆后，认清楚记忆是不是属于该用户的，再做出答复。");
        chatHistoryBuilder.AppendLine("你可以使用 userProfile.get_user_profile 查看当前用户档案。");
        chatHistoryBuilder.AppendLine("当你对用户形成更稳定的印象、关系判断、好感度变化或重要标签时，优先使用 userProfile.update_user_profile 或 userProfile.adjust_relationship 立即更新。");
        chatHistoryBuilder.AppendLine("favorability、trust、intimacy 的取值范围是 -100 到 100。只有在确实有依据时才调整这些值。");
        chatHistoryBuilder.AppendLine();
        chatHistoryBuilder.AppendLine("你可以使用以下网络搜索工具获取最新信息：");
        chatHistoryBuilder.AppendLine("- webSearch.fetch_url: 获取指定 URL 的页面内容和标题（适合获取文章、文档的完整内容）");
        chatHistoryBuilder.AppendLine("- webSearch.duckduckgo_instant_search: DuckDuckGo 即时搜索，适合快速获取事实、定义、摘要");
        chatHistoryBuilder.AppendLine("- webSearch.duckduckgo_search: DuckDuckGo 完整搜索，返回完整链接和摘要（适合查找特定网站）");
        chatHistoryBuilder.AppendLine("当你需要获取最新信息、验证事实、了解不熟悉的主题、或用户询问需要实时数据的问题时，主动使用网络搜索。");
        
        var chatHistory = new ChatHistory(chatHistoryBuilder.ToString());

        var orderedPreviousThoughts = await LoadMiChanHistoryForPromptAsync(sequence, currentThoughtId);

        var (compactedSummary, recentThoughts) = await PrepareMiChanHistoryAsync(
            sequence,
            orderedPreviousThoughts,
            Guid.Parse(currentUser.Id)
        );
        var recentThoughtTokens = recentThoughts.Sum(EstimateThoughtTokensForPrompt);
        logger.LogInformation(
            "Built MiChan prompt window for sequence {SequenceId} in {ElapsedMs}ms. historyThoughts={HistoryThoughtCount}, recentThoughts={RecentThoughtCount}, recentTokens={RecentThoughtTokens}, hasSummary={HasSummary}",
            sequence.Id,
            buildStopwatch.ElapsedMilliseconds,
            orderedPreviousThoughts.Count,
            recentThoughts.Count,
            recentThoughtTokens,
            !string.IsNullOrWhiteSpace(compactedSummary)
        );

        if (!string.IsNullOrWhiteSpace(compactedSummary))
        {
            chatHistory.AddSystemMessage("以下是你们较早对话的压缩摘要：\n" + compactedSummary);
        }

        // Add previous thoughts
        foreach (var thought in recentThoughts)
        {
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
        var useVisionKernel = false;
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
                useVisionKernel = true;
                var contentItems = new ChatMessageContentItemCollection { new TextContent("附加帖子的图片：") };
                var postsImages = postsWithImages.SelectMany(x => x.Attachments).ToList();
                BuildImageContent(postsImages).ForEach(content => contentItems.Add(content));
                chatHistory.AddUserMessage(contentItems);
            }
        }

        if (attachedMessages is { Count: > 0 })
        {
            chatHistory.AddUserMessage($"附加的聊天消息：{JsonSerializer.Serialize(attachedMessages)}");
        }

        // Handle direct attachments if provided
        if (attachments is { Count: > 0 })
        {
            var visionAvailable = postAnalysisService.IsVisionModelAvailable();

            useVisionKernel = attachments.Count > 0 && visionAvailable;
            logger.LogInformation(
                "Attachments item is not empty, and vision={UseVisionKernel}, attachmentsCount={AttachmentsCount}, available={VisionAvailable}",
                useVisionKernel, attachments.Count, visionAvailable);

            if (useVisionKernel)
            {
                var contentItems = new ChatMessageContentItemCollection { new TextContent("附加的图片文件：") };
                BuildImageContent(attachments).ForEach(content => contentItems.Add(content));

                chatHistory.AddUserMessage(contentItems);
            }
        }

        chatHistory.AddUserMessage(userMessage ?? "用户只添加了图片没有文字说明。");

        return (chatHistory, useVisionKernel);
    }

    private async Task<(string? summary, List<SnThinkingThought> recentThoughts)> PrepareMiChanHistoryAsync(
        SnThinkingSequence sequence,
        List<SnThinkingThought> orderedThoughts,
        Guid accountId)
    {
        var stopwatch = Stopwatch.StartNew();
        var (latestSummaryThought, rawThoughts, uncoveredThoughts) = ProjectMiChanHistoryWindowInternal(orderedThoughts);
        var latestSummaryText = GetThoughtText(latestSummaryThought);
        var originalCoveredThoughtId = GetCoveredThoughtId(latestSummaryThought);
        Guid? coveredThroughThoughtId = originalCoveredThoughtId;
        var uncoveredTokensBefore = uncoveredThoughts.Sum(EstimateThoughtTokensForPrompt);
        logger.LogInformation(
            "Preparing MiChan history for sequence {SequenceId}. rawThoughts={RawThoughtCount}, uncoveredThoughts={UncoveredThoughtCount}, uncoveredTokens={UncoveredTokens}, hasExistingSummary={HasExistingSummary}",
            sequence.Id,
            rawThoughts.Count,
            uncoveredThoughts.Count,
            uncoveredTokensBefore,
            !string.IsNullOrWhiteSpace(latestSummaryText)
        );

        if (ShouldCompactMiChanHistory(uncoveredThoughts))
        {
            var compactPrefix = SelectCompactionChunkPrefix(uncoveredThoughts);
            if (compactPrefix.Count > 0)
            {
                var compactPrefixTokens = compactPrefix.Sum(EstimateThoughtTokensForPrompt);
                logger.LogInformation(
                    "Compacting MiChan history for sequence {SequenceId}. compactThoughts={CompactThoughtCount}, compactTokens={CompactTokens}",
                    sequence.Id,
                    compactPrefix.Count,
                    compactPrefixTokens
                );
                var compactedSummary = await GenerateMiChanCompactionSummaryAsync(accountId, latestSummaryText, compactPrefix);
                if (!string.IsNullOrWhiteSpace(compactedSummary))
                {
                    latestSummaryText = compactedSummary;
                    coveredThroughThoughtId = compactPrefix[^1].Id;
                    uncoveredThoughts = uncoveredThoughts.Skip(compactPrefix.Count).ToList();
                    logger.LogInformation(
                        "Compacted MiChan history for sequence {SequenceId} in {ElapsedMs}ms. remainingThoughts={RemainingThoughtCount}",
                        sequence.Id,
                        stopwatch.ElapsedMilliseconds,
                        uncoveredThoughts.Count
                    );
                }
            }
        }

        uncoveredThoughts = ClampMiChanThoughtWindow(uncoveredThoughts, MiChanMaxThoughtWindowTokens);

        if (!string.IsNullOrWhiteSpace(latestSummaryText) &&
            coveredThroughThoughtId.HasValue &&
            coveredThroughThoughtId != originalCoveredThoughtId)
        {
            await SaveMiChanCompactionThoughtAsync(sequence, latestSummaryText, coveredThroughThoughtId.Value);
        }

        logger.LogInformation(
            "Prepared MiChan history for sequence {SequenceId} in {ElapsedMs}ms. finalThoughts={FinalThoughtCount}, finalTokens={FinalTokens}, savedSummary={SavedSummary}",
            sequence.Id,
            stopwatch.ElapsedMilliseconds,
            uncoveredThoughts.Count,
            uncoveredThoughts.Sum(EstimateThoughtTokensForPrompt),
            coveredThroughThoughtId != originalCoveredThoughtId
        );

        return (latestSummaryText, uncoveredThoughts);
    }

    internal (string? summary, List<SnThinkingThought> recentThoughts) ProjectMiChanHistoryWindowForTests(
        List<SnThinkingThought> orderedThoughts)
    {
        var (latestSummaryThought, _, uncoveredThoughts) = ProjectMiChanHistoryWindowInternal(orderedThoughts);
        return (GetThoughtText(latestSummaryThought), uncoveredThoughts);
    }

    internal bool ShouldCompactMiChanHistoryForTests(List<SnThinkingThought> thoughts)
    {
        return ShouldCompactMiChanHistory(thoughts);
    }

    internal List<SnThinkingThought> SelectCompactionPrefixForTests(List<SnThinkingThought> thoughts)
    {
        return SelectCompactionPrefix(thoughts);
    }

    internal List<SnThinkingThought> SelectCompactionChunkPrefixForTests(List<SnThinkingThought> thoughts)
    {
        return SelectCompactionChunkPrefix(thoughts);
    }

    internal List<SnThinkingThought> ClampMiChanThoughtWindowForTests(List<SnThinkingThought> thoughts, int tokenBudget)
    {
        return ClampMiChanThoughtWindow(thoughts, tokenBudget);
    }

    private (SnThinkingThought? latestSummaryThought, List<SnThinkingThought> rawThoughts, List<SnThinkingThought> uncoveredThoughts)
        ProjectMiChanHistoryWindowInternal(List<SnThinkingThought> orderedThoughts)
    {
        var latestSummaryThought = orderedThoughts.LastOrDefault(IsMiChanCompactionThought);
        var rawThoughts = orderedThoughts.Where(thought => !IsMiChanCompactionThought(thought)).ToList();
        var coveredIndex = FindCoveredThoughtIndex(rawThoughts, latestSummaryThought);
        var uncoveredThoughts = coveredIndex >= 0
            ? rawThoughts.Skip(coveredIndex + 1).ToList()
            : rawThoughts;

        return (latestSummaryThought, rawThoughts, uncoveredThoughts);
    }

    private async Task<List<SnThinkingThought>> LoadMiChanHistoryForPromptAsync(
        SnThinkingSequence sequence,
        Guid? currentThoughtId)
    {
        var stopwatch = Stopwatch.StartNew();
        var latestSummaryThought = await db.ThinkingThoughts
            .Where(t => t.SequenceId == sequence.Id)
            .Where(t => currentThoughtId == null || t.Id != currentThoughtId.Value)
            .Where(t => t.BotName == MiChanBotName && t.ModelName == "michan-compaction")
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .FirstOrDefaultAsync();
        if (latestSummaryThought == null)
        {
            var fullThoughts = await GetPreviousThoughtsAsync(sequence);
            return fullThoughts
                .OrderBy(t => t.CreatedAt)
                .ThenBy(t => t.Id)
                .Where(t => currentThoughtId == null || t.Id != currentThoughtId.Value)
                .ToList();
        }

        var textPart = latestSummaryThought.Parts.FirstOrDefault(part => part.Type == ThinkingMessagePartType.Text);
        if (!TryGetMetadataString(textPart?.Metadata, MiChanCoveredThroughThoughtIdMetadataKey, out var coveredThoughtIdText) ||
            !Guid.TryParse(coveredThoughtIdText, out var coveredThoughtId))
        {
            var fullThoughts = await GetPreviousThoughtsAsync(sequence);
            return fullThoughts
                .OrderBy(t => t.CreatedAt)
                .ThenBy(t => t.Id)
                .Where(t => currentThoughtId == null || t.Id != currentThoughtId.Value)
                .ToList();
        }

        var coveredThought = await db.ThinkingThoughts
            .Where(t => t.Id == coveredThoughtId)
            .Select(t => new { t.Id, t.CreatedAt })
            .FirstOrDefaultAsync();

        if (coveredThought == null)
        {
            var fullThoughts = await GetPreviousThoughtsAsync(sequence);
            return fullThoughts
                .OrderBy(t => t.CreatedAt)
                .ThenBy(t => t.Id)
                .Where(t => currentThoughtId == null || t.Id != currentThoughtId.Value)
                .ToList();
        }

        var candidateThoughts = await db.ThinkingThoughts
            .Where(t => t.SequenceId == sequence.Id)
            .Where(t => currentThoughtId == null || t.Id != currentThoughtId.Value)
            .Where(t => t.CreatedAt >= coveredThought.CreatedAt)
            .OrderBy(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .ToListAsync();

        var visibleCandidates = candidateThoughts
            .Where(thought => !IsMiChanCompactionThought(thought))
            .ToList();
        var coveredIndex = visibleCandidates.FindIndex(thought => thought.Id == coveredThought.Id);
        var recentThoughts = coveredIndex >= 0
            ? visibleCandidates.Skip(coveredIndex + 1).ToList()
            : visibleCandidates;

        logger.LogDebug(
            "Loaded MiChan prompt history for sequence {SequenceId} in {ElapsedMs}ms. latestSummaryFound={HasSummary}, candidateThoughts={CandidateThoughtCount}, recentThoughts={RecentThoughtCount}",
            sequence.Id,
            stopwatch.ElapsedMilliseconds,
            true,
            candidateThoughts.Count,
            recentThoughts.Count
        );

        return [latestSummaryThought, .. recentThoughts];
    }

    private bool ShouldCompactMiChanHistory(List<SnThinkingThought> thoughts)
    {
        if (thoughts.Count <= MiChanMinRecentThoughts)
        {
            return false;
        }

        var totalTokens = thoughts.Sum(EstimateThoughtTokensForPrompt);
        return totalTokens > MiChanCompactionThresholdTokens;
    }

    private List<SnThinkingThought> SelectCompactionPrefix(List<SnThinkingThought> thoughts)
    {
        if (thoughts.Count <= MiChanMinRecentThoughts)
        {
            return [];
        }

        var recentThoughts = new List<SnThinkingThought>();
        var recentTokens = 0;

        for (var i = thoughts.Count - 1; i >= 0; i--)
        {
            var tokens = EstimateThoughtTokensForPrompt(thoughts[i]);
            if (recentThoughts.Count >= MiChanMinRecentThoughts &&
                recentTokens + tokens > MiChanRecentHistoryTokenBudget)
            {
                break;
            }

            recentThoughts.Insert(0, thoughts[i]);
            recentTokens += tokens;
        }

        var compactCount = thoughts.Count - recentThoughts.Count;
        return compactCount > 0 ? thoughts.Take(compactCount).ToList() : [];
    }

    private List<SnThinkingThought> SelectCompactionChunkPrefix(List<SnThinkingThought> thoughts)
    {
        var compactableThoughts = SelectCompactionPrefix(thoughts);
        if (compactableThoughts.Count == 0)
        {
            return [];
        }

        var chunk = new List<SnThinkingThought>();
        var chunkTokens = 0;

        foreach (var thought in compactableThoughts)
        {
            var tokens = EstimateThoughtTokensForPrompt(thought);
            if (chunk.Count > 0 && chunkTokens + tokens > MiChanCompactionChunkTokenBudget)
            {
                break;
            }

            chunk.Add(thought);
            chunkTokens += tokens;
        }

        return chunk.Count > 0 ? chunk : [compactableThoughts[0]];
    }

    private async Task<string?> GenerateMiChanCompactionSummaryAsync(
        Guid accountId,
        string? previousSummary,
        List<SnThinkingThought> thoughtsToCompact)
    {
        var kernel = miChanKernelProvider.GetKernel();
        if (kernel == null || thoughtsToCompact.Count == 0)
        {
            return previousSummary;
        }

        var stopwatch = Stopwatch.StartNew();

        var transcript = BuildThoughtTranscript(thoughtsToCompact);
        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine("你正在为 MiChan 维护与单个用户的长期对话压缩摘要。");
        promptBuilder.AppendLine("请将较早的对话整理成紧凑、准确、面向未来对话可复用的摘要。");
        promptBuilder.AppendLine("要求：");
        promptBuilder.AppendLine("- 保留用户长期偏好、背景事实、未完成事项、重要上下文。");
        promptBuilder.AppendLine("- 记录 MiChan 已做过的重要承诺、决定和工具结果。");
        promptBuilder.AppendLine("- 不要虚构，不要加入摘要中不存在的新信息。");
        promptBuilder.AppendLine("- 用简洁中文输出，最多 12 条短项目符号。");
        promptBuilder.AppendLine("- 这份摘要是内部上下文，不要写成对用户说的话。");

        var summaryHistory = new ChatHistory(promptBuilder.ToString());
        var userPayload = new StringBuilder();
        userPayload.AppendLine($"用户 ID: {accountId}");

        if (!string.IsNullOrWhiteSpace(previousSummary))
        {
            userPayload.AppendLine();
            userPayload.AppendLine("现有压缩摘要：");
            userPayload.AppendLine(previousSummary);
        }

        userPayload.AppendLine();
        userPayload.AppendLine("请把以下新增较早对话合并进压缩摘要：");
        userPayload.AppendLine(transcript);
        summaryHistory.AddUserMessage(userPayload.ToString());

        var result = await kernel.GetRequiredService<IChatCompletionService>()
            .GetChatMessageContentAsync(summaryHistory);

        logger.LogInformation(
            "Generated MiChan compaction summary in {ElapsedMs}ms. thoughtCount={ThoughtCount}, transcriptTokens={TranscriptTokens}, previousSummaryChars={PreviousSummaryLength}, summaryChars={SummaryLength}",
            stopwatch.ElapsedMilliseconds,
            thoughtsToCompact.Count,
            tokenCounter.CountTokens(transcript),
            previousSummary?.Length ?? 0,
            result.Content?.Length ?? 0
        );

        return result.Content?.Trim();
    }

    private string BuildThoughtTranscript(IEnumerable<SnThinkingThought> thoughts)
    {
        var builder = new StringBuilder();

        foreach (var thought in thoughts)
        {
            builder.AppendLine(SerializeThoughtForPrompt(thought));
        }

        return builder.ToString();
    }

    private int EstimateThoughtTokensForPrompt(SnThinkingThought thought)
    {
        return tokenCounter.CountTokens(SerializeThoughtForPrompt(thought), thought.ModelName);
    }

    private List<SnThinkingThought> ClampMiChanThoughtWindow(List<SnThinkingThought> thoughts, int tokenBudget)
    {
        if (thoughts.Count <= MiChanMinRecentThoughts)
        {
            return thoughts;
        }

        var keptThoughts = new List<SnThinkingThought>();
        var totalTokens = 0;

        for (var i = thoughts.Count - 1; i >= 0; i--)
        {
            var tokens = EstimateThoughtTokensForPrompt(thoughts[i]);
            if (keptThoughts.Count >= MiChanMinRecentThoughts &&
                totalTokens + tokens > tokenBudget)
            {
                break;
            }

            keptThoughts.Insert(0, thoughts[i]);
            totalTokens += tokens;
        }

        return keptThoughts;
    }

    private string SerializeThoughtForPrompt(SnThinkingThought thought)
    {
        var builder = new StringBuilder();
        builder.AppendLine(thought.Role == ThinkingThoughtRole.User ? "[User]" : "[MiChan]");

        foreach (var part in thought.Parts)
        {
            switch (part.Type)
            {
                case ThinkingMessagePartType.Text when !string.IsNullOrWhiteSpace(part.Text):
                    builder.AppendLine(part.Text);
                    break;
                case ThinkingMessagePartType.FunctionCall when part.FunctionCall != null:
                    builder.AppendLine(
                        $"[ToolCall] {part.FunctionCall.PluginName}.{part.FunctionCall.Name}: {part.FunctionCall.Arguments}");
                    break;
                case ThinkingMessagePartType.FunctionResult when part.FunctionResult != null:
                    var resultText = part.FunctionResult.Result as string ??
                                     JsonSerializer.Serialize(part.FunctionResult.Result);
                    builder.AppendLine(
                        $"[ToolResult] {part.FunctionResult.PluginName}.{part.FunctionResult.FunctionName}: {resultText}");
                    break;
            }
        }

        return builder.ToString().TrimEnd();
    }

    private int FindCoveredThoughtIndex(List<SnThinkingThought> rawThoughts, SnThinkingThought? summaryThought)
    {
        var coveredThoughtId = GetCoveredThoughtId(summaryThought);
        if (!coveredThoughtId.HasValue)
        {
            return -1;
        }

        return rawThoughts.FindIndex(thought => thought.Id == coveredThoughtId.Value);
    }

    private string? GetThoughtText(SnThinkingThought? thought)
    {
        return thought?.Parts
            .Where(part => part.Type == ThinkingMessagePartType.Text)
            .Select(part => part.Text)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
    }

    private Guid? GetCoveredThoughtId(SnThinkingThought? summaryThought)
    {
        if (summaryThought == null)
        {
            return null;
        }

        var textPart = summaryThought.Parts.FirstOrDefault(part => part.Type == ThinkingMessagePartType.Text);
        if (!TryGetMetadataString(textPart?.Metadata, MiChanCoveredThroughThoughtIdMetadataKey, out var thoughtIdText) ||
            !Guid.TryParse(thoughtIdText, out var thoughtId))
        {
            return null;
        }

        return thoughtId;
    }

    private bool TryGetMetadataString(
        Dictionary<string, object>? metadata,
        string key,
        out string? value)
    {
        value = null;
        if (metadata == null || !metadata.TryGetValue(key, out var rawValue) || rawValue == null)
        {
            return false;
        }

        switch (rawValue)
        {
            case string text:
                value = text;
                return true;
            case JsonElement { ValueKind: JsonValueKind.String } jsonText:
                value = jsonText.GetString();
                return value != null;
            default:
                value = rawValue.ToString();
                return !string.IsNullOrWhiteSpace(value);
        }
    }

    #endregion

    #region Vision Analysis

    /// <summary>
    /// Build a ChatHistory with images for vision analysis of direct attachments
    /// </summary>
    private List<ImageContent> BuildImageContent(List<SnCloudFileReferenceObject> attachments)
    {
        var siteUrl = configGlobal["SiteUrl"];
        return attachments.Select(x => new ImageContent(new Uri($"{siteUrl}/drive/files/{x.Id}"))).ToList();
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

    public ThoughtServiceModel? GetSnChanServiceInfo()
    {
        var serviceId = thoughtProvider.GetServiceId();
        var serviceInfo = thoughtProvider.GetServiceInfo(serviceId);
        return serviceInfo;
    }

    public Kernel GetMiChanKernel()
        => miChanKernelProvider.GetKernel();

    public Kernel GetMiChanVisionKernel()
        => miChanKernelProvider.GetVisionKernel();

    public PromptExecutionSettings CreateMiChanExecutionSettings()
    {
        return miChanKernelProvider.CreatePromptExecutionSettings();
    }

    public ThoughtServiceModel? GetMiChanServiceInfo(bool withFiles)
    {
        var serviceId = miChanKernelProvider.GetServiceId();
        var serviceInfo = GetServiceInfoFromConfig(serviceId);
        return serviceInfo;
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

    private static void AppendTimeContext(StringBuilder builder, string? userTimeZone)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var serverZone = DateTimeZoneProviders.Tzdb.GetSystemDefault();
        var serverNow = now.InZone(serverZone);

        builder.AppendLine($"当前时间（服务器时间）: {serverNow:yyyy年MM月dd日 HH:mm:ss}");

        if (!string.IsNullOrEmpty(userTimeZone))
        {
            try
            {
                var tz = DateTimeZoneProviders.Tzdb.GetZoneOrNull(userTimeZone);
                if (tz != null)
                {
                    var local = now.InZone(tz);
                    builder.AppendLine($"用户当地时间: {local:yyyy年MM月dd日 HH:mm:ss} ({userTimeZone})");
                }
                else
                {
                    builder.AppendLine($"（用户时区 {userTimeZone} 无法识别）");
                }
            }
            catch
            {
                builder.AppendLine($"（用户时区 {userTimeZone} 无效）");
            }
        }
        else
        {
            builder.AppendLine("（用户未设置时区）");
        }

        builder.AppendLine();
    }
    #endregion
}

#pragma warning restore SKEXP0050
