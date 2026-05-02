# Publisher Rating

This document describes the publisher rating system in `DysonNetwork.Sphere`.

## Summary

Publisher rating is a reputation score assigned to publishers, modeled after the account social credit system. It is record-based: individual rating change records (deltas) aggregate into a total score. Records are permanent and do not decay.

The rating affects:

- **Timeline ranking** — higher-rated publishers receive a visibility boost; lower-rated publishers are penalized
- **Publisher profile** — the cached `Rating` field and computed `RatingLevel` are exposed via the publisher proto and public API

## Model

### `SnPublisherRatingRecord`

Each rating change is stored as an individual record:

| Field | Type | Description |
|-------|------|-------------|
| `Id` | `Guid` | Primary key |
| `ReasonType` | `string` | Category (e.g. `"publishers.rewards"`, `"publishers.resettle"`, `"punishments"`) |
| `Reason` | `string` | Human-readable explanation |
| `Delta` | `double` | Rating change (positive = gain, negative = penalty) |
| `PublisherId` | `Guid` | FK to `SnPublisher` |

Records inherit `ModelBase` (`CreatedAt`, `UpdatedAt`, `DeletedAt`).

### Rating Calculation

The total rating is simply the sum of all record deltas plus the base score:

\[
\text{rating} = \text{baseRating} + \sum_{r \in \text{records}} r.\text{delta}
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

This is separate from the per-member social credit distribution — it applies to the publisher entity itself.

### Admin Punishments

Admins can penalize a publisher's rating when issuing account punishments:

- `PublisherRatingReduction` — the delta to subtract
- `PublisherNames` — which publishers to penalize

The rating record is created with:

- `ReasonType = "punishments"`

### Aggressive Resettle

Admins can recalculate and apply ratings from historical post data for any date range:

- Reads historical posts, reactions (upvotes/downvotes), and awards
- Aggregates stats across the specified date range
- Creates incremental rating records (does not replace existing records)
- Default: last 30 days if no date range specified

## Caching

Rating scores are cached in Redis:

- **Key**: `publisher_rating:{publisherId}`
- **Group**: `publisher:{publisherId}`
- **TTL**: 5 minutes

Cache is invalidated when a new rating record is added.

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
- `GET /api/publishers/{name}/rating/overview` — rating overview with percentile and grade
- `GET /api/publishers/leaderboard` — paginated leaderboard sorted by rating descending

### Authenticated (members only)

- `GET /api/publishers/{name}/rating/history` — same as above, requires `Viewer` role or higher

### Admin

- `POST /api/publishers/rewards/resettle` — aggressive resettle from historical data

### Query Parameters (history)

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `take` | `int` | `20` | Page size |
| `offset` | `int` | `0` | Offset for pagination |

### Aggressive Resettle Request

```json
{
  "dateFrom": "2026-01-01T00:00:00Z",
  "dateTo": "2026-01-31T23:59:59Z",
  "publisherId": null
}
```

- `dateFrom` — start of the period to recalculate (required)
- `dateTo` — end of the period to recalculate (required)
- `publisherId` — optional, limit to a specific publisher; if null, resettles all publishers

### Aggressive Resettle Response

```json
{
  "processed": 42,
  "publishers": [
    { "publisherId": "uuid", "delta": 15 },
    { "publisherId": "uuid", "delta": -3 }
  ]
}
```

## Leaderboard

### Grade Scale

Grades are assigned based on percentile ranking (competition ranking — ties share the same rank):

| Grade | Percentile Threshold | Meaning |
|-------|---------------------|---------|
| S++ | #1 rank only | Absolute top |
| S+ | ≥ 99% | Top 1% |
| S | ≥ 95% | Top 5% |
| A++ | ≥ 90% | Top 10% |
| A+ | ≥ 80% | Top 20% |
| A | ≥ 70% | Top 30% |
| A- | ≥ 60% | Top 40% |
| B+ | ≥ 50% | Top 50% |
| B | ≥ 40% | Top 60% |
| C | ≥ 20% | Top 80% |
| D | < 20% | Bottom 20% |

### Percentile Formula

\[
\text{percentile} = \frac{\text{totalPublishers} - \text{rank} + 1}{\text{totalPublishers}} \times 100
\]

### Leaderboard Caching

The leaderboard is cached for **24 hours**. Cache is invalidated when any rating record is added.

## Database

### Table: `publisher_rating_records`

| Column | Type | Notes |
|--------|------|-------|
| `id` | `uuid` | PK |
| `reason_type` | `varchar(1024)` | |
| `reason` | `varchar(1024)` | |
| `delta` | `double precision` | |
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
| `DysonNetwork.Sphere/Publisher/PublisherLeaderboardService.cs` | Leaderboard with percentile ranking and grading |
| `DysonNetwork.Sphere/Publisher/PublisherRatingServiceGrpc.cs` | gRPC server |
| `DysonNetwork.Sphere/Publisher/PublisherService.cs` | Rating records added in `SettlePublisherRewards()`, `AggressiveResettle()` |
| `DysonNetwork.Sphere/Timeline/TimelineService.cs` | `GetPublisherRatingBonusMap()` replaces social credit bonus |
| `DysonNetwork.Sphere/Publisher/PublisherPublicController.cs` | Public rating endpoints |
| `DysonNetwork.Sphere/Publisher/PublisherController.cs` | Authenticated rating history, admin resettle endpoint |
| `DysonNetwork.Padlock/Account/AccountAdminController.cs` | Admin punishment with publisher rating reduction |
| `DysonSpec/proto/leveling.proto` | Proto definitions for `DyPublisherRatingService` |
| `DysonSpec/proto/publisher.proto` | `rating` field on `DyPublisher` |