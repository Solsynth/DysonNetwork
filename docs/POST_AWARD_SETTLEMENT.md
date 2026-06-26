# Post Award Settlement

This document describes the deferred post-award payout system in `DysonNetwork.Sphere`.

When a user awards a post with points, the awarded points are not immediately transferred to the publisher. Instead, they are **held by the system** and settled to the publisher's wallet at the next settlement moment (UTC midnight), with the publisher receiving **80%** of the total awarded amount. The remaining 20% stays with the system.

This mirrors the livestream award distribution flow (`LiveStreamService.DistributeAwardsAsync`), which pays out 90% of awards when a stream ends. Post awards use a daily batch settlement instead of an event-driven trigger.

## Overview

The award flow has three stages:

1. **Award creation** — A user calls `POST /api/posts/{id}/awards`. The Sphere service creates a wallet order with `productIdentifier = "posts.award"` and `payeeWalletId = null`. The order's payee is intentionally the system: the awarder's points are debited when the order is paid, and burned to the system wallet (not the publisher).

2. **Award confirmation** — When the order is paid, the Wallet service publishes a `PaymentOrderEvent`. Sphere's event listener calls `PostService.AwardPost`, which inserts a row into `post_awards` and increments `SnPost.AwardedScore`. No wallet transfer happens to the publisher at this stage.

3. **Settlement** — The `PublisherSettlementJob` (Quartz, cron `0 0 0 * * ?`, fires daily at UTC midnight) calls `PublisherService.SettlePostAwardsAsync`. This method pays out 80% of all unsettled positive awards to each publisher's wallet, marks the awards as settled, and leaves awards for publishers without a valid payout configuration held for the next run.

### Why 80%?

The 80/20 split is the platform's revenue share for post awards:

- **80%** → publisher (the content creator)
- **20%** → system (platform fee)

The awarder's full 100% is already debited at order-payment time. The settlement job credits 80% from the system side to the publisher, so the net effect is: awarder loses 100%, publisher gains 80%, system keeps 20%.

### Negative Awards

Negative-attitude awards (`Attitude = Negative`) are **never paid out**. They affect the post's `AwardedScore` (negatively) and feed into the publisher rating calculation, but no wallet transfer is created for them. This matches the livestream award distribution behavior.

## Data Model

### `SnPostAward.SettledAt`

New nullable timestamp column on `post_awards` tracking settlement state:

```csharp
public Instant? SettledAt { get; set; }
```

- `null` — award is pending settlement
- non-null — award has been settled (the instant the payout transaction was created)

The settlement query filters on `WHERE settled_at IS NULL AND attitude = Positive`.

### `SnPublisher.PayoutWalletId`

New nullable wallet reference on `publishers`, used for **organizational** publishers that have no direct `AccountId`:

```csharp
public Guid? PayoutWalletId { get; set; }
```

This mirrors `SnCustomApp.PaymentWalletId` (see `docs/CUSTOM_APP_WALLET_PAYOUTS.md`). Individual publishers do not need this field — their `AccountId` is used to resolve the payout wallet directly.

## Payout Recipient Resolution

The settlement resolves the payout target per publisher:

| Publisher Type | `AccountId` | `PayoutWalletId` | Payout Target |
|----------------|-------------|------------------|---------------|
| Individual | set | ignored | Credited via `CreateTransactionWithAccount(payer: null, payee: AccountId)` |
| Organizational | null | set | Credited via `CreateTransaction(payer: null, payee: PayoutWalletId)` |
| Organizational | null | null | **Held** — awards stay `SettledAt = null` and retry on the next run |

Held awards are not lost. Once an org publisher configures a `PayoutWalletId` (via `PATCH /api/publishers/{name}`), all accumulated held awards are paid out at the next settlement run.

## Settlement Algorithm

`PublisherService.SettlePostAwardsAsync()`:

