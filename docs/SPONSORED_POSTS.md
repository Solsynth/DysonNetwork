# Sponsored Posts

This document describes the sponsored ads system in `DysonNetwork.Sphere`.

Users can spend **golds** (golden points) to bid on ad placement for any public post. The system runs an hourly auction that selects the highest-bid post and promotes it to the first position in the featured posts list and the timeline feed.

## Overview

The sponsored ads system is a competitive bidding platform for post visibility:

- **Currency:** golds (`WalletCurrency.GoldenPoint`)
- **Minimum bid:** 5 golds per bid
- **Bid duration:** 24 hours from confirmation
- **Auction cycle:** hourly (at the top of each UTC hour)
- **Winner selection:** post with the highest total active bids
- **Placement:** first position in `GET /api/posts/featured` and `GET /api/timeline`
- **Serialization:** winning posts carry `"sponsored": true` in API responses

Multiple users can bid on the same post — their bids pool together to increase that post's total. Users can also place multiple bids on the same post over time ("adding more"). Bids are non-refundable.

### Comparison with Related Systems

| Aspect | Sponsored Posts | Post Awards | Livestream Awards | Realm Boost |
|--------|----------------|-------------|--------------------|-------------|
| Currency | golds | points | points | golds / points |
| Purpose | Ad placement | Tip the creator | Tip the streamer | Realm visibility level |
| Settlement | Hourly auction | Daily batch (80% payout) | On stream end (90% payout) | Immediate aggregation |
| Recipient | Platform (system) | Publisher wallet | Streamer wallet | Realm boost points |

## Flow

### 1. Bidding

A user calls `POST /api/posts/{id}/sponsor` with an amount of golds (≥5). The Sphere service:

1. Validates the post is public, not deleted, not shadowbanned.
2. Creates a wallet order with `currency: "golds"`, `productIdentifier: "ads.sponsor"`, and metadata containing the account ID, post ID, and amount.
3. Returns the order ID to the client.

### 2. Payment

The client pays the order via the Wallet service (`POST /api/orders/{id}/pay`). This debits the bidder's golds pocket. The Wallet service publishes a `PaymentOrderEvent` on the `payment_orders` NATS subject.

### 3. Bid Confirmation

Sphere's event listener (in `Startup/ServiceCollectionExtensions.cs`) handles the `"ads.sponsor"` product identifier:

- Deserializes the order metadata.
- Calls `SponsorService.ConfirmSponsorBidAsync` which inserts an `SnPostSponsorBid` row with `ExpiresAt = now + 24h`.

### 4. Hourly Auction

The `PostSponsorAuctionJob` (Quartz, cron `0 0 * * * ?`) runs at the top of each UTC hour:

1. Computes the current hour window `[hourStart, hourStart + 1h)`.
2. Skips if a placement already exists for this hour (idempotent).
3. Queries all active bids (where `ExpiresAt > now`), grouped by post, summed by amount, ordered descending.
4. Skips posts that are deleted, non-public, or shadowbanned.
5. Selects the top post and inserts an `SnPostSponsorPlacement` record for the hour.

### 5. Display

When featured posts or the timeline are requested:

- `PostService.ListFeaturedPostsAsync` loads the current hour's sponsored post (if any), sets `Sponsored = true`, and prepends it at index 0.
- `TimelineService.ListEvents` and `ListEventsForAnyone` do the same — the sponsored post is inserted at index 0 after `RankPosts`, bypassing the diversification/ranking pipeline entirely.

The sponsored post serializes with `"sponsored": true` alongside its normal fields.

## Data Model

### `SnPostSponsorBid`

Individual bid records. One row per payment.

| Field | Type | Description |
|-------|------|-------------|
| `Id` | `Guid` | Primary key |
| `PostId` | `Guid` | FK → `posts.id` (cascade) |
| `AccountId` | `Guid` | The bidder's account ID |
| `Amount` | `decimal` | Golds spent on this bid (≥5) |
| `ExpiresAt` | `Instant` | When this bid stops counting (CreatedAt + 24h) |
| `CreatedAt` | `Instant` | From `ModelBase` |
| `UpdatedAt` | `Instant` | From `ModelBase` |
| `DeletedAt` | `Instant?` | Soft delete from `ModelBase` |

