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
    PaymentService.PaymentServiceClient paymentService
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
        string? model = null
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
            logger.LogInformation("Unpaid tokens for {accountId} resulted in zero cost. Marking as paid and unmarking.", accountId);
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
}