1. Load all unsettled positive awards: `db.PostAwards.Where(a => a.SettledAt == null && a.Attitude == Positive)`.
2. Awards with no publisher (`Post.PublisherId == null`) are marked settled without payout (orphan cleanup).
3. Group remaining awards by `PublisherId`.
4. For each publisher:
   - Resolve the payout target (individual `AccountId` or org `PayoutWalletId`).
   - If no valid payout target → **skip** (hold the awards; they remain `SettledAt = null`).
   - Compute `payoutAmount = sum(award.Amount) * 0.80m`.
   - If `payoutAmount <= 0` → mark settled, continue.
   - Create a system-side wallet transaction (`DyTransactionType.System`) from `null` payer to the resolved payee.
   - On success → mark all the publisher's awards in this batch as `SettledAt = now`.
   - On failure → log the error, leave awards unsettled (they retry next run).

### Idempotency

The `SettledAt` column makes settlement idempotent. Re-running the job (manually or via the daily cron) never double-pays because the query only selects awards where `SettledAt IS NULL`.

`AggressiveResettle` (`POST /api/publishers/rewards/resettle`) only recomputes rating records — it does **not** touch wallet payouts. Wallet settlement is exclusively driven by `SettlePostAwardsAsync`.

## Scheduling

The settlement runs as part of the existing `PublisherSettlementJob`:

```text
File: DysonNetwork.Sphere/Publisher/PublisherSettlementJob.cs
Cron: 0 0 0 * * ?   (daily at 00:00 UTC)
```

The job executes both settlements in sequence:

1. `SettlePublisherRewards()` — grants experience, social credits, and rating (unchanged)
2. `SettlePostAwardsAsync()` — pays out 80% of pending awards to publisher wallets (new)

The "next settle moment" for any award made during the day is the next UTC midnight.

## API Endpoints

### Award a Post

```http
POST /api/posts/{id}/awards
Authorization: Bearer {token}

{
  "amount": 50,
  "attitude": "Positive",
  "message": "Great post!"
}
```

Returns `{ "order_id": "uuid" }`. The client must then pay the order via the Wallet service (`POST /api/orders/{id}/pay`) to complete the award.

This endpoint is unchanged. It creates the order with `payeeWalletId = null` so the 20% platform fee is retained by the system, and the 80% publisher share is paid out later by the settlement job.

### Get Post Awards

```http
GET /api/posts/{id}/awards?offset=0&take=20
```

Returns paginated award history. Sets `X-Total` header.

### Get Pending Post Awards

```http
GET /api/posts/{id}/awards/pending
```

Returns the pending payout summary for a post:

```json
{
  "count": 3,
  "total_amount": 150.0,
  "payout_amount": 120.0
}
```

- `count` — number of unsettled positive awards
- `total_amount` — sum of pending award amounts (100%)
- `payout_amount` — 80% of total (what the publisher will receive at settlement)

### Settle Post Awards (Manual)

```http
POST /api/publishers/awards/settle
Authorization: Bearer {token}
```

Requires the `publishers.reward.settle` permission. Triggers `SettlePostAwardsAsync` immediately without waiting for the daily cron. Useful for:

- Testing payout configuration changes
- Settling held awards immediately after configuring an org publisher's `PayoutWalletId`
- Operational backfill after a failed run

### Settle All Publisher Rewards (Manual)

```http
POST /api/publishers/rewards/settle
Authorization: Bearer {token}
```

Requires the `publishers.reward.settle` permission. Triggers both `SettlePublisherRewards` and `SettlePostAwardsAsync`.

## Configuring the Payout Wallet (Organizational Publishers)

Organizational publishers (those backed by a realm, without a direct `AccountId`) must configure a payout wallet to receive post award settlements. Without it, their awards are held indefinitely.

### Update Publisher

```http
PATCH /api/publishers/{name}
Authorization: Bearer {token}

{
  "payout_wallet_id": "6f7413d0-44d8-4fd3-84b5-d2603fb0e5a2"
}
```

