using DysonNetwork.Insight.MiChan;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Extensions;

namespace DysonNetwork.Insight.Thought;

public class FreeQuotaService(
    AppDatabase db,
    FreeQuotaConfig config,
    MiChanConfig miChanConfig,
    ILogger<FreeQuotaService> logger)
{
    private readonly AppDatabase _db = db;
    private readonly FreeQuotaConfig _config = config;
    private readonly MiChanConfig _miChanConfig = miChanConfig;
    private readonly ILogger<FreeQuotaService> _logger = logger;

    public bool IsEnabled => _config.Enabled;

    public int TokensPerDay => _config.TokensPerDay;

    public int ResetPeriodHours => _config.ResetPeriodHours;

    public async Task<(long freeRemaining, long freeUsed)> GetFreeQuotaStatusAsync(Guid accountId)
    {
        if (!_config.Enabled)
            return (0, 0);

        var now = SystemClock.Instance.GetCurrentInstant();
        var sequences = await _db.ThinkingSequences
            .Where(s => s.AccountId == accountId)
            .ToListAsync();

        if (!sequences.Any())
            return (_config.TokensPerDay, 0);

        var totalDailyFreeUsed = 0L;
        foreach (var seq in sequences)
        {
            ResetIfNeeded(seq, now);
            totalDailyFreeUsed += seq.DailyFreeTokensUsed;
        }

        var remaining = _config.TokensPerDay - totalDailyFreeUsed;
        return (Math.Max(0, remaining), totalDailyFreeUsed);
    }

    public async Task<long> ConsumeFreeTokensAsync(Guid accountId, long tokens, CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled)
            return 0;

        var accountSequences = await _db.ThinkingSequences
            .Where(s => s.AccountId == accountId)
            .ToListAsync(cancellationToken);

        if (!accountSequences.Any())
            return 0;

        var now = SystemClock.Instance.GetCurrentInstant();
        var remaining = tokens;
        var sequencesToUpdate = new List<SnThinkingSequence>();

        foreach (var seq in accountSequences)
        {
            if (remaining <= 0)
                break;

            ResetIfNeeded(seq, now);

            var dailyFreeUsed = seq.DailyFreeTokensUsed;
            var dailyFreeRemaining = _config.TokensPerDay - dailyFreeUsed;

            if (dailyFreeRemaining <= 0)
                continue;

            var toConsume = Math.Min(remaining, dailyFreeRemaining);
            seq.DailyFreeTokensUsed += toConsume;
            seq.FreeTokens += toConsume;
            seq.PaidToken += toConsume;
            remaining -= toConsume;
            sequencesToUpdate.Add(seq);

            _logger.LogDebug("Consumed {Tokens} free tokens from sequence {SequenceId}. Daily used: {DailyUsed}",
                toConsume, seq.Id, seq.DailyFreeTokensUsed);
        }

        if (sequencesToUpdate.Any())
        {
            _db.ThinkingSequences.UpdateRange(sequencesToUpdate);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return tokens - remaining;
    }

    public async Task ResetAllQuotasAsync(CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled)
            return;

        var now = SystemClock.Instance.GetCurrentInstant();
        var sequences = await _db.ThinkingSequences.ToListAsync(cancellationToken);

        var count = 0;
        foreach (var seq in sequences)
        {
            if (seq.DailyFreeTokensUsed > 0 || seq.LastFreeQuotaResetAt.HasValue)
            {
                seq.DailyFreeTokensUsed = 0;
                seq.LastFreeQuotaResetAt = now;
                count++;
            }
        }

        if (count > 0)
        {
            _db.ThinkingSequences.UpdateRange(sequences);
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Reset free quota for {Count} sequences", count);
        }
    }

    public async Task ResetQuotasForAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled)
            return;

        var now = SystemClock.Instance.GetCurrentInstant();
        var sequences = await _db.ThinkingSequences
            .Where(s => s.AccountId == accountId)
            .ToListAsync(cancellationToken);

        foreach (var seq in sequences)
        {
            seq.DailyFreeTokensUsed = 0;
            seq.LastFreeQuotaResetAt = now;
        }

        if (sequences.Any())
        {
            _db.ThinkingSequences.UpdateRange(sequences);
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Reset free quota for account {AccountId}", accountId);
        }
    }

    private void ResetIfNeeded(SnThinkingSequence sequence, Instant now)
    {
        if (!_config.Enabled)
            return;

        var lastReset = sequence.LastFreeQuotaResetAt ?? sequence.CreatedAt;
        var period = Duration.FromHours(_config.ResetPeriodHours);

        if (now - lastReset >= period)
        {
            sequence.DailyFreeTokensUsed = 0;
            sequence.LastFreeQuotaResetAt = now;
            _logger.LogDebug("Reset free quota for sequence {SequenceId}", sequence.Id);
        }
    }
}