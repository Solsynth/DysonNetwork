# Sponsored Posts

This document describes the sponsored ads system in `DysonNetwork.Sphere`.

Users spend **golds** (golden points) to bid on ad placement for any public post. The system runs a **weighted hourly auction** where posts with higher total active bids have a proportionally higher chance of winning. The winning post is then eligible for injection into the timeline feed subject to a per-viewer cadence gate based on the viewer's perk level.

## Overview

The sponsored ads system is a competitive bidding platform for post visibility:

- **Currency:** golds (`WalletCurrency.GoldenPoint`)
- **Minimum bid:** 5 golds per bid
- **Bid duration:** 24 hours from confirmation
- **Auction cycle:** hourly (at the top of each UTC hour)
- **Winner selection:** weighted random by total active bid amount (proportional odds, not pick-first)
- **Placement:** fixed index slot (`posts[4]`) in `GET /api/posts/featured` and `GET /api/timeline`, gated by viewer cadence
- **Perk-aware cadence:** normal users ~1 ad per 5 timeline requests; perk 1 → 1/10; perk 2 → 1/20; perk 3+ → no ads. Counted via cache, per-viewer key
- **Serialization:** winning posts carry `"sponsored": true` in API responses
- **Analytics:** publisher members can view per-post bid totals, placements, and impression counts via `GET /api/publishers/{name}/ads`
- **Ad metrics:** per-post aggregated stats are tracked in a dedicated `post_aggregated_stats` table, separate from both bids and placements

Multiple users can bid on the same post — their bids pool together to increase that post's total weight in the auction. Users can also place multiple bids on the same post over time. Bids are non-refundable.

### Comparison with Related Systems

| Aspect | Sponsored Posts | Post Awards | Livestream Awards | Realm Boost |
|--------|----------------|-------------|--------------------|-------------|
| Currency | golds | points | points | golds / points |
| Purpose | Ad placement | Tip the creator | Tip the streamer | Realm visibility level |
| Settlement | Hourly weighted auction | Daily batch (80% payout) | On stream end (90% payout) | Immediate aggregation |
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

### 4. Hourly Auction (Weighted Draw)

The `PostSponsorAuctionJob` (Quartz, cron `0 0 * * * ?`) runs at the top of each UTC hour:

1. Computes the current hour window `[hourStart, hourStart + 1h)`.
2. Skips if a placement already exists for this hour (idempotent).
3. Queries all active bids (where `ExpiresAt > now`), grouped by post, summed by amount.
4. Filters out posts that are deleted, non-public, shadowbanned, or whose publisher is shadowbanned.
5. **Weighted selection:** draws a random number in `[0, totalWeight)` where `totalWeight = Σ bidTotal`; walks candidates accumulating bid totals and picks the first post whose cumulative sum exceeds the draw. Higher-bid posts occupy proportionally more of the draw space.
6. Inserts an `SnPostSponsorPlacement` record for the hour.
7. Creates or refreshes the post's `SnPostAggregatedStats` row with the latest `ActiveBidTotal`, `ActiveBidCount`, and `IsCurrentlyPlaced = true`.

### 5. Display

When featured posts or the timeline are requested, the **current hour's winning placement** is eligible for display, subject to a per-viewer cadence check:

- `TimelineService` maintains a lightweight per-viewer counter in the shared cache (`ICacheService`). The counter key is `timeline:ad-counter:{viewerKey}` and is protected by a per-viewer distributed lock during increment to avoid drift under concurrent requests.
  - Authenticated users: keyed by `accountId`.
  - Unauthenticated users: keyed by a fingerprint of `{remoteIp}:{userAgent}`.
- Each timeline request increments the viewer's counter. An ad slot opens only when `counter % interval == 0`, where `interval` is determined by the viewer's `DyAccount.PerkLevel`: `5` (normal), `10` (perk 1), `20` (perk 2), or no ads for perk 3+.
- When the slot opens, `SponsorService.TryGetTimelineSponsoredPostAsync` checks the current placement. If present:
  - The post is loaded with full includes.
  - `Sponsored` is set to `true`.
  - The post is inserted at `posts[4]` (clamped to the end if the feed has fewer than 5 items).
  - `SponsorService.RecordImpressionAsync` increments `ShownCount` and sets `LastShownAt` on the post's `SnPostAggregatedStats` row.

This ensures:

- A sponsored post appears roughly once per N timeline requests per viewer (not on every request).
- Higher-bid posts are more likely to win placements, but lower-bid posts still have a chance.
- The same ad can reappear across multiple requests when its placement is active and the viewer's counter aligns.
- Perk-level subscribers see fewer or no ads; perk 3+ never see ads in the timeline.

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

Hourly winner log. One row per hour (unique on `ValidFrom`). Pure record of which post won which hour — carries no impression metrics.

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

