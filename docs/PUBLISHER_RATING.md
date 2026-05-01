# Publisher Rating

This document describes the publisher rating system in `DysonNetwork.Sphere`.

## Summary

Publisher rating is a reputation score assigned to publishers, modeled after the account social credit system. It is record-based: individual rating change records (deltas) aggregate into a total score. Records can expire and decay over time.

The rating affects:

- **Timeline ranking** — higher-rated publishers receive a visibility boost; lower-rated publishers are penalized
- **Publisher profile** — the cached `Rating` field and computed `RatingLevel` are exposed via the publisher proto and public API

## Model

### `SnPublisherRatingRecord`

Each rating change is stored as an individual record:

| Field | Type | Description |
|-------|------|-------------|
| `Id` | `Guid` | Primary key |
| `ReasonType` | `string` | Category (e.g. `"publishers.rewards"`, `"punishments"`) |
| `Reason` | `string` | Human-readable explanation |
| `Delta` | `double` | Rating change (positive = gain, negative = penalty) |
| `Status` | `Active` / `Expired` | Current status |
| `ExpiredAt` | `Instant?` | Optional expiration time |
| `PublisherId` | `Guid` | FK to `SnPublisher` |

Records inherit `ModelBase` (`CreatedAt`, `UpdatedAt`, `DeletedAt`).

### Effective Delta (Time-Weighted Decay)

Permanent records (no `ExpiredAt`) always return their full delta. Temporary records decay linearly as they approach expiration:

\[
\text{effective}(r, t) =
\begin{cases}
0, & \text{if } r.\text{expiredAt} \le t \\
r.\text{delta}, & \text{if } r.\text{expiredAt} = \text{null} \\
r.\text{delta} \cdot \max\left(0,\; 1 - \frac{t - r.\text{createdAt}}{r.\text{expiredAt} - r.\text{createdAt}}\right), & \text{otherwise}
\end{cases}
\]

### Cached Rating on `SnPublisher`

| Field | Type | Description |
|-------|------|-------------|
| `Rating` | `double` | Cached aggregate score (default `100`) |
| `RatingLevel` | `int` (`[NotMapped]`) | Computed: `< 100 → -1`, `100–200 → 0`, `200–300 → 1`, `≥ 300 → 2` |

## How Ratings Are Calculated

### Base Score

Every publisher starts with a base rating of **100**.

### Daily Settlement (Post Engagement)

The daily `PublisherSettlementJob` calculates rating deltas from yesterday's post engagement:

\[
\text{points} = (upvotes - downvotes) + awardScore \times 0.1
\]

\[
\text{ratingDelta} = \lfloor \text{points} \rfloor
\]

If non-zero, a rating record is created for the publisher with:

- `ReasonType = "publishers.rewards"`
- `ExpiredAt = end of today + 30 days` (temporary, expires monthly)

This is separate from the per-member social credit distribution — it applies to the publisher entity itself.

### Admin Punishments

Admins can penalize a publisher's rating when issuing account punishments:

- `PublisherRatingReduction` — the delta to subtract
- `PublisherNames` — which publishers to penalize

The rating record is created with:

- `ReasonType = "punishments"`
- `ExpiredAt` defaults to 365 days if not specified

## Periodic Sync

The `PublisherRatingValidationJob` runs every **60 minutes** and:

1. Fetches all distinct publisher IDs with rating records
2. Recomputes the effective rating per publisher (time-weighted sum + base)
3. Persists the result to `publishers.rating` via `ExecuteUpdateAsync`
4. Clears the `publisher_rating:` cache group

This ensures the cached `Rating` column stays reasonably up-to-date even if cached values expire.

## Caching

Rating scores are cached in Redis:

- **Key**: `publisher_rating:{publisherId}`
- **Group**: `publisher:{publisherId}`
- **TTL**: 5 minutes

Cache is invalidated when:

- A new rating record is added
- The periodic validation job runs