Setting `payout_wallet_id` requires **Owner** role on the publisher. Set it to `null` to remove the configuration (future awards will be held again).

The wallet must already exist. For organizational publishers, create a realm-owned wallet via the Wallet service:

```http
POST /api/wallets

{
  "realm_id": "uuid-of-the-realm",
  "name": "Publisher Payout Wallet"
}
```

See `docs/MULTI_WALLET.md` for wallet creation details. Realm wallets have `AccountId = null` and `RealmId = <realm-id>`.

## Wallet Transaction Details

Settlement payouts are recorded as `SnWalletTransaction` with:

| Field | Value |
|-------|-------|
| `PayerWalletId` | `null` (system) |
| `PayeeWalletId` | resolved publisher wallet |
| `Currency` | `points` |
| `Amount` | 80% of total pending awards |
| `Type` | `System` |
| `Status` | `Confirmed` (instant) |
| `Remarks` | localized `"posts.award.distribution"` string |

The system-side funding (null payer) is the same pattern used by:

- `LiveStreamService.DistributeAwardsAsync` (90% livestream payout)
- Daily check-in rewards (`AccountEventService`)

## Localization

The settlement remark uses the key `posts.award.distribution`:

| Locale | Value |
|--------|-------|
| `en` | `Post award settlement for {publisher}` |
| `zh-hans` | `发布者 {publisher} 的帖子打赏结算` |

## Database

### Migration

```text
DysonNetwork.Sphere/Migrations/20260626141335_AddPostAwardPayout.cs
```

### Column: `post_awards.settled_at`

| Column | Type | Nullable |
|--------|------|----------|
| `settled_at` | `timestamp with time zone` | yes |

### Column: `publishers.payout_wallet_id`

| Column | Type | Nullable |
|--------|------|----------|
| `payout_wallet_id` | `uuid` | yes |

## File Reference

| File | Purpose |
|------|---------|
| `DysonNetwork.Shared/Models/Post.cs` | `SnPostAward.SettledAt` field |
| `DysonNetwork.Shared/Models/Publisher.cs` | `SnPublisher.PayoutWalletId` field |
| `DysonNetwork.Sphere/Publisher/PublisherService.cs` | `SettlePostAwardsAsync()` — core settlement logic |
| `DysonNetwork.Sphere/Publisher/PublisherSettlementJob.cs` | Quartz job wiring (daily UTC midnight) |
| `DysonNetwork.Sphere/Publisher/PublisherController.cs` | `PayoutWalletId` in update DTO; manual settle endpoints |
| `DysonNetwork.Sphere/Post/PostActionController.cs` | `GET /api/posts/{id}/awards/pending` endpoint |
| `DysonNetwork.Sphere/Post/PostService.cs` | `AwardPost()` — inserts the award row (unchanged) |
| `DysonNetwork.Sphere/Resources/Locales/en.json` | `posts.award.distribution` string |
| `DysonNetwork.Sphere/Resources/Locales/zh-hans.json` | `posts.award.distribution` string (Chinese) |
| `DysonNetwork.Shared/Registry/RemotePaymentService.cs` | gRPC client used to create settlement transactions |

## Comparison with Livestream Award Distribution

| Aspect | Post Awards | Livestream Awards |
|--------|-------------|-------------------|
| Payout ratio | 80% | 90% |
| Trigger | Daily cron job (UTC midnight) | Stream end event |
| Tracking | `SnPostAward.SettledAt` | `SnLiveStream.DistributedAwardAmount` |
| Negative awards | Never paid out | Not counted |
| Org publisher support | Via `PayoutWalletId` | Via `Publisher.AccountId` only |
| Idempotency | `SettledAt IS NULL` filter | `DistributedAwardAmount` delta |
| File | `PublisherService.SettlePostAwardsAsync` | `LiveStreamService.DistributeAwardsAsync` |