### `SnPostAggregatedStats`

Per-post aggregated advertising stats. One row per post (unique on `PostId`). Maintained on auction wins and impression events; this is the source of truth for advertiser-facing reporting.

| Field | Type | Description |
|-------|------|-------------|
| `Id` | `Guid` | Primary key |
| `PostId` | `Guid` | FK → `posts.id` (cascade), unique |
| `ActiveBidTotal` | `decimal` | Sum of currently-active bids (refreshed on each auction win) |
| `ActiveBidCount` | `int` | Count of currently-active bids (refreshed on each auction win) |
| `IsCurrentlyPlaced` | `bool` | True if this post won the most recent auction |
| `ShownCount` | `long` | Lifetime impressions (increments on each actual timeline display) |
| `LastShownAt` | `Instant?` | Most recent impression time |
| `CreatedAt` | `Instant` | From `ModelBase` |
| `UpdatedAt` | `Instant` | From `ModelBase` |
| `DeletedAt` | `Instant?` | Soft delete from `ModelBase` |

Index: `post_id` (unique).

Lifecycle: a new row is created on a post's first auction win or first impression (whichever comes first). Subsequent auction wins refresh `ActiveBidTotal`, `ActiveBidCount`, and `IsCurrentlyPlaced`. Each time the post is displayed in a timeline ad slot, `ShownCount` increments and `LastShownAt` updates.

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
- Self-sponsorship is allowed.

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

### List Publisher Advertising Posts

```http
GET /api/publishers/{name}/ads?offset=0&take=20
Authorization: Bearer {token}
```

Returns advertising stats for all sponsored posts belonging to the publisher. **Requires publisher membership** with at least `Viewer` role.

**Response headers:**

| Header | Description |
|--------|-------------|
| `X-Total` | Total count of advertising posts |

**Response body:**

```json
[
  {
    "post_id": "uuid",
    "title": "My Product Launch",
    "slug": "my-product-launch",
    "active_bid_total": 250.0,
    "bid_count": 18,
    "is_currently_placed": true,
    "shown_count": 142,
    "last_shown_at": "2026-07-04T15:32:00Z"
  }
]
```

| Field | Type | Description |
|-------|------|-------------|
| `post_id` | `uuid` | Post identifier |
| `title` | `string?` | Post title |
| `slug` | `string?` | Post slug |
| `active_bid_total` | `decimal` | Sum of all still-active bids (from `SnPostAggregatedStats.ActiveBidTotal`) |
| `bid_count` | `int` | Number of still-active bids (from `SnPostAggregatedStats.ActiveBidCount`) |
| `is_currently_placed` | `bool` | Whether this post won the current hour's auction |
| `shown_count` | `long` | Lifetime impressions across all placements (from `SnPostAggregatedStats.ShownCount`) |
| `last_shown_at` | `Instant?` | Most recent impression time |

This endpoint reads from `SnPostAggregatedStats`, so it is a cheap query with no complex subqueries across bids or placements.

### List Public Publisher Advertising Posts

```http
GET /api/ads/{name}?offset=0&take=20
```

Returns public advertising stats for the publisher's public, timeline-eligible sponsored posts, including each post's current chance to win timeline placement.

**Response headers:**

| Header | Description |
|--------|-------------|
| `X-Total` | Total count of advertising posts |

**Response body:**

```json
[
  {
    "post_id": "uuid",
    "title": "My Product Launch",
    "slug": "my-product-launch",
    "active_bid_total": 250.0,
    "bid_count": 18,
    "is_currently_placed": true,
    "shown_count": 142,
    "last_shown_at": "2026-07-04T15:32:00Z",
    "display_chance": 0.125
  }
]
```

| Field | Type | Description |
|-------|------|-------------|
| `post_id` | `uuid` | Post identifier |
| `title` | `string?` | Post title |
| `slug` | `string?` | Post slug |
| `active_bid_total` | `decimal` | Sum of all still-active bids |
| `bid_count` | `int` | Number of still-active bids |
| `is_currently_placed` | `bool` | Whether this post won the current hour's auction |
| `shown_count` | `long` | Lifetime impressions across all placements |
| `last_shown_at` | `Instant?` | Most recent impression time |
| `display_chance` | `decimal` | Current auction chance as a 0-1 ratio across all valid active sponsor bids |

This endpoint combines active bids, current placement, and aggregated impression stats so posts with live bids are visible even before they win their first placement.

## Auction Algorithm

The `PostSponsorAuctionJob` runs hourly via Quartz (cron `0 0 * * * ?`):