## Timeline Integration

Publisher rating replaces the previous account social credit level as the ranking factor. The `TimelineService` computes a bonus per post based on the publisher's `RatingLevel`:

\[
R_{\text{publisher}}(p) =
\begin{cases}
\min(3,\; 0.05 \cdot L_{\text{rating}}(P(p))), & \text{if publisher has a rating} \\
0, & \text{otherwise}
\end{cases}
\]

Where \(L_{\text{rating}}\) is the `RatingLevel` derived from the publisher's cached `Rating`:

| Rating Range | Level | Bonus |
|-------------|-------|-------|
| < 100 | -1 | -0.05 (slight penalty) |
| 100–200 | 0 | 0 (neutral) |
| 200–300 | 1 | +0.05 |
| ≥ 300 | 2 | +0.10 (capped at 3.0) |

This bonus is added to the post's rank score in `RankPosts()`, directly affecting content visibility.

## API Endpoints

### Public

- `GET /api/publishers/{name}/rating` — returns the current rating score (double)
- `GET /api/publishers/{name}/rating/history` — paginated rating history, returns `X-Total` header

### Authenticated (members only)

- `GET /api/publishers/{name}/rating/history` — same as above, requires `Viewer` role or higher

### Query Parameters (history)

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `take` | `int` | `20` | Page size |
| `offset` | `int` | `0` | Offset for pagination |

## gRPC Service

`DyPublisherRatingService` (defined in `leveling.proto`):

- `AddRecord(DyAddPublisherRatingRecordRequest) → DyPublisherRatingRecord`
- `GetRating(DyGetPublisherRatingRequest) → DyPublisherRatingResponse`

Used by Padlock (admin punishments) to penalize publisher ratings remotely.

## Database

### Table: `publisher_rating_records`

| Column | Type | Notes |
|--------|------|-------|
| `id` | `uuid` | PK |
| `reason_type` | `varchar(1024)` | |
| `reason` | `varchar(1024)` | |
| `delta` | `double precision` | |
| `status` | `integer` | enum: 0=Active, 1=Expired |
| `expired_at` | `timestamptz` | nullable |
| `publisher_id` | `uuid` | FK → `publishers.id` (cascade) |
| `created_at` | `timestamptz` | from `ModelBase` |
| `updated_at` | `timestamptz` | from `ModelBase` |
| `deleted_at` | `timestamptz` | nullable, soft delete |

### Column added to `publishers`

| Column | Type | Default |
|--------|------|---------|
| `rating` | `double precision` | `100` |

## File Reference

| File | Purpose |
|------|---------|
| `DysonNetwork.Shared/Models/Publisher.cs` | `SnPublisherRatingRecord`, `Rating`/`RatingLevel` on `SnPublisher` |
| `DysonNetwork.Sphere/Publisher/PublisherRatingService.cs` | Core service: `AddRecord`, `GetRating`, history |
| `DysonNetwork.Sphere/Publisher/PublisherRatingServiceGrpc.cs` | gRPC server |
| `DysonNetwork.Sphere/Publisher/PublisherRatingValidationJob.cs` | 60-min periodic sync job |
| `DysonNetwork.Sphere/Publisher/PublisherService.cs` | Rating records added in `SettlePublisherRewards()` |
| `DysonNetwork.Sphere/Timeline/TimelineService.cs` | `GetPublisherRatingBonusMap()` replaces social credit bonus |
| `DysonNetwork.Sphere/Publisher/PublisherPublicController.cs` | Public rating endpoints |
| `DysonNetwork.Sphere/Publisher/PublisherController.cs` | Authenticated rating history endpoint |
| `DysonNetwork.Padlock/Account/AccountAdminController.cs` | Admin punishment with publisher rating reduction |
| `DysonSpec/proto/leveling.proto` | Proto definitions for `DyPublisherRatingService` |
| `DysonSpec/proto/publisher.proto` | `rating` field on `DyPublisher` |
