# Free Daily Token Quota

## Overview

Every user gets a free daily token quota for the Insight AI chat feature. Within this free quota, tokens are automatically marked as "paid" (no billing). Only when users exceed their free quota will they be prompted for pricing.

## Configuration

Add to `appsettings.json` under `Thinking` section:

```json
"Thinking": {
  "FreeQuota": {
    "Enabled": true,
    "TokensPerDay": 10000,
    "ResetPeriodHours": 24
  }
}
```

### Configuration Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `Enabled` | bool | `true` | Enable/disable free quota |
| `TokensPerDay` | int | `10000` | Free tokens per user per day (10k tokens ≈ 15k-20k words) |
| `ResetPeriodHours` | int | `24` | Hours until quota auto-resets |

## How It Works

### Token Consumption Flow

1. **User sends a message** → Assistant generates a response
2. **Token count calculated** → Using accurate TiktokenSharp tokenizer
3. **Check free quota** → 
   - If free quota available → Consume from free quota, mark as `PaidToken`
   - If no free quota → Mark as billable (triggers pricing later)

### Token Fields

Each `SnThinkingSequence` tracks tokens:

| Field | Type | Description |
|-------|------|-------------|
| `TotalToken` | long | Total tokens in conversation |
| `PaidToken` | long | Tokens marked as paid (billable) |
| `FreeTokens` | long | Tokens from free quota |
| `DailyFreeTokensUsed` | long | Daily free tokens consumed |
| `LastFreeQuotaResetAt` | Instant | Last reset timestamp |

### Automatic Reset

The system supports two reset mechanisms:

1. **Per-sequence auto-reset**: Happens on first token consumption after period expires
2. **Scheduled job reset**: Runs daily at midnight (cron: `0 0 0 * * ?`)

## API Integration

The free quota status is automatically tracked and included in billing settlement. No manual API calls needed - consumption happens automatically when saving thoughts.

### FreeQuotaService Methods

```csharp
// Get remaining free tokens and used amount
var (freeRemaining, freeUsed) = await freeQuotaService.GetFreeQuotaStatusAsync(accountId);

// Consume from free quota (auto-returns consumed amount)
var consumed = await freeQuotaService.ConsumeFreeTokensAsync(accountId, tokenCount);

// Manual reset for all accounts (via scheduled job)
await freeQuotaService.ResetAllQuotasAsync();

// Reset specific account
await freeQuotaService.ResetQuotasForAccountAsync(accountId);
```

## Billing Flow

1. User uses AI → Tokens added to conversation
2. Free quota checked → If available, consumed; else marked as billable
3. `TokenBillingJob` runs periodically → Bills unpaid tokens
4. If payment fails → Tokens unpaid, user marked for follow-up
5. If quota exhausted → User gets pricing warning on next use

## Related Files

- `DysonNetwork.Insight/Thought/FreeQuotaConfig.cs` - Configuration
- `DysonNetwork.Insight/Thought/FreeQuotaService.cs` - Service logic
- `DysonNetwork.Insight/Thought/FreeQuotaResetJob.cs` - Scheduled job
- `DysonNetwork.Insight/Startup/ScheduledJobsConfiguration.cs` - Job registration
- `DysonNetwork.Shared/Models/ThinkingSequence.cs` - Model fields