```
1. now = current UTC instant
2. hourStart = floor(now to start of hour)
3. hourEnd = hourStart + 1 hour
4. IF placement EXISTS WHERE valid_from == hourStart: RETURN  (idempotent)
5. candidates = SELECT post_id, SUM(amount) as total
                FROM post_sponsor_bids
                WHERE expires_at > now
                GROUP BY post_id
6. FILTER OUT posts that are deleted, non-public, shadowbanned,
   or whose publisher is shadowbanned
7. IF no candidates: RETURN
8. totalWeight = SUM(total) over all valid candidates
9. roll = randomDouble() * totalWeight
10. cumulative = 0
11. FOR EACH candidate:
      cumulative += candidate.total
      IF roll < cumulative:
        winner = candidate; BREAK
12. INSERT post_sponsor_placement (
      post_id, total_amount,
      valid_from=hourStart, valid_until=hourEnd)
13. Upsert post_aggregated_stats:
      active_bid_total = SUM(active bids),
      active_bid_count = COUNT(active bids),
      is_currently_placed = true
```

### Idempotency

The unique index on `post_sponsor_placements.validFrom` guarantees that re-running the job within the same hour does not create duplicate placements. If the job fails mid-run, the next run will retry safely.

### Weighted Selection

Unlike the previous pick-first approach, the weighted draw gives each candidate a slice of `[0, total)` proportional to its bid total. Over many auctions:

- A post with 80% of the total active bid pool wins ~80% of the hours it competes in.
- A post with 5% of the pool still wins ~5% of the hours — small bidders get proportional exposure, never zero.

This avoids the bimodal all-or-nothing outcome where only the single highest bidder ever wins.

## Timeline Cadence Algorithm

```
perkLevel = currentUser?.PerkLevel ?? 0  (unauthenticated = 0)
interval  = perkLevel >= 3 ? 0
          : perkLevel == 2 ? 20
          : perkLevel == 1 ? 10
          : 5

viewerKey = authenticated ? accountId
          : "anon:{remoteIp}:{userAgent}"

IF interval == 0: RETURN (no ads)

LOCK "timeline:ad-counter-lock:{viewerKey}" (TTL 3s, wait 1s):
    count = cache.Get("timeline:ad-counter:{viewerKey}") ?? 0
    count++
    cache.Set("timeline:ad-counter:{viewerKey}", count, TTL 24h)

shouldShowAd = (count % interval == 0)

IF shouldShowAd:
    post = GetCurrentSponsoredPostAsync()  (checks current placement + post validity)
    IF post != null:
        post.Sponsored = true
        posts.Insert(min(4, posts.Count), post)
        postAggregatedStats.ShownCount += 1
        postAggregatedStats.LastShownAt = now
```

The per-viewer lock prevents concurrent timeline requests from racing on the increment. Under lock acquisition failure, the request silently skips the ad slot to preserve availability rather than erroring.

### Cache Constraints

- Counter TTL: **24 hours** (rolling window). A viewer who goes silent for over a day gets a fresh cadence on return.
- Lock TTL: **3 seconds**, wait budget **1 second**, retry interval **50ms** — prevents stale locks under crash while converging quickly under contention.

## Display Integration

### Featured Posts (`GET /api/posts/featured`)

`PostService.ListFeaturedPostsAsync` loads the top-5 featured posts as normal. After ranking, if the viewer's cadence slot is open, it calls `SponsorService.TryGetTimelineSponsoredPostAsync`. If a post is returned:

- The post is loaded with full includes (publisher, categories, etc.).
- `Sponsored` is set to `true`.
- The post is inserted at `posts[4]`, clamped to the end of the feed if shorter than 5 items.

### Timeline (`GET /api/timeline`)

Both `TimelineService.ListEvents` (authenticated) and `ListEventsForAnyone` (unauthenticated) invoke `MaybeInsertAdAsync`:

- After `RankPosts` and diversification are complete.
- Before converting posts to timeline events.
- On cadence hit, inserts the current placement post at `posts[4]`.
- The insertion is clamped to `posts.Count` if the feed is shorter than 5 items.
- Records an impression on the post's `SnPostAggregatedStats` row.
- Perk 3+ viewers never reach the ad call — the cadence check returns early.

This ensures the sponsored post is **not guaranteed to appear** on every timeline request for a given viewer, but surfaces roughly once per `interval` requests.

## Privacy

Individual bid records (`SnPostSponsorBid`) are **private**:

- **The bidder** can see their own bids on any post.
- **The post's author** (the publisher's `AccountId`) can see all bids on their post.
- **Anyone else** sees nothing — `GET /api/posts/{id}/sponsor/history` returns an empty list for unauthorized requesters.

Aggregate data is **public**:

- `GET /api/posts/sponsor/current` — the current winner is visible to everyone.
- `GET /api/posts/sponsor/leaderboard` — total amounts per post are visible to everyone.
- `GET /api/posts/{id}/sponsor` — the total active sponsorship for a post is visible to everyone.
- `GET /api/publishers/{name}/ads` — aggregate stats for publisher members only.
- `GET /api/ads/{name}` — public aggregate ad stats plus current display chance.

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