Indexes: `post_id`, `expires_at`.

A post's **total active sponsorship** = `SUM(Amount) WHERE PostId = ? AND ExpiresAt > now`.

### `SnPostSponsorPlacement`

Hourly winner log. One row per hour (unique on `ValidFrom`).

| Field | Type | Description |
|-------|------|-------------|
| `Id` | `Guid` | Primary key |
| `PostId` | `Guid` | FK → `posts.id` (cascade) |
| `TotalAmount` | `decimal` | Sum of all active bids at calculation time |
| `ValidFrom` | `Instant` | Start of the hour window |
| `ValidUntil` | `Instant` | Start of the next hour |
| `CreatedAt` | `Instant` | From `ModelBase` |
| `UpdatedAt` | `Instant` | From `ModelBase` |
| `DeletedAt` | `Instant?` | Soft delete from `ModelBase` |

Index: `valid_from` (unique).

### `SnPost.Sponsored`

```csharp
[NotMapped]
public bool Sponsored { get; set; }
```

A per-response flag (not persisted). Set to `true` only on the current hour's winning post when loaded via `SponsorService.GetCurrentSponsoredPostAsync`. Serializes as `"sponsored": true` in JSON responses. Follows the same `[NotMapped]` pattern as `DebugRank`, `IsBookmarked`, and `IsTruncated`.

## API Endpoints

### Sponsor a Post

```http
POST /api/posts/{id}/sponsor
Authorization: Bearer {token}

{
  "amount": 10
}
```

Creates a golds payment order for the sponsorship bid. The client must then pay the order via the Wallet service.

**Validation:**

- Amount must be ≥ 5 golds.
- Post must exist, be public, not deleted, not shadowbanned.
- Self-sponsorship is allowed (users can sponsor their own posts).

**Response:**

```json
{
  "order_id": "uuid",
  "amount": 10
}
```

### Get Current Sponsored Post

```http
GET /api/posts/sponsor/current
```

Returns the current hour's sponsored post, or `{ "sponsored": false }` if no placement exists for the current hour.

**Response (with sponsor):**

```json
{
  "sponsored": true,
  "post": { "id": "uuid", "sponsored": true, "title": "...", "..." : "..." }
}
```

**Response (no sponsor):**

```json
{
  "sponsored": false
}
```

### Get Sponsor Leaderboard

```http
GET /api/posts/sponsor/leaderboard?take=20
```

Returns posts ranked by total active sponsorship (descending).

**Response:**

```json
[
  {
    "post_id": "uuid",
    "total_amount": 150.0,
    "bid_count": 12
  }
]
```

### Get Post Sponsorship Total

```http
GET /api/posts/{id}/sponsor
```

Returns the total active sponsorship amount for a specific post. Public endpoint.

**Response:**

```json
{
  "total_amount": 45.0
}
```

### Get Bid History

```http
GET /api/posts/{id}/sponsor/history
Authorization: Bearer {token}
```

Returns the bid history for a post. **Privacy-restricted**: only the bidder themselves or the post's author can see bid records. A non-author requester only sees their own bids on that post.

**Response:**

```json
[
  {
    "id": "uuid",
    "post_id": "uuid",
    "account_id": "uuid",
    "amount": 10.0,
    "expires_at": "2026-06-27T12:00:00Z",
    "created_at": "2026-06-26T12:00:00Z"
  }
]
```

## Auction Algorithm

The `PostSponsorAuctionJob` runs hourly via Quartz (cron `0 0 * * * ?`):

