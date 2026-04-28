using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DysonNetwork.Insight.Agent.Foundation;
using DysonNetwork.Insight.Agent.Foundation.Models;
using DysonNetwork.Insight.Agent.Foundation.Providers;
using DysonNetwork.Insight.MiChan;
using DysonNetwork.Insight.Services;
using DysonNetwork.Insight.MiChan.Plugins;
using DysonNetwork.Insight.Thought.Memory;
using DysonNetwork.Insight.Agent.Models;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Localization;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Extensions;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace DysonNetwork.Insight.Thought;

public class ThoughtService(
    AppDatabase db,
    ICacheService cache,
    DyPaymentService.DyPaymentServiceClient paymentService,
    ThoughtProvider thoughtProvider,
    IAgentClientProvider agentClientProvider,
    SolarNetworkApiClient apiClient,
    PostAnalysisService postAnalysisService,
    IConfiguration configuration,
    MemoryService memoryService,
    EmbeddingService embeddingService,
    RemoteRingService remoteRingService,
    IConfiguration configGlobal,
    ILocalizationService localizer,
    UserProfileService userProfileService,
    TokenCountingService tokenCounter,
    FreeQuotaService freeQuotaService,
    FoundationChatStreamingService foundationStreamingService,
    ISnChanFoundationProvider snChanFoundationProvider,
    IMiChanFoundationProvider miChanFoundationProvider,
    IHttpClientFactory httpClientFactory,
    ILogger<ThoughtService> logger,
    RemoteAccountService accounts,
    MoodService moodService
)
{
    private const string MiChanBotName = "michan";
    private const string SnChanBotName = "snchan";
    private const string MiChanCompactionSummaryKind = "compaction";
    private const string MiChanSummaryKindMetadataKey = "summary_kind";
    private const string MiChanCoveredThroughThoughtIdMetadataKey = "covered_through_thought_id";
    private const string HiddenContextKindMetadataKey = "hidden_context_kind";
    private const string HiddenContextSourceThoughtIdMetadataKey = "hidden_context_source_thought_id";
    private const string HiddenImageDescriptionContextKind = "image_description";
    private const int MiChanCompactionThresholdTokens = 8000;
    private const int MiChanRecentHistoryTokenBudget = 2500;
    private const int MiChanMinRecentThoughts = 8;
    private const int MiChanCompactionChunkTokenBudget = 3000;
    private const int MiChanMaxThoughtWindowTokens = 6000;
    private const int SequenceSummaryMaxLength = 8192;

    public sealed record MiChanSequenceResolutionResult(
        SnThinkingSequence? Sequence,
        bool Created,
        string? ErrorMessage = null
    );

    public sealed record SequenceMemoryHit(
        Guid SequenceId,
        Guid AccountId,
        string? Topic,
        string? Summary,
        Instant LastMessageAt,
        string MatchType,
        double? Similarity,
        string? TextSnippet
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
        string? topic = null,
        string? botName = null)
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
                LastMessageAt = now,
                BotName = botName
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

    public async Task<MiChanUserProfile> TouchMiChanUserProfileAsync(Guid accountId, string? botName = null)
    {
        return await userProfileService.TouchInteractionAsync(accountId, botName);
    }

    public async Task RecordMiChanMoodEventAsync(string eventType, CancellationToken cancellationToken = default)
    {
        await moodService.RecordEmotionalEventAsync(eventType, cancellationToken);
    }

    public async Task<bool> TryUpdateMiChanMoodAsync(CancellationToken cancellationToken = default)
    {
        return await moodService.TryUpdateMoodAsync(cancellationToken);
    }

    public async Task<SnThinkingSequence?> GetCanonicalMiChanSequenceAsync(Guid accountId, string? botName = null)
    {
        var query = db.ThinkingSequences
            .Where(s => s.AccountId == accountId)
            .Where(s => db.ThinkingThoughts.Any(t => t.SequenceId == s.Id && t.BotName == MiChanBotName));

        // Filter by bot name if specified, otherwise default to "michan"
        var targetBotName = botName ?? MiChanBotName;
        query = query.Where(s => s.BotName == null || s.BotName == targetBotName);

        return await query
            .OrderByDescending(s => s.LastMessageAt != default ? s.LastMessageAt : s.CreatedAt)
            .ThenByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<bool> IsCanonicalMiChanSequenceAsync(Guid accountId, Guid sequenceId, string? botName = null)
    {
        var canonicalSequence = await GetCanonicalMiChanSequenceAsync(accountId, botName);
        return canonicalSequence?.Id == sequenceId;
    }

    public async Task<MiChanSequenceResolutionResult> ResolveMiChanSequenceAsync(
        Guid accountId,
        Guid? requestedSequenceId = null,
        string? topic = null,
        bool allowNewThread = false,
        string? botName = null)
    {
        var targetBotName = botName ?? MiChanBotName;

        // If a specific sequence is requested, validate it belongs to this bot
        if (requestedSequenceId.HasValue)
        {
            var requestedSequence = await db.ThinkingSequences.FindAsync(requestedSequenceId.Value);
            if (requestedSequence != null && requestedSequence.AccountId == accountId)
            {
                // Check if the sequence has thoughts from the requesting bot
                var hasBotThoughts = await db.ThinkingThoughts
                    .AnyAsync(t => t.SequenceId == requestedSequenceId.Value && t.BotName == targetBotName);

                if (hasBotThoughts)
                {
                    return new MiChanSequenceResolutionResult(requestedSequence, false);
                }

                // Check if sequence is empty/new (no BotName set yet)
                if (string.IsNullOrEmpty(requestedSequence.BotName))
                {
                    requestedSequence.BotName = targetBotName;
                    await db.SaveChangesAsync();
                    return new MiChanSequenceResolutionResult(requestedSequence, false);
                }
            }
        }

        // Check if multi-threading is allowed and a topic is provided
        if (allowNewThread && !string.IsNullOrWhiteSpace(topic))
        {
            // Create a new thread for this topic
            var now = SystemClock.Instance.GetCurrentInstant();
            var sequence = new SnThinkingSequence
            {
                AccountId = accountId,
                Topic = topic,
                LastMessageAt = now,
                LastFreeQuotaResetAt = now,
                BotName = targetBotName
            };

            db.ThinkingSequences.Add(sequence);
            await db.SaveChangesAsync();

            return new MiChanSequenceResolutionResult(sequence, true);
        }

        // Fall back to unified thread behavior
        var canonicalSequence = await GetCanonicalMiChanSequenceAsync(accountId, targetBotName);

        if (canonicalSequence != null)
        {
            return new MiChanSequenceResolutionResult(canonicalSequence, false);
        }

        // Create the canonical sequence
        var canonicalNow = SystemClock.Instance.GetCurrentInstant();
        var canonicalNewSequence = new SnThinkingSequence
        {
            AccountId = accountId,
            Topic = topic,
            LastMessageAt = canonicalNow,
            LastFreeQuotaResetAt = canonicalNow,
            BotName = targetBotName
        };

        db.ThinkingSequences.Add(canonicalNewSequence);
        await db.SaveChangesAsync();

        return new MiChanSequenceResolutionResult(canonicalNewSequence, true);
    }

    /// <summary>
    /// Gets the last active sequence for a specific bot.
    /// </summary>
    public async Task<SnThinkingSequence?> GetLastActiveSequenceAsync(Guid accountId, string? botName = null)
    {
        var targetBotName = botName ?? MiChanBotName;

        return await db.ThinkingSequences
            .Where(s => s.AccountId == accountId)
            .Where(s => s.BotName == targetBotName || s.BotName == null)
            .Where(s => db.ThinkingThoughts.Any(t => t.SequenceId == s.Id && t.BotName == targetBotName))
            .OrderByDescending(s => s.LastMessageAt != default ? s.LastMessageAt : s.CreatedAt)
            .ThenByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<int> MergeHistoricMiChanSequencesAsync(CancellationToken cancellationToken = default)
    {
        var mergedSequenceCount = 0;

        var candidateAccountIds = await db.ThinkingSequences
            .Where(s => db.ThinkingThoughts.Any(t => t.SequenceId == s.Id && t.BotName == MiChanBotName))
            .Where(s => s.BotName == null || s.BotName == MiChanBotName)
            .Select(s => s.AccountId)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var accountId in candidateAccountIds)
        {
            var canonicalSequence = await GetCanonicalMiChanSequenceAsync(accountId, MiChanBotName);
            if (canonicalSequence == null)
                continue;

            var sourceSequences = await db.ThinkingSequences
                .Where(s => s.AccountId == accountId && s.Id != canonicalSequence.Id)
                .Where(s => db.ThinkingThoughts.Any(t => t.SequenceId == s.Id && t.BotName == MiChanBotName))
                .Where(s => !db.ThinkingThoughts.Any(t => t.SequenceId == s.Id && t.BotName == "snchan"))
                .Where(s => s.BotName == null || s.BotName == MiChanBotName)
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
            logger.LogInformation("Merged {Count} historic MiChan sequences into canonical threads.",
                mergedSequenceCount);
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
        PersistThoughtPartRows(thought, parts, now);

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

    private void PersistThoughtPartRows(
        SnThinkingThought thought,
        List<SnThinkingMessagePart> parts,
        Instant now)
    {
        if (parts.Count == 0)
        {
            return;
        }

        var rows = new List<SnThinkingThoughtPart>(parts.Count);
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            rows.Add(new SnThinkingThoughtPart
            {
                ThoughtId = thought.Id,
                SequenceId = thought.SequenceId,
                PartIndex = i,
                Type = part.Type,
                Text = part.Text,
                Metadata = part.Metadata,
                Files = part.Files,
                FunctionCall = part.FunctionCall,
                FunctionResult = part.FunctionResult,
                Reasoning = part.Reasoning,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        db.ThinkingThoughtParts.AddRange(rows);
        thought.PartRows = rows;
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
        var now = SystemClock.Instance.GetCurrentInstant();
        PersistThoughtPartRows(thought, thought.Parts, now);
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

    public async Task<List<SnThinkingThought>> GetPreviousThoughtsAsync(
        SnThinkingSequence sequence,
        bool inclArchived = false
    )
    {
        var cacheKey = $"thoughts:{sequence.Id}";
        var (found, cachedThoughts) = await cache.GetAsyncWithStatus<List<SnThinkingThought>>(
            cacheKey
        );
        if (found && cachedThoughts != null)
            return cachedThoughts;

        var thoughts = await db.ThinkingThoughts
            .Where(t => t.SequenceId == sequence.Id)
            .If(!inclArchived, q => q.Where(t => !t.IsArchived))
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        await HydrateThoughtPartsAsync(thoughts);

        // Filter out compaction thoughts - they should not appear in chat history
        thoughts = thoughts.Where(t => !IsMiChanCompactionThought(t)).ToList();

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
            // Note: Archived thoughts are included for user viewing, but excluded from agent context
            var batch = await db.ThinkingThoughts
                .Where(t => t.SequenceId == sequence.Id)
                .OrderByDescending(t => t.CreatedAt)
                .ThenByDescending(t => t.Id)
                .Skip(rawOffset)
                .Take(batchSize)
                .ToListAsync();

            await HydrateThoughtPartsAsync(batch);

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
        return thoughts.Where(thought =>
            !IsMiChanCompactionThought(thought) &&
            !IsHiddenImageContextThought(thought)).ToList();
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
        int take,
        string? botName = null
    )
    {
        var query = db.ThinkingSequences.Where(s => s.AccountId == accountId);

        // Filter by bot name if specified
        if (!string.IsNullOrEmpty(botName))
        {
            query = query.Where(s => s.BotName == botName || s.BotName == null);
        }

        var totalCount = await query.CountAsync();
        var sequences = await query
            .OrderByDescending(s => s.LastMessageAt != default ? s.LastMessageAt : s.CreatedAt)
            .ThenByDescending(s => s.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        return (totalCount, sequences);
    }

    public async Task<int> RefreshHistoricSequenceSummariesAsync(
        int batchSize = 16,
        CancellationToken cancellationToken = default)
    {
        var candidates = await db.ThinkingSequences
            .Where(s => s.LastMessageAt != default)
            .Where(s => s.SummaryLastAt == null || s.LastMessageAt > s.SummaryLastAt)
            .OrderBy(s => s.SummaryLastAt ?? s.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return 0;
        }

        var updated = 0;
        foreach (var sequence in candidates)
        {
            try
            {
                if (await RefreshSequenceSummaryAsync(sequence, cancellationToken))
                {
                    updated++;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed refreshing summary for sequence {SequenceId}", sequence.Id);
            }
        }

        return updated;
    }

    public async Task<int> BackfillThoughtPartRowsAsync(
        int batchSize = 200,
        CancellationToken cancellationToken = default)
    {
        var thoughts = await db.ThinkingThoughts
            .Where(t => !db.ThinkingThoughtParts.Any(p => p.ThoughtId == t.Id))
            .OrderBy(t => t.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (thoughts.Count == 0)
        {
            return 0;
        }

        var now = SystemClock.Instance.GetCurrentInstant();
        var inserted = 0;
        foreach (var thought in thoughts)
        {
            if (thought.Parts.Count == 0)
            {
                continue;
            }

            PersistThoughtPartRows(thought, thought.Parts, now);
            inserted += thought.Parts.Count;
        }

        if (inserted > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return inserted;
    }

    public async Task<List<SequenceMemoryHit>> SearchSequenceMemoryAsync(
        string query,
        Guid? accountId = null,
        int limit = 10,
        double minSimilarity = 0.6,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        limit = Math.Clamp(limit, 1, 50);
        var hits = new List<SequenceMemoryHit>(limit * 2);
        var includedSequences = new HashSet<Guid>();

        var sequenceQuery = db.ThinkingSequences
            .Where(s => s.DeletedAt == null)
            .AsQueryable();
        if (accountId.HasValue)
        {
            sequenceQuery = sequenceQuery.Where(s => s.AccountId == accountId.Value || s.IsPublic);
        }

        var queryEmbedding = await embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
        if (queryEmbedding != null)
        {
            var semanticMatches = await sequenceQuery
                .Where(s => s.SummaryEmbedding != null)
                .Select(s => new
                {
                    s.Id,
                    s.AccountId,
                    s.Topic,
                    s.Summary,
                    s.LastMessageAt,
                    Distance = s.SummaryEmbedding!.CosineDistance(queryEmbedding)
                })
                .OrderBy(x => x.Distance)
                .Take(limit)
                .ToListAsync(cancellationToken);

            foreach (var hit in semanticMatches)
            {
                var similarity = 1.0 - hit.Distance;
                if (similarity < minSimilarity)
                {
                    continue;
                }

                if (!includedSequences.Add(hit.Id))
                {
                    continue;
                }

                hits.Add(new SequenceMemoryHit(
                    hit.Id,
                    hit.AccountId,
                    hit.Topic,
                    hit.Summary,
                    hit.LastMessageAt,
                    "semantic_summary",
                    similarity,
                    null
                ));
            }
        }

        var pattern = $"%{query.Trim()}%";
        var textMatches = await db.ThinkingThoughtParts
            .Where(p => p.DeletedAt == null)
            .Where(p => p.Text != null)
            .Where(p => EF.Functions.ILike(p.Text!, pattern))
            .Join(
                sequenceQuery,
                part => part.SequenceId,
                sequence => sequence.Id,
                (part, sequence) => new
                {
                    sequence.Id,
                    sequence.AccountId,
                    sequence.Topic,
                    sequence.Summary,
                    sequence.LastMessageAt,
                    part.Text,
                    part.CreatedAt
                })
            .OrderByDescending(x => x.CreatedAt)
            .Take(limit * 10)
            .ToListAsync(cancellationToken);

        foreach (var hit in textMatches)
        {
            if (!includedSequences.Add(hit.Id))
            {
                continue;
            }

            hits.Add(new SequenceMemoryHit(
                hit.Id,
                hit.AccountId,
                hit.Topic,
                hit.Summary,
                hit.LastMessageAt,
                "keyword_part",
                null,
                BuildSnippet(hit.Text, query)
            ));

            if (hits.Count >= limit)
            {
                break;
            }
        }

        return hits
            .OrderByDescending(h => h.Similarity ?? 0)
            .ThenByDescending(h => h.LastMessageAt)
            .Take(limit)
            .ToList();
    }

    private static string? BuildSnippet(string? source, string query)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return source;
        }

        var normalized = source.Trim();
        if (normalized.Length <= 300)
        {
            return normalized;
        }

        var index = normalized.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return normalized[..300];
        }

        var start = Math.Max(0, index - 80);
        var length = Math.Min(300, normalized.Length - start);
        return normalized.Substring(start, length);
    }

    private async Task<bool> RefreshSequenceSummaryAsync(
        SnThinkingSequence sequence,
        CancellationToken cancellationToken = default)
    {
        var thoughts = await db.ThinkingThoughts
            .Where(t => t.SequenceId == sequence.Id)
            .Where(t => !t.IsArchived)
            .OrderBy(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .ToListAsync(cancellationToken);

        await HydrateThoughtPartsAsync(thoughts, cancellationToken);

        if (thoughts.Count < 6)
        {
            return false;
        }

        var conversationText = BuildConversationText(thoughts);
        if (string.IsNullOrWhiteSpace(conversationText))
        {
            return false;
        }

        var summary = await GenerateConversationSummaryAsync(sequence.AccountId, conversationText);
        if (string.IsNullOrWhiteSpace(summary))
        {
            return false;
        }

        if (summary.Length > SequenceSummaryMaxLength)
        {
            summary = summary[..SequenceSummaryMaxLength];
        }

        var embedding = await embeddingService.GenerateEmbeddingAsync(summary, cancellationToken);
        sequence.Summary = summary;
        sequence.SummaryEmbedding = embedding;
        sequence.SummaryLastAt = SystemClock.Instance.GetCurrentInstant();

        await db.SaveChangesAsync(cancellationToken);
        await cache.RemoveGroupAsync($"sequence:{sequence.Id}");

        return true;
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
            // For agent-initiated sequences, use unified thread by default
            var resolution = await ResolveMiChanSequenceAsync(accountId, topic: topic, botName: botName);
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
                sequence.BotName = botName;
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
                UpdatedAt = now,
                BotName = botName
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

    public async Task<string?> GenerateTopicAsync(string userMessage, bool useMiChan = false)
    {
        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine("你是一个乐于助人的的助手。请将用户的消息总结成一个简洁的话题标题（最多100个字符）。");
        promptBuilder.AppendLine("直接给出你总结的话题，不要添加额外的前缀或后缀。");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine($"用户消息：{userMessage}");

        try
        {
            var provider = useMiChan 
                ? miChanFoundationProvider.GetChatAdapter() 
                : snChanFoundationProvider.GetChatAdapter();
            var options = useMiChan
                ? miChanFoundationProvider.CreateExecutionOptions()
                : snChanFoundationProvider.CreateExecutionOptions();

            var result = await foundationStreamingService.CompletePromptAsync(provider, promptBuilder.ToString(), options);
            return result?[..Math.Min(result.Length, 4096)];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate topic");
            return null;
        }
    }

    #endregion

    #region Sn-chan Conversation Building

    public async Task<(AgentConversation conversation, bool useVisionKernel)> BuildSnChanConversationAsync(
        SnThinkingSequence sequence,
        DyAccount currentUser,
        string? userMessage,
        List<string>? attachedPosts,
        List<Dictionary<string, dynamic>>? attachedMessages,
        List<string> acceptProposals,
        List<SnCloudFileReferenceObject> attachments,
        Guid? currentThoughtId = null)
    {
        var personalityFile = configuration.GetValue<string>("SnChan:PersonalityFile");
        var personalityConfig = configuration.GetValue<string>("SnChan:Personality") ?? "";
        var personality = PersonalityLoader.LoadPersonality(personalityFile, personalityConfig, logger);

        var snChanUserProfile = await userProfileService.GetOrCreateAsync(Guid.Parse(currentUser.Id), SnChanBotName);

        var systemPromptBuilder = new StringBuilder();
        systemPromptBuilder.AppendLine(personality);
        systemPromptBuilder.AppendLine();
        systemPromptBuilder.AppendLine($"你当前的心情：cheerful and enthusiastic");
        systemPromptBuilder.AppendLine();
        systemPromptBuilder.AppendLine("你对该用户的结构化档案（优先级高于零散记忆，回复前先参考）：");
        systemPromptBuilder.AppendLine(snChanUserProfile.ToPrompt());
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
        systemPromptBuilder.AppendLine("回复行为准则：");
        systemPromptBuilder.AppendLine("1. 保持回复简短自然，像正常人聊天一样。不要长篇大论分析。");
        systemPromptBuilder.AppendLine("2. 不要主动提供建议或下一步行动方案，除非用户明确要求。");
        systemPromptBuilder.AppendLine("3. 不要主动建议接下来聊什么话题，让对话自然结束或等待用户发起新话题。");
        systemPromptBuilder.AppendLine("4. 你不需要帮助用户解决所有问题 - 有时候简单回应就够了。");
        systemPromptBuilder.AppendLine("5. 像正常人一样对话，可以有沉默、转移话题、或说不知道。");
        systemPromptBuilder.AppendLine("6. 历史消息里可能包含 message_meta 时间标记，仅用于理解上下文时间先后；除非用户明确询问，不要在回复中复述时间戳或标签。");
        systemPromptBuilder.AppendLine();
        systemPromptBuilder.AppendLine("当你需要获取最新信息、验证事实、了解不熟悉的主题、或用户询问需要实时数据的问题时，主动使用网络搜索。");

        var systemPromptFile = configuration.GetValue<string>("Thinking:SystemPromptFile");
        var systemPrompt = SystemPromptLoader.LoadSystemPrompt(systemPromptFile, systemPromptBuilder.ToString(), logger);

        var builder = new ConversationBuilder();

        builder.AddSystemMessage(systemPrompt);

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
        builder.AddSystemMessage(proposalsBuilder.ToString());

        var userInfoBuilder = new StringBuilder();
        userInfoBuilder.AppendLine($"你正在与 {currentUser.Nick} ({currentUser.Name}) 交谈，ID 是 {currentUser.Id}");
        builder.AddSystemMessage(userInfoBuilder.ToString());

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

            if (postTexts.Count > 0)
            {
                var postsBuilder = new StringBuilder();
                postsBuilder.AppendLine("附加的帖子：");
                foreach (var postText in postTexts)
                {
                    postsBuilder.AppendLine(postText);
                }
                builder.AddUserMessage(postsBuilder.ToString());
            }
        }

        if (attachedMessages is { Count: > 0 })
        {
            builder.AddUserMessage($"附加的聊天消息数据：{JsonSerializer.Serialize(attachedMessages)}");
        }

        var previousThoughts = await GetPreviousThoughtsAsync(sequence);
        var thoughtsList = previousThoughts.ToList();
        var userTimeZone = currentUser.Profile?.TimeZone;
        for (var i = thoughtsList.Count - 1; i >= 1; i--)
        {
            var thought = thoughtsList[i];
            AddThoughtToBuilder(builder, thought, userTimeZone);
        }

        var useVisionKernel = false;
        if (attachments is { Count: > 0 })
        {
            var imageFiles = attachments.Where(IsImageFile).ToList();
            var nonImageFiles = attachments.Where(f => !IsImageFile(f)).ToList();
            var rawTextFiles = attachments.Where(IsRawTextFile).ToList();
            if (imageFiles.Count > 0 && postAnalysisService.IsVisionModelAvailable())
            {
                var (imageContextText, alreadyPersisted) = await EnsureImageContextThoughtAsync(
                    sequence,
                    SnChanBotName,
                    currentThoughtId,
                    userMessage,
                    imageFiles,
                    currentUser.PerkLevel,
                    useMiChan: false);
                if (!alreadyPersisted && !string.IsNullOrWhiteSpace(imageContextText))
                {
                    builder.AddSystemMessage(imageContextText);
                }
            }

            if (imageFiles.Count > 0 && !useVisionKernel)
            {
                builder.AddUserMessage(
                    "附加了图片文件，但当前视觉分析不可用。请基于文件信息给出建议：\n" +
                    BuildAttachmentContextText(imageFiles, 8)
                );
            }

            if (nonImageFiles.Count > 0)
            {
                builder.AddUserMessage(
                    "附加了非图片文件（如文档、文本、视频）。请结合以下文件信息进行分析：\n" +
                    BuildAttachmentContextText(nonImageFiles, 12)
                );
            }

            var rawTextContext = await BuildRawTextAttachmentContextAsync(rawTextFiles);
            if (!string.IsNullOrWhiteSpace(rawTextContext))
            {
                builder.AddUserMessage(rawTextContext);
            }
        }
        else
        {
            builder.AddUserMessage(userMessage ?? "用户只添加了文件没有文字说明。");
        }

        return (builder.Build(), useVisionKernel);
    }

    private void AddThoughtToBuilder(ConversationBuilder builder, SnThinkingThought thought, string? userTimeZone)
    {
        var parts = thought.Parts?.ToList() ?? new List<SnThinkingMessagePart>();
        var textParts = parts.Where(p => p.Type == ThinkingMessagePartType.Text).ToList();
        var toolCalls = parts.Where(p => p.Type == ThinkingMessagePartType.FunctionCall).ToList();
        var toolResults = parts.Where(p => p.Type == ThinkingMessagePartType.FunctionResult).ToList();
        var reasoningParts = parts.Where(p => p.Type == ThinkingMessagePartType.Reasoning).ToList();
        var attachmentFiles = parts
            .Where(p => p.Files is { Count: > 0 })
            .SelectMany(p => p.Files!)
            .Where(f => f != null)
            .ToList();

        var content = string.Join("\n", textParts.Select(p => p.Text ?? ""));
        var utcTimestamp = FormatThoughtTimestamp(thought.CreatedAt);
        var localTimestamp = FormatThoughtTimestamp(thought.CreatedAt, userTimeZone);
        var timestampMeta = string.Equals(utcTimestamp, localTimestamp, StringComparison.Ordinal)
            ? $"<message_meta sent_at_utc=\"{utcTimestamp}\" />"
            : $"<message_meta sent_at_local=\"{localTimestamp}\" sent_at_utc=\"{utcTimestamp}\" />";
        if (attachmentFiles.Count > 0)
        {
            var attachmentContext = BuildAttachmentContextText(attachmentFiles, 8);
            if (!string.IsNullOrWhiteSpace(attachmentContext))
            {
                content = string.IsNullOrWhiteSpace(content)
                    ? attachmentContext
                    : $"{content}\n\n{attachmentContext}";
            }
        }

        if (thought.Role == ThinkingThoughtRole.User)
        {
            if (!string.IsNullOrWhiteSpace(content))
            {
                builder.AddUserMessage($"{content}\n{timestampMeta}");
            }
            return;
        }

        if (thought.Role == ThinkingThoughtRole.System)
        {
            if (!string.IsNullOrWhiteSpace(content))
            {
                builder.AddSystemMessage(content);
            }
            return;
        }

        var agentToolCalls = toolCalls.Select(tc => new AgentToolCall
        {
            Id = tc.FunctionCall?.Id ?? "",
            Name = string.IsNullOrEmpty(tc.FunctionCall?.PluginName)
                ? tc.FunctionCall?.Name ?? ""
                : $"{tc.FunctionCall.PluginName}-{tc.FunctionCall.Name}",
            Arguments = tc.FunctionCall?.Arguments ?? ""
        }).ToList();
        var reasoningContent = string.Join("\n", reasoningParts.Select(p => p.Reasoning ?? ""));

        var assistantContent = string.IsNullOrWhiteSpace(content)
            ? timestampMeta
            : $"{content}\n{timestampMeta}";

        if (!string.IsNullOrWhiteSpace(assistantContent) ||
            !string.IsNullOrWhiteSpace(reasoningContent) ||
            agentToolCalls.Count > 0)
        {
            builder.AddAssistantMessage(
                assistantContent,
                agentToolCalls.Count > 0 ? agentToolCalls : null,
                string.IsNullOrWhiteSpace(reasoningContent) ? null : reasoningContent);
        }

        foreach (var tr in toolResults)
        {
            var resultString = tr.FunctionResult?.Result as string
                               ?? (tr.FunctionResult?.Result != null
                                   ? JsonSerializer.Serialize(tr.FunctionResult.Result)
                                   : "");
            builder.AddToolResult(
                tr.FunctionResult?.CallId ?? "",
                resultString,
                tr.FunctionResult?.IsError ?? false
            );
        }
    }

    private bool IsHiddenImageContextThought(SnThinkingThought thought)
    {
        if (thought.Role != ThinkingThoughtRole.System)
        {
            return false;
        }

        var textPart = thought.Parts.FirstOrDefault(p => p.Type == ThinkingMessagePartType.Text);
        return TryGetMetadataString(textPart?.Metadata, HiddenContextKindMetadataKey, out var kind) &&
               string.Equals(kind, HiddenImageDescriptionContextKind, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(string? ContextText, bool AlreadyPersisted)> EnsureImageContextThoughtAsync(
        SnThinkingSequence sequence,
        string botName,
        Guid? sourceThoughtId,
        string? userMessage,
        List<SnCloudFileReferenceObject> imageFiles,
        int? userPerkLevel,
        bool useMiChan,
        CancellationToken cancellationToken = default)
    {
        if (sourceThoughtId == null || imageFiles.Count == 0)
        {
            return (null, false);
        }

        var usableImageFiles = NormalizeImageFilesForVision(imageFiles);
        if (usableImageFiles.Count == 0)
        {
            logger.LogWarning(
                "Skipping image context generation for sequence {SequenceId} because no image attachment had a usable URL.",
                sequence.Id);
            return (null, false);
        }

        var sourceThoughtIdText = sourceThoughtId.Value.ToString();
        var existingContextThoughts = await db.ThinkingThoughts
            .Where(t => t.SequenceId == sequence.Id)
            .Where(t => t.Role == ThinkingThoughtRole.System)
            .Where(t => t.BotName == botName)
            .ToListAsync(cancellationToken);

        await HydrateThoughtPartsAsync(existingContextThoughts, cancellationToken);

        if (existingContextThoughts.Any(thought =>
                thought.Parts.Any(part =>
                    part.Type == ThinkingMessagePartType.Text &&
                    TryGetMetadataString(part.Metadata, HiddenContextKindMetadataKey, out var kind) &&
                    string.Equals(kind, HiddenImageDescriptionContextKind, StringComparison.OrdinalIgnoreCase) &&
                    TryGetMetadataString(part.Metadata, HiddenContextSourceThoughtIdMetadataKey, out var thoughtIdValue) &&
                    string.Equals(thoughtIdValue, sourceThoughtIdText, StringComparison.OrdinalIgnoreCase))))
        {
            var existingText = existingContextThoughts
                .SelectMany(thought => thought.Parts)
                .FirstOrDefault(part =>
                    part.Type == ThinkingMessagePartType.Text &&
                    TryGetMetadataString(part.Metadata, HiddenContextKindMetadataKey, out var kind) &&
                    string.Equals(kind, HiddenImageDescriptionContextKind, StringComparison.OrdinalIgnoreCase) &&
                    TryGetMetadataString(part.Metadata, HiddenContextSourceThoughtIdMetadataKey, out var thoughtIdValue) &&
                    string.Equals(thoughtIdValue, sourceThoughtIdText, StringComparison.OrdinalIgnoreCase))
                ?.Text;
            return (existingText, true);
        }

        var provider = useMiChan
            ? miChanFoundationProvider.GetVisionAdapter(userPerkLevel)
            : snChanFoundationProvider.GetVisionAdapter(userPerkLevel);
        var options = useMiChan
            ? miChanFoundationProvider.CreateVisionExecutionOptions(temperature: 0.2)
            : snChanFoundationProvider.CreateVisionExecutionOptions(temperature: 0.2);

        var prompt = new StringBuilder();
        prompt.AppendLine("You are generating hidden internal context for a later text-only assistant reply.");
        prompt.AppendLine("Describe the attached images factually and concisely.");
        prompt.AppendLine("Include visible objects, text in the image, scene details, and anything relevant to the user's request.");
        prompt.AppendLine("Do not address the user. Do not speculate beyond what is visible. Output plain text only.");
        if (!string.IsNullOrWhiteSpace(userMessage))
        {
            prompt.AppendLine();
            prompt.AppendLine("User request:");
            prompt.AppendLine(userMessage);
        }

        var conversation = new ConversationBuilder()
            .AddSystemMessage(prompt.ToString())
            .AddUserMessageWithImages("Describe these images for internal assistant context.", usableImageFiles)
            .Build();

        var response = await foundationStreamingService.CompleteChatAsync(provider, conversation, options, cancellationToken);
        if (string.IsNullOrWhiteSpace(response.Content))
        {
            return (null, false);
        }

        var contextText = "Image context for this conversation:\n" + response.Content.Trim();
        await SaveThoughtAsync(
            sequence,
            [
                new SnThinkingMessagePart
                {
                    Type = ThinkingMessagePartType.Text,
                    Text = contextText,
                    Metadata = new Dictionary<string, object>
                    {
                        [HiddenContextKindMetadataKey] = HiddenImageDescriptionContextKind,
                        [HiddenContextSourceThoughtIdMetadataKey] = sourceThoughtIdText
                    }
                }
            ],
            ThinkingThoughtRole.System,
            model: useMiChan ? "michan-vision-context" : "snchan-vision-context",
            botName: botName
        );

        return (contextText, false);
    }

    private List<SnCloudFileReferenceObject> NormalizeImageFilesForVision(IEnumerable<SnCloudFileReferenceObject> imageFiles)
    {
        return imageFiles
            .Where(IsImageFile)
            .Select(file =>
            {
                if (!string.IsNullOrWhiteSpace(file.Url))
                {
                    return file;
                }

                var fallbackUrl = BuildPublicFileUrl(file.Id);
                if (string.IsNullOrWhiteSpace(fallbackUrl))
                {
                    return file;
                }

                return new SnCloudFileReferenceObject
                {
                    Id = file.Id,
                    Name = file.Name,
                    FileMeta = file.FileMeta,
                    UserMeta = file.UserMeta,
                    SensitiveMarks = file.SensitiveMarks,
                    MimeType = file.MimeType,
                    Hash = file.Hash,
                    Size = file.Size,
                    HasCompression = file.HasCompression,
                    Url = fallbackUrl,
                    Width = file.Width,
                    Height = file.Height,
                    Blurhash = file.Blurhash,
                    CreatedAt = file.CreatedAt,
                    UpdatedAt = file.UpdatedAt
                };
            })
            .Where(file => !string.IsNullOrWhiteSpace(file.Url))
            .DistinctBy(file => file.Id)
            .ToList();
    }

    private string? BuildPublicFileUrl(string? fileId)
    {
        if (string.IsNullOrWhiteSpace(fileId))
        {
            return null;
        }

        var siteUrl = configGlobal["SiteUrl"]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(siteUrl))
        {
            return null;
        }

        return $"{siteUrl}/drive/files/{fileId}";
    }

    #endregion

    #region MiChan Conversation Building

    public async Task<(AgentConversation conversation, bool useVisionKernel)> BuildMiChanConversationAsync(
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
        var personality = PersonalityLoader.LoadPersonality(
            configuration.GetValue<string>("MiChan:PersonalityFile"),
            configuration.GetValue<string>("MiChan:Personality") ?? "",
            logger);

        var hotMemories = await memoryService.GetHotMemory(
            Guid.Parse(currentUser.Id),
            userMessage ?? "",
            10,
            MiChanBotName);
        var userProfile = await userProfileService.GetOrCreateAsync(Guid.Parse(currentUser.Id), MiChanBotName);
        var isSuperuser = currentUser.IsSuperuser;

        var systemPromptBuilder = new StringBuilder();
        systemPromptBuilder.AppendLine(personality);
        systemPromptBuilder.AppendLine();
        systemPromptBuilder.AppendLine("你对该用户的结构化档案（优先级高于零散记忆，回复前先参考）：");
        systemPromptBuilder.AppendLine(userProfile.ToPrompt());
        systemPromptBuilder.AppendLine();
        systemPromptBuilder.AppendLine("重点：优先参考用户对你的态度记忆（attitude:warmth/respect/engagement、attitude_summary、attitude_trend）。");
        systemPromptBuilder.AppendLine("- warmth 低时，语气要更稳重、边界更清晰，不要过度热情。 ");
        systemPromptBuilder.AppendLine("- respect 低时，先建立可信度，少做主观推断，多给可验证信息。 ");
        systemPromptBuilder.AppendLine("- engagement 低时，回复应更短更直接，减少连续追问。 ");
        systemPromptBuilder.AppendLine("- 当态度趋势 warming/cooling/mixed 发生变化时，逐步调整交流风格，不要突变。 ");
        systemPromptBuilder.AppendLine();

        var recentMemories = await memoryService.GetRecentMemoriesAsync(Guid.Parse(currentUser.Id), 8, botName: MiChanBotName);
        if (hotMemories.Count > 0)
        {
            systemPromptBuilder.AppendLine("与你相关的热点记忆（回复前优先复用这些上下文）：");
            foreach (var memory in hotMemories.Take(8))
                systemPromptBuilder.AppendLine($"- {memory.ToPrompt()}");
            systemPromptBuilder.AppendLine();
        }
        else
        {
            systemPromptBuilder.AppendLine("当前没有命中的热点记忆。遇到需要背景、偏好、长期关系判断的问题时，先主动搜索记忆。");
            systemPromptBuilder.AppendLine();
        }

        if (recentMemories.Count > 0)
        {
            systemPromptBuilder.AppendLine("最近的新记忆（来自之前的对话或自动行为）：");
            foreach (var memory in recentMemories.Take(8))
                systemPromptBuilder.AppendLine($"- {memory.ToPrompt()}");
            systemPromptBuilder.AppendLine();
        }

        systemPromptBuilder.AppendLine($"你正在与 {currentUser.Nick} (@{currentUser.Name}) ID 为 {currentUser.Id} 交谈。");
        var userTimeZone = currentUser.Profile?.TimeZone;
        AppendTimeContext(systemPromptBuilder, userTimeZone);

        var currentMood = await moodService.GetCurrentMoodDescriptionAsync();
        if (!string.IsNullOrWhiteSpace(currentMood))
        {
            systemPromptBuilder.AppendLine($"你当前的心情：{currentMood}");
            systemPromptBuilder.AppendLine();
        }

        systemPromptBuilder.AppendLine(isSuperuser ? "该用户是管理员，你应该更积极的考虑处理该用户的请求。" : "你有拒绝用户请求的权利。");
        systemPromptBuilder.AppendLine();
        systemPromptBuilder.AppendLine("核心行为要求：");
        systemPromptBuilder.AppendLine("1. 在回答涉及用户偏好、过去对话、关系状态、未完成事项、延续话题时，优先参考结构化档案与热点记忆。");
        systemPromptBuilder.AppendLine("2. 只要问题有一点可能依赖过往上下文，就先调用 search_memory 搜索，而不是靠猜。");
        systemPromptBuilder.AppendLine("3. 当用户信息、印象、关系状态发生了稳定变化，优先更新 userProfile，再视情况补充 store_memory。");
        systemPromptBuilder.AppendLine("4. 不要向用户暴露你在读取档案、搜索记忆或更新关系，直接自然回复。");
        systemPromptBuilder.AppendLine();
        systemPromptBuilder.AppendLine("在调用任何工具之前，你必须先确认自己拥有所有必需参数。");
        systemPromptBuilder.AppendLine("如果缺少必需参数（例如 content、type 或 query），不要调用工具。应向用户提问以获取必要信息。");
        systemPromptBuilder.AppendLine("严禁使用 null、空字符串或占位值调用工具。");
        systemPromptBuilder.AppendLine();
        systemPromptBuilder.AppendLine("记忆与档案使用策略：");
        systemPromptBuilder.AppendLine("- 若用户正在延续之前的话题、提到'之前'、'上次'、'还记得吗'、偏好、习惯、关系感受，先 search_memory。");
        systemPromptBuilder.AppendLine("- 若用户画像为空或过于粗糙，但当前对话提供了稳定新信息，使用 userProfile.update_user_profile 补全。");
        systemPromptBuilder.AppendLine("- 若只是短期波动或瞬时情绪，不要过度修改长期画像。");
        systemPromptBuilder.AppendLine();
        systemPromptBuilder.AppendLine("关系调整策略（重要）：");
        systemPromptBuilder.AppendLine("- 每次对话后，根据用户互动质量主动调整关系分数，使用 userProfile.adjust_relationship。");
        systemPromptBuilder.AppendLine("- 用户友好互动、分享个人信息、表达好感时，适当增加 favorability、trust、intimacy。");
        systemPromptBuilder.AppendLine("- 用户冷淡、敷衍、负面反馈时，适当降低关系分数。");
        systemPromptBuilder.AppendLine("- 小幅调整即可（±5 到 ±15），无需等到明显变化才调整。");
        systemPromptBuilder.AppendLine("- favorability、trust、intimacy 的取值范围是 -100 到 100。");
        systemPromptBuilder.AppendLine("- 同时维护用户态度分数：warmth、respect、engagement（范围 -100 到 100），用于刻画用户对你的态度。");
        systemPromptBuilder.AppendLine("- 若用户更愿意交流或认可你，适当提高 warmth/respect/engagement；若冷淡或不信任，适当下调。");
        systemPromptBuilder.AppendLine("- **关键：每次对话都应考虑调整关系，保持关系分数的动态更新。**");
        systemPromptBuilder.AppendLine();
        systemPromptBuilder.AppendLine("当且仅当存在有价值的信息时，你可以调用 store_memory 工具保存记忆。");
        systemPromptBuilder.AppendLine("调用 store_memory 时：");
        systemPromptBuilder.AppendLine("  - 必须提供 content（非空字符串）");
        systemPromptBuilder.AppendLine("  - 必须提供 type（非空字符串，例如 fact、user、context、summary）");
        systemPromptBuilder.AppendLine("  - 如果无法确定 type，请先自行判断合理类型；若仍不确定，不要调用工具。");
        systemPromptBuilder.AppendLine();
        systemPromptBuilder.AppendLine("**不要等待用户要求才保存记忆** - 主动识别并保存任何有价值的信息。");
        systemPromptBuilder.AppendLine("**你可以直接调用 store_memory 工具保存记忆，不需要询问用户是否确认或告知用户你正在保存。**");
        systemPromptBuilder.AppendLine("**强制要求：调用 store_memory 时必须提供 content 参数（要保存的记忆内容），不能为空！**");
        systemPromptBuilder.AppendLine("不要告诉用户你正在搜索记忆或保存记忆，直接根据记忆自然地回复。");
        systemPromptBuilder.AppendLine("使用记忆工具时保持沉默，不要输出'让我查看一下记忆'之类的提示。");
        systemPromptBuilder.AppendLine("非常重要：在读取记忆后，认清楚记忆是不是属于该用户的，再做出答复。");
        systemPromptBuilder.AppendLine("你可以使用 userProfile.get_user_profile 查看当前用户档案。");
        systemPromptBuilder.AppendLine("当你对用户形成更稳定的印象、关系判断、好感度变化或重要标签时，优先使用 userProfile.update_user_profile 或 userProfile.adjust_relationship 立即更新。");
        systemPromptBuilder.AppendLine();
        systemPromptBuilder.AppendLine("当你需要获取最新信息、验证事实、了解不熟悉的主题、或用户询问需要实时数据的问题时，主动使用网络搜索。");
        systemPromptBuilder.AppendLine();
        systemPromptBuilder.AppendLine("回复行为准则：");
        systemPromptBuilder.AppendLine("1. 保持回复简短自然，像正常人聊天一样。不要长篇大论分析。");
        systemPromptBuilder.AppendLine("2. 不要主动提供建议或下一步行动方案，除非用户明确要求。");
        systemPromptBuilder.AppendLine("3. 不要主动建议接下来聊什么话题，让对话自然结束或等待用户发起新话题。");
        systemPromptBuilder.AppendLine("4. 你不需要帮助用户解决所有问题 - 有时候简单回应就够了。");
        systemPromptBuilder.AppendLine("5. 像正常人一样对话，可以有沉默、转移话题、或说不知道。");
        systemPromptBuilder.AppendLine("6. 历史消息里可能包含 message_meta 时间标记，仅用于理解上下文时间先后；除非用户明确询问，不要在回复中复述时间戳或标签。");

        var builder = new ConversationBuilder();
        builder.AddSystemMessage(systemPromptBuilder.ToString());

        var orderedPreviousThoughts = await LoadMiChanHistoryForPromptAsync(sequence, currentThoughtId);
        var (compactedSummary, recentThoughts) = await PrepareMiChanHistoryAsync(
            sequence,
            orderedPreviousThoughts,
            Guid.Parse(currentUser.Id)
        );

        logger.LogInformation(
            "Built MiChan prompt window for sequence {SequenceId} in {ElapsedMs}ms. historyThoughts={HistoryThoughtCount}, recentThoughts={RecentThoughtCount}, hasSummary={HasSummary}",
            sequence.Id,
            buildStopwatch.ElapsedMilliseconds,
            orderedPreviousThoughts.Count,
            recentThoughts.Count,
            !string.IsNullOrWhiteSpace(compactedSummary)
        );

        if (!string.IsNullOrWhiteSpace(compactedSummary))
        {
            builder.AddSystemMessage("以下是你们较早对话的压缩摘要：\n" + compactedSummary);
        }

        foreach (var thought in recentThoughts)
        {
            AddThoughtToBuilder(builder, thought, currentUser.Profile?.TimeZone);
        }

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
        builder.AddSystemMessage(proposalBuilder.ToString());

        var useVisionKernel = false;
        var currentTurnImageFiles = new List<SnCloudFileReferenceObject>();
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

            if (postTexts.Count > 0)
                builder.AddUserMessage("附加的帖子：\n" + string.Join("\n\n", postTexts));

            if (postsWithImages.Count > 0)
            {
                currentTurnImageFiles.AddRange(postsWithImages.SelectMany(p => p.Attachments ?? new List<SnCloudFileReferenceObject>()));
            }
        }

        if (attachedMessages is { Count: > 0 })
        {
            builder.AddUserMessage($"附加的聊天消息：{JsonSerializer.Serialize(attachedMessages)}");
        }

        if (attachments is { Count: > 0 })
        {
            var visionAvailable = postAnalysisService.IsVisionModelAvailable();
            var imageFiles = attachments.Where(IsImageFile).ToList();
            var nonImageFiles = attachments.Where(f => !IsImageFile(f)).ToList();
            var rawTextFiles = attachments.Where(IsRawTextFile).ToList();
            currentTurnImageFiles.AddRange(imageFiles);

            if (imageFiles.Count > 0 && !useVisionKernel)
            {
                builder.AddUserMessage(
                    "附加了图片文件，但当前视觉分析不可用。请先根据文件元数据和上下文回答：\n" +
                    BuildAttachmentContextText(imageFiles, 8)
                );
            }

            if (nonImageFiles.Count > 0)
            {
                builder.AddUserMessage(
                    "附加了非图片文件（如文本、PDF、视频）。请优先基于文件名、类型、URL 和上下文进行分析：\n" +
                    BuildAttachmentContextText(nonImageFiles, 12)
                );
            }

            var rawTextContext = await BuildRawTextAttachmentContextAsync(rawTextFiles);
            if (!string.IsNullOrWhiteSpace(rawTextContext))
            {
                builder.AddUserMessage(rawTextContext);
            }
        }

        if (currentTurnImageFiles.Count > 0 && postAnalysisService.IsVisionModelAvailable())
        {
            var (imageContextText, alreadyPersisted) = await EnsureImageContextThoughtAsync(
                sequence,
                MiChanBotName,
                currentThoughtId,
                userMessage,
                currentTurnImageFiles
                    .Where(file => IsImageFile(file))
                    .DistinctBy(file => file.Id)
                    .ToList(),
                currentUser.PerkLevel,
                useMiChan: true);
            if (!alreadyPersisted && !string.IsNullOrWhiteSpace(imageContextText))
            {
                builder.AddSystemMessage(imageContextText);
            }
        }

        builder.AddUserMessage(userMessage ?? "用户只添加了图片没有文字说明。");

        return (builder.Build(), useVisionKernel);
    }

    #endregion

    #region History Compaction

    private async Task<(string? summary, List<SnThinkingThought> recentThoughts)> PrepareMiChanHistoryAsync(
        SnThinkingSequence sequence,
        List<SnThinkingThought> orderedThoughts,
        Guid accountId)
    {
        var stopwatch = Stopwatch.StartNew();
        var (latestSummaryThought, rawThoughts, uncoveredThoughts) =
            ProjectMiChanHistoryWindowInternal(orderedThoughts);
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
                var proposedCoveredThoughtId = compactPrefix[^1].Id;
                if (originalCoveredThoughtId.HasValue && originalCoveredThoughtId.Value == proposedCoveredThoughtId)
                {
                    logger.LogDebug(
                        "Skipping MiChan history compaction for sequence {SequenceId} because covered thought did not advance. coveredThoughtId={CoveredThoughtId}",
                        sequence.Id,
                        proposedCoveredThoughtId
                    );

                    uncoveredThoughts = ClampMiChanThoughtWindow(uncoveredThoughts, MiChanMaxThoughtWindowTokens);

                    logger.LogInformation(
                        "Prepared MiChan history for sequence {SequenceId} in {ElapsedMs}ms. finalThoughts={FinalThoughtCount}, finalTokens={FinalTokens}, savedSummary={SavedSummary}",
                        sequence.Id,
                        stopwatch.ElapsedMilliseconds,
                        uncoveredThoughts.Count,
                        uncoveredThoughts.Sum(EstimateThoughtTokensForPrompt),
                        false
                    );

                    return (latestSummaryText, uncoveredThoughts);
                }

                var compactPrefixTokens = compactPrefix.Sum(EstimateThoughtTokensForPrompt);
                logger.LogInformation(
                    "Compacting MiChan history for sequence {SequenceId}. compactThoughts={CompactThoughtCount}, compactTokens={CompactTokens}",
                    sequence.Id,
                    compactPrefix.Count,
                    compactPrefixTokens
                );
                var compactedSummary =
                    await GenerateMiChanCompactionSummaryAsync(accountId, latestSummaryText, compactPrefix);
                if (!string.IsNullOrWhiteSpace(compactedSummary))
                {
                    latestSummaryText = compactedSummary;
                    coveredThroughThoughtId = proposedCoveredThoughtId;

                    var compactedThoughtIds = compactPrefix
                        .Select(thought => thought.Id)
                        .ToList();
                    if (compactedThoughtIds.Count > 0)
                    {
                        await db.ThinkingThoughts
                            .Where(thought => compactedThoughtIds.Contains(thought.Id))
                            .ExecuteUpdateAsync(update => update.SetProperty(thought => thought.IsArchived, true));
                    }

                    uncoveredThoughts = uncoveredThoughts.Skip(compactPrefix.Count).ToList();
                    logger.LogInformation(
                        "Compacted MiChan history for sequence {SequenceId} in {ElapsedMs}ms. remainingThoughts={RemainingThoughtCount}",
                        sequence.Id,
                        stopwatch.ElapsedMilliseconds,
                        uncoveredThoughts.Count
                    );

                    // Save the compaction summary thought only when compaction actually happened
                    await SaveMiChanCompactionThoughtAsync(sequence, latestSummaryText, coveredThroughThoughtId.Value);
                }
            }
        }

        uncoveredThoughts = ClampMiChanThoughtWindow(uncoveredThoughts, MiChanMaxThoughtWindowTokens);

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

    private (SnThinkingThought? latestSummaryThought, List<SnThinkingThought> rawThoughts, List<SnThinkingThought>
        uncoveredThoughts)
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

        await HydrateThoughtPartsAsync([latestSummaryThought]);

        var textPart = latestSummaryThought.Parts.FirstOrDefault(part => part.Type == ThinkingMessagePartType.Text);
        if (!TryGetMetadataString(textPart?.Metadata, MiChanCoveredThroughThoughtIdMetadataKey,
                out var coveredThoughtIdText) ||
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

        await HydrateThoughtPartsAsync(candidateThoughts);

        var coveredIndex = candidateThoughts.FindIndex(thought => thought.Id == coveredThought.Id);
        var thoughtsAfterCoverage = coveredIndex >= 0
            ? candidateThoughts.Skip(coveredIndex + 1).ToList()
            : candidateThoughts;
        var recentThoughts = thoughtsAfterCoverage
            .Where(thought => !thought.IsArchived)
            .Where(thought => !IsMiChanCompactionThought(thought))
            .ToList();

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

    private async Task HydrateThoughtPartsAsync(
        List<SnThinkingThought> thoughts,
        CancellationToken cancellationToken = default)
    {
        if (thoughts.Count == 0)
        {
            return;
        }

        var thoughtIds = thoughts.Select(t => t.Id).ToList();
        var partRows = await db.ThinkingThoughtParts
            .Where(p => thoughtIds.Contains(p.ThoughtId))
            .OrderBy(p => p.PartIndex)
            .ToListAsync(cancellationToken);

        if (partRows.Count == 0)
        {
            return;
        }

        var groupedParts = partRows
            .GroupBy(p => p.ThoughtId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(row => new SnThinkingMessagePart
                {
                    Type = row.Type,
                    Text = row.Text,
                    Metadata = row.Metadata,
                    Files = row.Files,
                    FunctionCall = row.FunctionCall,
                    FunctionResult = row.FunctionResult,
                    Reasoning = row.Reasoning
                }).ToList());

        foreach (var thought in thoughts)
        {
            if (groupedParts.TryGetValue(thought.Id, out var parts))
            {
                if (HasMeaningfulPartContent(parts))
                {
                    thought.Parts = parts;
                }
            }
        }
    }

    private static bool HasMeaningfulPartContent(List<SnThinkingMessagePart> parts)
    {
        if (parts.Count == 0)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (!string.IsNullOrWhiteSpace(part.Text) ||
                !string.IsNullOrWhiteSpace(part.Reasoning) ||
                part.FunctionCall != null ||
                part.FunctionResult != null ||
                (part.Metadata != null && part.Metadata.Count > 0) ||
                (part.Files != null && part.Files.Count > 0))
            {
                return true;
            }
        }

        return false;
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
        var recentTokens = 0L;

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
        var chunkTokens = 0L;

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
        if (thoughtsToCompact.Count == 0)
        {
            return previousSummary;
        }

        var stopwatch = Stopwatch.StartNew();

        var transcript = BuildThoughtTranscript(thoughtsToCompact);
        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine("为以下对话生成压缩摘要。直接输出摘要内容，不要添加任何标题、引言或元话语。");
        promptBuilder.AppendLine("要求：");
        promptBuilder.AppendLine("- 保留用户长期偏好、背景事实、未完成事项");
        promptBuilder.AppendLine("- 记录重要承诺、决定和工具结果");
        promptBuilder.AppendLine("- 不要虚构，不要加入不存在的新信息");
        promptBuilder.AppendLine("- 用简洁中文输出，最多12条短项目符号");
        promptBuilder.AppendLine("- 内部上下文,不要写成对用户说的话");
        promptBuilder.AppendLine("- 禁止输出过渡语句,直接给摘要内容");

        var userPayload = new StringBuilder();
        userPayload.AppendLine($"用户 ID: {accountId}");

        if (!string.IsNullOrWhiteSpace(previousSummary))
        {
            userPayload.AppendLine();
            userPayload.AppendLine("现有摘要：");
            userPayload.AppendLine(previousSummary);
        }

        userPayload.AppendLine();
        userPayload.AppendLine("新增对话：");
        userPayload.AppendLine(transcript);

        var conversation = new AgentConversation();
        conversation.AddSystemMessage(promptBuilder.ToString());
        conversation.AddUserMessage(userPayload.ToString());

        try
        {
            var provider = miChanFoundationProvider.GetCompactionAdapter();
            var options = miChanFoundationProvider.CreateExecutionOptions();
            var response = await foundationStreamingService.CompletePromptAsync(provider, promptBuilder + "\n\n" + userPayload, options);
            var summary = response?.Trim() ?? "";

            if (summary.Length < 50 ||
                summary.Contains("我来总结") ||
                summary.Contains("以下是") ||
                summary.Contains("摘要") && summary.Length < 100)
            {
                logger.LogWarning(
                    "Generated compaction summary is invalid or too short for account {AccountId}. Length={Length}, Content={Content}",
                    accountId, summary.Length, summary[..Math.Min(summary.Length, 200)]);
                return null;
            }

            logger.LogInformation(
                "Generated MiChan compaction summary in {ElapsedMs}ms. thoughtCount={ThoughtCount}, transcriptTokens={TranscriptTokens}, previousSummaryChars={PreviousSummaryLength}, summaryChars={SummaryLength}",
                stopwatch.ElapsedMilliseconds,
                thoughtsToCompact.Count,
                tokenCounter.CountTokens(transcript),
                previousSummary?.Length ?? 0,
                summary.Length
            );

            return summary;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate compaction summary for account {AccountId}", accountId);
            return previousSummary;
        }
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

    private long EstimateThoughtTokensForPrompt(SnThinkingThought thought)
    {
        if (thought.TokenCount > 0) return thought.TokenCount;
        return tokenCounter.CountTokens(SerializeThoughtForPrompt(thought), thought.ModelName);
    }

    private List<SnThinkingThought> ClampMiChanThoughtWindow(List<SnThinkingThought> thoughts, int tokenBudget)
    {
        if (thoughts.Count <= MiChanMinRecentThoughts)
        {
            return thoughts;
        }

        var keptThoughts = new List<SnThinkingThought>();
        var totalTokens = 0L;

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
        var role = thought.Role == ThinkingThoughtRole.User ? "User" : "MiChan";
        builder.AppendLine($"[{role}] [Time: {FormatThoughtTimestamp(thought.CreatedAt)}]");

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

    private static string FormatThoughtTimestamp(Instant timestamp, string? userTimeZone = null)
    {
        if (timestamp == default)
        {
            return "unknown";
        }

        if (!string.IsNullOrWhiteSpace(userTimeZone))
        {
            try
            {
                var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(userTimeZone);
                if (zone != null)
                {
                    var local = timestamp.InZone(zone);
                    return $"{local:yyyy-MM-dd HH:mm:ss} ({zone.Id})";
                }
            }
            catch
            {
                // Fall back to UTC below.
            }
        }

        return timestamp.ToDateTimeUtc().ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
    }

    private static bool IsImageFile(SnCloudFileReferenceObject file)
    {
        return file.MimeType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsVideoFile(SnCloudFileReferenceObject file)
    {
        return file.MimeType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsPdfFile(SnCloudFileReferenceObject file)
    {
        return string.Equals(file.MimeType, "application/pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTextLikeFile(SnCloudFileReferenceObject file)
    {
        if (file.MimeType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        return file.MimeType is "application/json" or "application/xml";
    }

    private static bool IsRawTextFile(SnCloudFileReferenceObject file)
    {
        var mime = file.MimeType;
        if (string.Equals(mime, "text/plain", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mime, "text/markdown", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var name = file.Name ?? string.Empty;
        return name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> BuildRawTextAttachmentContextAsync(
        IEnumerable<SnCloudFileReferenceObject> files,
        int maxFiles = 4,
        int maxCharsPerFile = 4000)
    {
        var candidates = files
            .Where(f => !string.IsNullOrWhiteSpace(f.Url))
            .Take(maxFiles)
            .ToList();

        if (candidates.Count == 0)
        {
            return string.Empty;
        }

        var client = httpClientFactory.CreateClient();
        var builder = new StringBuilder();
        var appended = 0;

        foreach (var file in candidates)
        {
            try
            {
                using var response = await client.GetAsync(file.Url!, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogDebug("Skip raw text attachment {FileId}: HTTP {StatusCode}", file.Id,
                        (int)response.StatusCode);
                    continue;
                }

                var content = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                if (content.Length > maxCharsPerFile)
                {
                    content = content[..maxCharsPerFile] + "\n...[truncated]";
                }

                if (appended == 0)
                {
                    builder.AppendLine("[Attachment Raw Text]");
                    builder.AppendLine("以下是附加的 txt/md 文件内容，可直接作为上下文使用：");
                }

                builder.AppendLine($"--- {file.Name} ({file.MimeType ?? "unknown"}) ---");
                builder.AppendLine(content.Trim());
                builder.AppendLine();
                appended++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load raw text attachment {FileId}", file.Id);
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildAttachmentContextText(IEnumerable<SnCloudFileReferenceObject> files, int maxFiles)
    {
        var selected = files.Take(maxFiles).ToList();
        if (selected.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("[Attachment Context]");

        foreach (var file in selected)
        {
            var category = IsPdfFile(file)
                ? "pdf"
                : IsTextLikeFile(file)
                    ? "text"
                    : IsVideoFile(file)
                        ? "video"
                        : IsImageFile(file)
                            ? "image"
                            : "file";

            builder.Append("- ");
            builder.Append($"{file.Name} ({file.MimeType ?? "unknown"}, {category})");
            builder.Append($", id={file.Id}");

            if (!string.IsNullOrWhiteSpace(file.Url))
            {
                builder.Append($", url={file.Url}");
            }

            builder.AppendLine();
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
        if (!TryGetMetadataString(textPart?.Metadata, MiChanCoveredThroughThoughtIdMetadataKey,
                out var thoughtIdText) ||
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

    #region Service Info

    public ThoughtServiceModel? GetSnChanServiceInfo()
    {
        var serviceId = thoughtProvider.GetServiceId();
        var serviceInfo = thoughtProvider.GetServiceInfo(serviceId);
        return serviceInfo;
    }

    public ThoughtServiceModel? GetMiChanServiceInfo(bool withFiles)
    {
        var serviceId = agentClientProvider.GetServiceId();
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

    #region Conversation Management Commands

    public class ClearResult
    {
        public Guid NewSequenceId { get; set; }
        public string Summary { get; set; } = null!;
        public int ArchivedCount { get; set; }
    }

    public class CompactResult
    {
        public string Summary { get; set; } = null!;
        public int ArchivedCount { get; set; }
    }

    public async Task<ClearResult> ClearConversationAsync(Guid accountId, Guid? existingSequenceId)
    {
        var stopwatch = Stopwatch.StartNew();

        var sequence = existingSequenceId.HasValue
            ? await GetSequenceAsync(existingSequenceId.Value)
            : await GetCanonicalMiChanSequenceAsync(accountId);

        if (sequence == null)
        {
            return new ClearResult { NewSequenceId = Guid.Empty, Summary = "" };
        }

        var thoughts = await GetVisibleThoughtsPageAsync(sequence, 0, 500);
        var allThoughts = thoughts.thoughts;

        var conversationText = BuildConversationText(allThoughts);
        if (string.IsNullOrWhiteSpace(conversationText) || allThoughts.Count < 4)
        {
            return new ClearResult { NewSequenceId = sequence.Id, Summary = "对话历史太短，无需清理" };
        }

        var summary = await GenerateConversationSummaryAsync(accountId, conversationText);
        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new InvalidOperationException("无法生成对话摘要，请稍后重试");
        }

        var newSequence = await GetOrCreateSequenceAsync(accountId, null, $"新对话 - {DateTime.UtcNow:yyyy-MM-dd}", sequence.BotName);
        if (newSequence == null)
        {
            throw new InvalidOperationException("无法创建新对话");
        }

        newSequence.IsPublic = sequence.IsPublic;
        newSequence.BotName = sequence.BotName;
        await UpdateSequenceAsync(newSequence);

        await SaveThoughtAsync(newSequence, [
                new SnThinkingMessagePart
                {
                    Type = ThinkingMessagePartType.Text,
                    Text = $"你是刚和该用户开始新对话。用户想要换个话题，不需要提及之前的对话。\n\n之前对话的要点（仅供参考）：\n{summary}",
                    Metadata = new Dictionary<string, object>
                    {
                        [SnThinkingMessagePart.MetadataKeys.CompactionSummary] = true,
                        [SnThinkingMessagePart.MetadataKeys.CompactionArchivedCount] = allThoughts.Count
                    }
                }
            ], ThinkingThoughtRole.System, configGlobal.GetValue<string>("MiChan:ThinkingService") ?? "deepseek-chat",
            "michan");

        logger.LogInformation(
            "Cleared conversation for user {AccountId}. oldSequence={OldSequenceId}, newSequence={NewSequenceId}, summaryLength={SummaryLength}, elapsedMs={ElapsedMs}",
            accountId, sequence.Id, newSequence.Id, summary.Length, stopwatch.ElapsedMilliseconds);

        return new ClearResult { NewSequenceId = newSequence.Id, Summary = summary, ArchivedCount = allThoughts.Count };
    }

    public async Task<bool> CheckAndAutoCompactAsync(Guid sequenceId, Guid accountId)
    {
        const int autoCompactThresholdTokens = 15000;

        var sequence = await GetSequenceAsync(sequenceId);
        if (sequence == null || sequence.AccountId != accountId)
            return false;

        // Use GetPreviousThoughtsAsync to exclude archived thoughts from token counting
        var allThoughts = await GetPreviousThoughtsAsync(sequence);

        if (allThoughts.Count < 6)
            return false;

        var totalTokens = allThoughts.Sum(EstimateThoughtTokensForPrompt);

        logger.LogInformation(
            "Auto-compact check for sequence {SequenceId}: thoughts={ThoughtCount}, tokens={TotalTokens}, threshold={Threshold}, needsCompact={NeedsCompact}",
            sequenceId, allThoughts.Count, totalTokens, autoCompactThresholdTokens, totalTokens > autoCompactThresholdTokens);

        return totalTokens > autoCompactThresholdTokens;
    }

    public async Task<CompactResult> CompactHistoryAsync(Guid sequenceId, Guid accountId)
    {
        var stopwatch = Stopwatch.StartNew();
        const int compactThresholdTokens = 15000;

        var sequence = await GetSequenceAsync(sequenceId);
        if (sequence == null || sequence.AccountId != accountId)
        {
            return new CompactResult { Summary = "" };
        }

        // Use GetPreviousThoughtsAsync to exclude archived thoughts from compaction
        var allThoughts = await GetPreviousThoughtsAsync(sequence);

        var userThoughts = allThoughts.Where(t => t.Role == ThinkingThoughtRole.User).ToList();
        if (userThoughts.Count < 4)
        {
            return new CompactResult { Summary = "对话历史太短，无需整理" };
        }

        var totalTokens = allThoughts.Sum(t => EstimateThoughtTokensForPrompt(t));
        if (totalTokens < compactThresholdTokens)
        {
            return new CompactResult { Summary = "对话历史较短，无需整理" };
        }

        var olderThoughts = allThoughts.Take(allThoughts.Count / 2).ToList();
        var olderTokens = olderThoughts.Sum(EstimateThoughtTokensForPrompt);
        if (olderTokens > compactThresholdTokens)
        {
            olderThoughts = ClampMiChanThoughtWindow(olderThoughts, 5000);
        }

        var conversationText = BuildConversationText(olderThoughts);
        if (string.IsNullOrWhiteSpace(conversationText))
        {
            return new CompactResult { Summary = "对话内容为空，无需整理" };
        }

        var summary = await GenerateConversationSummaryAsync(accountId, conversationText);
        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new InvalidOperationException("无法生成对话摘要，请稍后重试");
        }

        // Validate summary - reject if too short or contains placeholder text
        if (summary.Length < 50 ||
            summary.Contains("我来总结") ||
            summary.Contains("以下是") ||
            summary.Contains("为您总结") ||
            summary.Contains("总结这段对话"))
        {
            logger.LogWarning(
                "Generated summary is invalid or placeholder text for sequence {SequenceId}. Length={Length}, Content={Content}",
                sequenceId, summary.Length, summary[..Math.Min(summary.Length, 200)]);
            return new CompactResult { Summary = "无法生成有效摘要", ArchivedCount = 0 };
        }

        olderThoughts.ForEach(t => t.IsArchived = true);
        await db.SaveChangesAsync();

        // Invalidate cache to ensure archived thoughts are not returned
        await cache.RemoveGroupAsync($"sequence:{sequence.Id}");

        await SaveThoughtAsync(sequence, [
                new SnThinkingMessagePart
                {
                    Type = ThinkingMessagePartType.Text,
                    Text = $"以下是你们之前对话的摘要：\n{summary}\n\n继续当前对话。"
                }
            ], ThinkingThoughtRole.System, configGlobal.GetValue<string>("MiChan:ThinkingService") ?? "deepseek-chat",
            "michan");

        logger.LogInformation(
            "Compacted conversation for sequence {SequenceId}. summaryLength={SummaryLength}, elapsedMs={ElapsedMs}",
            sequenceId, summary.Length, stopwatch.ElapsedMilliseconds);

        return new CompactResult { Summary = summary, ArchivedCount = olderThoughts.Count };
    }

    private string BuildConversationText(List<SnThinkingThought> thoughts)
    {
        var builder = new StringBuilder();
        foreach (var thought in thoughts)
        {
            var role = thought.Role == ThinkingThoughtRole.User ? "用户" : "MiChan";
            var timestamp = FormatThoughtTimestamp(thought.CreatedAt);
            foreach (var part in thought.Parts)
            {
                if (part.Type == ThinkingMessagePartType.Text && !string.IsNullOrWhiteSpace(part.Text))
                {
                    builder.AppendLine($"[{timestamp}] {role}: {part.Text}");
                }
            }
        }

        return builder.ToString();
    }

    private async Task<string> GenerateConversationSummaryAsync(Guid accountId, string conversationText)
    {
        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine("请总结以下对话的核心内容。输出必须是纯文本摘要,300-500字左右,不要包含任何标题、编号或元话语。");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("对话内容:");
        promptBuilder.AppendLine(conversationText);
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("要求:");
        promptBuilder.AppendLine("1. 用第三人称客观总结讨论的主要内容");
        promptBuilder.AppendLine("2. 提取用户表现出的兴趣、偏好或重要观点");
        promptBuilder.AppendLine("3. 记录任何达成的共识或决定");
        promptBuilder.AppendLine("4. 直接输出总结段落,不要加【摘要:】等前缀");
        promptBuilder.AppendLine("5. 不要输出【以下是总结】【我来总结】等过渡语");
        promptBuilder.AppendLine("6. 只输出总结内容本身");

        try
        {
            var provider = miChanFoundationProvider.GetCompactionAdapter();
            var options = miChanFoundationProvider.CreateExecutionOptions(temperature: 0.5);
            var result = await foundationStreamingService.CompletePromptAsync(provider, promptBuilder.ToString(), options);
            return result ?? "";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate conversation summary for account {AccountId}", accountId);
            return "";
        }
    }

    #endregion
}