### Migrations

| Migration | File | Purpose |
|-----------|------|---------|
| `20260626220000_AddPostSponsor.cs` | `DysonNetwork.Sphere/Migrations/20260626220000_AddPostSponsor.cs` | Initial `post_sponsor_bids` and `post_sponsor_placements` tables |
| `20260704113729_AddPostAggregatedStats.cs` | `DysonNetwork.Sphere/Migrations/20260704113729_AddPostAggregatedStats.cs` | New `post_aggregated_stats` table for per-post ad metrics |

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

**`post_aggregated_stats`**

| Column | Type | Nullable | Notes |
|--------|------|----------|-------|
| `id` | `uuid` | no | PK |
| `post_id` | `uuid` | no | FK → `posts.id` (cascade), unique |
| `active_bid_total` | `numeric` | no | Sum of currently-active bids |
| `active_bid_count` | `integer` | no | Count of currently-active bids |
| `is_currently_placed` | `boolean` | no | Whether the post won the current hour's auction |
| `shown_count` | `bigint` | no | Lifetime impressions |
| `last_shown_at` | `timestamptz` | yes | Most recent impression time |
| `created_at` | `timestamptz` | no | From `ModelBase` |
| `updated_at` | `timestamptz` | no | From `ModelBase` |
| `deleted_at` | `timestamptz` | yes | Soft delete |

Indexes: `ix_post_aggregated_stats_post_id` (unique).

## Localization

| Key | en | zh-hans |
|-----|-----|---------|
| `posts.sponsor.remarks` | `Sponsor post {title}` | `赞助帖子 {title}` |

## Perk Level Reference

| Perk Level | Ad interval (timeline requests) | Behavior |
|------------|---------------------------------|----------|
| 0 (free / unauthenticated) | every 5th request (~20%) | Maximum exposure for free users |
| 1 | every 10th request (~10%) | Reduced exposure for entry perk |
| 2 | every 20th request (~5%) | Further reduced for mid perk |
| 3+ | no ads | Fully ad-free for top-tier perk subscribers |

## File Reference

| File | Purpose |
|------|---------|
| `DysonNetwork.Shared/Models/Post.cs` | `SnPostSponsorBid`, `SnPostSponsorPlacement`, `SnPostAggregatedStats` models; `Sponsored` field on `SnPost` |
| `DysonNetwork.Sphere/Post/SponsorService.cs` | Core service: bidding, weighted auction, cadence/eligibility, winner lookup, leaderboard, history, ad stats, impression tracking |
| `DysonNetwork.Sphere/Post/PostSponsorAuctionJob.cs` | Quartz job (hourly, weighted draw) |
| `DysonNetwork.Sphere/Post/PostActionController.cs` | `POST /api/posts/{id}/sponsor` endpoint |
| `DysonNetwork.Sphere/Post/PostController.cs` | `GET /sponsor/current`, `/sponsor/leaderboard`, `/{id}/sponsor`, `/{id}/sponsor/history` |
| `DysonNetwork.Sphere/Post/PostService.cs` | `ListFeaturedPostsAsync` — sponsored post injection |
| `DysonNetwork.Sphere/Publisher/PublisherController.cs` | `GET /api/publishers/{name}/ads` endpoint |
| `DysonNetwork.Sphere/Timeline/TimelineService.cs` | `ListEvents` / `ListEventsForAnyone` — cadence-gated ad insertion |
| `DysonNetwork.Sphere/Timeline/TimelineController.cs` | Passes viewer key (accountId or anon fingerprint) into `ListEventsForAnyone` |
| `DysonNetwork.Sphere/AppDatabase.cs` | `DbSet<SnPostAggregatedStats>` and model configuration |
| `DysonNetwork.Sphere/Startup/PaymentOrderSponsorEvent.cs` | Event meta DTO for `ads.sponsor` |
| `DysonNetwork.Sphere/Startup/ServiceCollectionExtensions.cs` | `ads.sponsor` event handler; `SponsorService` DI registration |
| `DysonNetwork.Sphere/Startup/ScheduledJobsConfiguration.cs` | Hourly auction job registration |
| `DysonNetwork.Sphere/Resources/Locales/en.json` | `posts.sponsor.remarks` string |
| `DysonNetwork.Sphere/Resources/Locales/zh-hans.json` | `posts.sponsor.remarks` string (Chinese) |
| `DysonNetwork.Shared/Registry/RemotePaymentService.cs` | gRPC client used to create golds orders |
| `DysonNetwork.Shared/Registry/RemoteAccountService.cs` | gRPC client used to resolve `DyAccount.PerkLevel` |
| `DysonNetwork.Shared/Cache/ICacheService.cs` | Cache abstraction used for per-viewer request counters |
