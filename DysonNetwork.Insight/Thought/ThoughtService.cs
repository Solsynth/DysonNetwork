using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Insight.Thought;

public class ThoughtService(AppDatabase db, ICacheService cache)
{
    public async Task<SnThinkingSequence?> GetOrCreateSequenceAsync(Guid accountId, Guid? sequenceId, string? topic = null)
    {
        if (sequenceId.HasValue)
        {
            var seq = await db.ThinkingSequences.FindAsync(sequenceId.Value);
            if (seq == null || seq.AccountId != accountId) return null;
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

    public async Task<SnThinkingThought> SaveThoughtAsync(SnThinkingSequence sequence, string content, ThinkingThoughtRole role)
    {
        var thought = new SnThinkingThought
        {
            SequenceId = sequence.Id,
            Content = content,
            Role = role
        };
        db.ThinkingThoughts.Add(thought);
        await db.SaveChangesAsync();

        // Invalidate cache for this sequence's thoughts
        await cache.RemoveGroupAsync($"sequence:{sequence.Id}");

        return thought;
    }

    public async Task<List<SnThinkingThought>> GetPreviousThoughtsAsync(SnThinkingSequence sequence)
    {
        var cacheKey = $"thoughts:{sequence.Id}";
        var (found, cachedThoughts) = await cache.GetAsyncWithStatus<List<SnThinkingThought>>(cacheKey);
        if (found && cachedThoughts != null)
        {
            return cachedThoughts;
        }

        var thoughts = await db.ThinkingThoughts
            .Where(t => t.SequenceId == sequence.Id)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        // Cache for 10 minutes
        await cache.SetWithGroupsAsync(cacheKey, thoughts, [$"sequence:{sequence.Id}"], TimeSpan.FromMinutes(10));

        return thoughts;
    }

    public async Task<(int total, List<SnThinkingSequence> sequences)> ListSequencesAsync(Guid accountId, int offset, int take)
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
}
