# Advertising posts in publisher API and timeline

## Context
- There is already a sponsorship system in `DysonNetwork.Sphere/Post/PostController.cs` and `DysonNetwork.Sphere/Post/SponsorService.cs`.
- Today timeline injection is blunt: `TimelineService` always prepends the single current sponsored post when one exists.
- You want to treat sponsored posts as ads: expose publisher-facing advertising data, mix ads into timeline at a controlled frequency, weight selection by bid amount, allow repeated exposure over time, and count impressions so the API can report shown times.
- Ad frequency should be driven by request counting via cache, and reduced for paid/perk users: normal users `1/5`, perk 1 `1/10`, perk 2 `1/20`, perk 3 `0` ads.

## Approach
- Reuse the existing sponsor/bid flow instead of inventing a parallel ads system.
- Keep the existing hourly placement path for current post sponsor endpoints unless implementation proves it is dead weight; do not make timeline depend on the single current placement anymore.
- Extend sponsor data with impression tracking and enough aggregation to list advertiser-facing post stats.
- Use `DysonNetwork.Shared.Cache.ICacheService` to maintain lightweight per-viewer timeline request counters for ad-slot eligibility instead of writing request counters to the database.
- Replace the current “always insert current sponsored post at index 0” logic in `TimelineService` with a fixed-slot ad decision keyed by perk level:
  - normal users: ad eligible every 5 requests
  - perk level 1: every 10 requests
  - perk level 2: every 20 requests
  - perk level 3+: no ads
- When a request is ad-eligible, choose among eligible sponsored posts with weighted random selection based on active bid total.
- Keep the ad insertion isolated in `SponsorService` so `TimelineService` only asks whether to inject an ad and gets back the chosen post plus tracked impression side effects.
- Expose a per-publisher member-only endpoint from `PublisherController` (likely `GET /api/publishers/{name}/ads`) for listing sponsored/advertising posts and their bid/impression/cost summary, reusing existing membership checks.

## Files to modify
- `DysonNetwork.Sphere/Post/SponsorService.cs`
- `DysonNetwork.Sphere/Publisher/PublisherController.cs`
- `DysonNetwork.Sphere/Timeline/TimelineService.cs`
- `DysonNetwork.Shared/Models/Post.cs`
- `DysonNetwork.Sphere/AppDatabase.cs`
- `DysonNetwork.Sphere/Migrations/<new migration>.cs`
- maybe `DysonNetwork.Sphere/Post/PostController.cs` if existing sponsor endpoints should expose the new stats shape too

## Reuse
- Existing sponsor lifecycle and queries in `DysonNetwork.Sphere/Post/SponsorService.cs`
  - `CreateSponsorBidAsync`
  - `ConfirmSponsorBidAsync`
  - `GetCurrentSponsoredPostAsync`
  - `GetPostTotalSponsorshipAsync`
  - `GetBidHistoryAsync`
  - `GetLeaderboardAsync`
  - `RunHourlyAuctionAsync`
- Existing sponsor payment/auction wiring already present
  - `DysonNetwork.Sphere/Startup/PaymentOrderSponsorEvent.cs`
  - `DysonNetwork.Sphere/Post/PostSponsorAuctionJob.cs`
- Existing publisher membership/role guard pattern in `DysonNetwork.Sphere/Publisher/PublisherController.cs`
  - `ps.IsMemberWithRole(...)`
- Existing public publisher stats/listing patterns in `DysonNetwork.Sphere/Publisher/PublisherPublicController.cs`
  - paginated list endpoints using `X-Total`
- Existing timeline assembly point in `DysonNetwork.Sphere/Timeline/TimelineService.cs`
  - current sponsored insertion happens in both `ListEventsForAnyone` and `ListEvents`
- Existing cache abstraction for lightweight request counters
  - `DysonNetwork.Shared/Cache/ICacheService.cs`

## Steps
- [ ] Add a minimal schema for impression tracking. Likely smallest diff: add counters/timestamps to `SnPostSponsorPlacement` and aggregate them by post; only introduce a separate impression table if per-impression history is required.
- [ ] Define the minimal ad stats shape for publisher API: post id, post title/slug, active bid total, bid count, currently placed yes/no, shown count, last shown at.
- [ ] Add `SponsorService` methods for:
  - listing a publisher’s advertising posts with aggregated bid/placement/impression stats
  - resolving ad-slot eligibility from cache-backed request counters and viewer perk level
  - choosing an ad candidate for timeline injection using weighted random based on active bid total
  - recording shown/impression count only when an ad is actually inserted into the returned feed
- [ ] Update `PublisherController` with a per-publisher member-only endpoint (likely `GET /api/publishers/{name}/ads`) for advertising post listing/stats, following existing publisher membership checks and `X-Total` pagination style if the result can grow.
- [ ] Update `TimelineService` to stop always prepending `GetCurrentSponsoredPostAsync()` and instead call the new sponsor-selection method in both anonymous and authenticated timeline flows, with cache-keyed cadence per viewer and no ads for perk level 3+.
- [ ] Reuse `SnPost.Sponsored` and `SnPost.ToActivity()` so client-visible ad marking comes for free once the chosen post is flagged before timeline conversion.
- [ ] Keep expired, deleted, non-public, or shadowbanned posts out of ad selection.
- [ ] Add verification for selection frequency, weighted choice, repeated appearances, and impression counting.

## Verification
- Manual/API:
  - create multiple sponsored posts with different active bid totals
  - call timeline repeatedly and confirm ad cadence follows perk level rules: normal `1/5`, perk 1 `1/10`, perk 2 `1/20`, perk 3 `0`
  - confirm higher-bid posts appear more often over many requests
  - confirm the same ad can reappear across multiple requests
  - confirm each actual ad insertion increments shown/impression count
  - confirm publisher advertising listing endpoint returns cost/bid/impression data and respects membership permissions
- Data checks:
  - verify expired bids are excluded from totals/selection
  - verify deleted/non-public/shadowbanned posts are never selected
