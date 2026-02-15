# Sponsor Page Subscription Implementation

This document describes the implementation of sponsor page subscriptions that increment sponsor badge levels.

## Overview

The implementation adds a new subscription type `solian.sponsor.page` that automatically increments a user's sponsor badge level each time they subscribe to a sponsor page.

## Changes Made

### 1. Subscription Type Definition (`DysonNetwork.Shared/Models/Subscription.cs`)

Added a new subscription type:
```csharp
public const string SponsorPage = "solian.sponsor.page";
```

Added the subscription to the type dictionary with:
- Base price: 100 Source Points
- No level requirement
- No group identifier (standalone subscription)

### 2. Badge Level Increment Logic (`DysonNetwork.Wallet/Payment/SubscriptionService.cs`)

#### New Method: `HandleSponsorPageSubscriptionAsync`
- Checks for existing sponsor badge for the account
- Increments the badge level by 1 for each new subscription
- Creates a new sponsor badge if none exists
- Updates badge metadata with subscription ID and level
- Handles errors gracefully without failing subscription creation

#### Integration Points:
- Called automatically when a sponsor page subscription is created
- Updates badge `ActivatedAt` and `ExpiredAt` to match subscription dates
- Stores subscription ID in badge metadata for tracking

### 3. Badge Structure

Sponsor badges use the following structure:
- **Type**: `"sponsor"`
- **Label**: `"Sponsor"`
- **Caption**: `"Level {level}"`
- **Meta**: Contains `level` (int) and `subscription_id` (string)

## How It Works

1. User subscribes to a sponsor page (creates `solian.sponsor.page` subscription)
2. Subscription service detects the sponsor page type
3. Service checks for existing sponsor badge
4. Badge level is incremented (or set to 1 for new badges)
5. Badge metadata is updated with new level and subscription ID
6. Badge dates are synchronized with subscription dates

## Testing

A test script (`test_sponsor_subscription.cs`) is provided to verify the functionality:

```csharp
var test = new SponsorSubscriptionTest(db, subscriptionService);
await test.RunTest();
```

The test:
1. Creates a test account
2. Creates three sponsor page subscriptions
3. Verifies badge levels increment: 1 → 2 → 3
4. Reports pass/fail status

## Database Schema

The implementation uses existing tables:
- `wallet_subscriptions` - Stores sponsor page subscriptions
- `badges` - Stores sponsor badges with level metadata

No database migrations are required as the implementation uses existing infrastructure.

## Error Handling

- Badge update failures are logged but don't prevent subscription creation
- Invalid badge metadata is handled gracefully
- Database errors are caught and logged

## Future Enhancements

Potential improvements:
- Badge expiration handling when subscriptions expire
- Badge level decay over time
- Multiple sponsor page support (different badges per sponsor)
- Badge display customization
- Sponsor page management interface