```
1. now = current UTC instant
2. hourStart = floor(now to start of hour)
3. hourEnd = hourStart + 1 hour
4. IF placement EXISTS WHERE valid_from == hourStart: RETURN  (idempotent)
5. candidates = SELECT post_id, SUM(amount) as total
                FROM post_sponsor_bids
                WHERE expires_at > now AND deleted_at IS NULL
                GROUP BY post_id
                ORDER BY total DESC
6. FOR EACH candidate (top to bottom):
     IF post is deleted OR not public OR shadowbanned: CONTINUE
     ELSE: winner = candidate; BREAK
7. IF winner EXISTS:
     INSERT post_sponsor_placement (post_id, total_amount, valid_from=hourStart, valid_until=hourEnd)
```

### Idempotency

The unique index on `post_sponsor_placements.valid_from` guarantees that re-running the job within the same hour does not create duplicate placements. If the job fails mid-run, the next run will retry safely.

### Tie-breaking

If two posts have the same total bid amount, the database's `ORDER BY ... DESC` returns them in an undefined order. The first valid (non-deleted, public, non-shadowbanned) post wins. This is intentional — exact ties are rare and the platform benefits from either choice.

## Privacy

Individual bid records (`SnPostSponsorBid`) are **private**:

- **The bidder** can see their own bids on any post.
- **The post's author** (the publisher's `AccountId`) can see all bids on their post.
- **Anyone else** sees nothing — `GET /api/posts/{id}/sponsor/history` returns an empty list for unauthorized requesters.

Aggregate data is **public**:

- `GET /api/posts/sponsor/current` — the current winner is visible to everyone.
- `GET /api/posts/sponsor/leaderboard` — total amounts per post are visible to everyone.
- `GET /api/posts/{id}/sponsor` — the total active sponsorship for a post is visible to everyone.

## Display Integration

### Featured Posts (`GET /api/posts/featured`)

`PostService.ListFeaturedPostsAsync` loads the top-5 featured posts as normal. After loading, it calls `SponsorService.GetCurrentSponsoredPostAsync`. If a sponsored post exists for the current hour:

- The post is loaded with full includes (publisher, categories, etc.).
- `Sponsored` is set to `true`.
- The post is prepended at index 0.

The result is a 6-item list (1 sponsored + 5 featured), with the sponsored post always first.

### Timeline (`GET /api/timeline`)

Both `TimelineService.ListEvents` (authenticated) and `ListEventsForAnyone` (unauthenticated) inject the sponsored post:

- After `RankPosts` completes (which applies all ranking, diversification, and personalization).
- Before converting posts to timeline events.
- The sponsored post is inserted at `posts[0]`, becoming `items[0].data` in the response.

This ensures the sponsored post is **always the first item** in the timeline, regardless of its organic rank. It bypasses:

- `CalculateBaseRank` (performance scoring)
- `DiversifyRankedPosts` (publisher-repeat penalty)
- Personalization bonuses
- Subscription boosts

## Payment Flow

The sponsorship bid follows the standard order-payment-event pattern used by post awards and realm boosts:

```
Client                          Sphere                      Wallet                     NATS
  |                               |                           |                          |
  |--POST /posts/{id}/sponsor---->|                           |                          |
  |                               |--CreateOrder(golds)------>|                          |
  |                               |<--order_id----------------|                          |
  |<--{order_id}------------------|                           |                          |
  |                               |                           |                          |
  |--POST /orders/{id}/pay------->|                           |                          |
  |                               |                           |--debit golds pocket----->|
  |                               |                           |--mark order Paid-------->|
  |                               |                           |--publish PaymentOrderEvent-->|
  |                               |                           |                          |
  |                               |<--PaymentOrderEvent (ads.sponsor)---------------------|
  |                               |--ConfirmSponsorBidAsync-->|                          |
  |                               |   (insert bid row)        |                          |
```

The order is created with `payeeWalletId = null`, meaning the golds are consumed by the system (no wallet receives them — the platform retains 100%). This matches the post-award order pattern where the awarder's points are burned to system.

## Scheduling

```text
Job:     PostSponsorAuctionJob
File:    DysonNetwork.Sphere/Post/PostSponsorAuctionJob.cs
Cron:    0 0 * * * ?   (top of every hour, UTC)
Config:  DysonNetwork.Sphere/Startup/ScheduledJobsConfiguration.cs
```

