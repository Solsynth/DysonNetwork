using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.EntityFrameworkCore;
using PaymentService = DysonNetwork.Shared.Proto.PaymentService;
using TransactionType = DysonNetwork.Shared.Proto.TransactionType;
using WalletService = DysonNetwork.Shared.Proto.WalletService;

namespace DysonNetwork.Insight.Thought;

public class ThoughtService(
    AppDatabase db,
    ICacheService cache,
    PaymentService.PaymentServiceClient paymentService,
    WalletService.WalletServiceClient walletService
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

    public async Task<SnThinkingThought> SaveThoughtAsync(
        SnThinkingSequence sequence,
        string content,
        ThinkingThoughtRole role,
        List<SnThinkingChunk>? chunks = null,
        string? model = null
    )
    {
        // Approximate token count (1 token ≈ 4 characters for GPT-like models)
        var tokenCount = content?.Length / 4 ?? 0;

        var thought = new SnThinkingThought
        {
            SequenceId = sequence.Id,
            Content = content,
            Role = role,
            TokenCount = tokenCount,
            ModelName = model,
            Chunks = chunks ?? new List<SnThinkingChunk>(),
        };
        db.ThinkingThoughts.Add(thought);

        // Update sequence total tokens only for assistant responses
        if (role == ThinkingThoughtRole.Assistant)
            sequence.TotalToken += tokenCount;

        await db.SaveChangesAsync();

        // Invalidate cache for this sequence's thoughts
        await cache.RemoveGroupAsync($"sequence:{sequence.Id}");

        return thought;
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
            }
        }

        await db.SaveChangesAsync();
    }
}