The auction job is separate from the daily `PublisherSettlementJob` (which handles experience/social-credit/rating rewards at midnight UTC). They operate on entirely different data.

## Database

### Migration

```text
DysonNetwork.Sphere/Migrations/20260626220000_AddPostSponsor.cs
```

### Tables

**`post_sponsor_bids`**

| Column | Type | Nullable | Notes |
|--------|------|----------|-------|
| `id` | `uuid` | no | PK |
| `post_id` | `uuid` | no | FK → `posts.id` (cascade) |
| `account_id` | `uuid` | no | The bidder |
| `amount` | `numeric` | no | Golds (≥5) |
| `expires_at` | `timestamptz` | no | CreatedAt + 24h |
| `created_at` | `timestamptz` | no | From `ModelBase` |
| `updated_at` | `timestamptz` | no | From `ModelBase` |
| `deleted_at` | `timestamptz` | yes | Soft delete |

Indexes: `ix_post_sponsor_bids_post_id`, `ix_post_sponsor_bids_expires_at`.

**`post_sponsor_placements`**

| Column | Type | Nullable | Notes |
|--------|------|----------|-------|
| `id` | `uuid` | no | PK |
| `post_id` | `uuid` | no | FK → `posts.id` (cascade) |
| `total_amount` | `numeric` | no | Sum of active bids at auction time |
| `valid_from` | `timestamptz` | no | Start of the hour |
| `valid_until` | `timestamptz` | no | Start of next hour |
| `created_at` | `timestamptz` | no | From `ModelBase` |
| `updated_at` | `timestamptz` | no | From `ModelBase` |
| `deleted_at` | `timestamptz` | yes | Soft delete |

Indexes: `ix_post_sponsor_placements_post_id`, `ix_post_sponsor_placements_valid_from` (unique).

## Localization

| Key | en | zh-hans |
|-----|-----|---------|
| `posts.sponsor.remarks` | `Sponsor post {title}` | `赞助帖子 {title}` |

## File Reference

| File | Purpose |
|------|---------|
| `DysonNetwork.Shared/Models/Post.cs` | `SnPostSponsorBid`, `SnPostSponsorPlacement` models; `Sponsored` field on `SnPost` |
| `DysonNetwork.Sphere/Post/SponsorService.cs` | Core service: bidding, auction, winner lookup, leaderboard, history |
| `DysonNetwork.Sphere/Post/PostSponsorAuctionJob.cs` | Quartz job (hourly) |
| `DysonNetwork.Sphere/Post/PostActionController.cs` | `POST /api/posts/{id}/sponsor` endpoint |
| `DysonNetwork.Sphere/Post/PostController.cs` | `GET /sponsor/current`, `/sponsor/leaderboard`, `/{id}/sponsor`, `/{id}/sponsor/history` |
| `DysonNetwork.Sphere/Post/PostService.cs` | `ListFeaturedPostsAsync` — sponsored post injection |
| `DysonNetwork.Sphere/Timeline/TimelineService.cs` | `ListEvents` / `ListEventsForAnyone` — sponsored post injection |
| `DysonNetwork.Sphere/Startup/PaymentOrderSponsorEvent.cs` | Event meta DTO for `ads.sponsor` |
| `DysonNetwork.Sphere/Startup/ServiceCollectionExtensions.cs` | `ads.sponsor` event handler; `SponsorService` DI registration |
| `DysonNetwork.Sphere/Startup/ScheduledJobsConfiguration.cs` | Hourly auction job registration |
| `DysonNetwork.Sphere/Resources/Locales/en.json` | `posts.sponsor.remarks` string |
| `DysonNetwork.Sphere/Resources/Locales/zh-hans.json` | `posts.sponsor.remarks` string (Chinese) |
| `DysonNetwork.Shared/Registry/RemotePaymentService.cs` | gRPC client used to create golds orders |
