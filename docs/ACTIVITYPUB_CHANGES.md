# ActivityPub/Fediverse Implementation Changes

This document summarizes all changes made to the ActivityPub/Fediverse implementation during recent development sessions.

---

## Table of Contents

1. [Post Display & Timeline](#1-post-display--timeline)
2. [Actor Models & JSON Serialization](#2-actor-models--json-serialization)
3. [Actor Search & Discovery](#3-actor-search--discovery)
4. [Post Listing & Remote Outbox](#4-post-listing--remote-outbox)
5. [Fediverse Moderation](#5-fediverse-moderation)
6. [Race Condition Fixes](#6-race-condition-fixes)
7. [Garbage Collection](#7-garbage-collection)
8. [Scheduled Jobs](#8-scheduled-jobs)
9. [Redis Caching](#9-redis-caching)
10. [Fediverse Availability](#10-fediverse-availability)
11. [Stats Fetching Improvements](#11-stats-fetching-improvements)
12. [Remote Post Attachments](#12-remote-post-attachments)

---

## 1. Post Display & Timeline

### Boost Display in Timelines

Posts boosted by followed actors now appear in the timeline via `/api/posts` endpoint.

**Implementation:** Added `GetBoostedPostIdsForTimelineAsync` to `TimelineService` that:
- Gets local actors linked to publishers (actor.PublisherId != null)
- Gets who those local actors follow
- Fetches boosts from those followed actors
- Merges with regular timeline posts

### Boost Display in Actor Posts

The `/api/fediverse/actors/{id}/posts` endpoint now includes boosts.

**Changes:**
- Returns `PostResponse` with optional `BoostInfo` field
- Boosts merged with original posts, sorted by original post's `publishedAt`
- For boosted posts: `actorId` = booster, `boostInfo.originalActor` = original author
- Includes both local DB posts and remote outbox posts

---

## 2. Actor Models & JSON Serialization

### SnFediverseActor

Added computed properties and explicit JSON property names:

```csharp
// Computed properties (not stored in DB)
[NotMapped] [JsonPropertyName("full_handle")] public string FullHandle
[NotMapped] [JsonPropertyName("web_url")] public string WebUrl
[NotMapped] [JsonPropertyName("followers_count")] public int FollowersCount { get; set; }
[NotMapped] [JsonPropertyName("following_count")] public int FollowingCount { get; set; }
[NotMapped] [JsonPropertyName("post_count")] public int PostCount { get; set; }
[NotMapped] [JsonPropertyName("total_post_count")] public int? TotalPostCount { get; set; }
[NotMapped] [JsonPropertyName("recent_posts")] public List<SnPost>? RecentPosts
```

### SnFediverseInstance

Added explicit `[JsonPropertyName]` attributes for all fields:
- `domain`, `name`, `description`, `software`, `version`
- `icon_url`, `thumbnail_url`, `contact_email`, `contact_account_username`
- `active_users`, `is_blocked`, `is_silenced`, etc.

### Removed DTO Classes

- Removed `FediverseActorResponse` - now returns `SnFediverseActor` directly
- Removed `FediverseInstanceResponse` - now returns `SnFediverseInstance` directly
- Kept `FediverseRelationshipResponse` (needed for relationship endpoint)

---

## 3. Actor Search & Discovery

### GetActorFromWebfingerAsync

New method that fetches actor data without saving to DB (for search/lookup):

```csharp
private async Task<SnFediverseActor?> GetActorFromWebfingerAsync(
    string actorUri, string username, string domain, string? webfingerAvatarUrl)
```

**Behavior:**
- Checks DB first for existing actor (avoids duplicates)
- If actor exists but missing bio, refreshes from remote
- Creates new actor and adds to DB before calling `FetchActorDataAsync`

### Extract Counts from ActivityPub Response

`FetchActorDataAsync` now extracts:
- `followersCount` → `actor.FollowersCount`
- `followingCount` → `actor.FollowingCount`
- `statusesCount` → `actor.TotalPostCount`

### Post Count in Search

Search endpoint now queries local DB for post counts by both `ActorId` and `Actor.Uri`.

---

## 4. Post Listing & Remote Outbox

### `/api/fediverse/actors/{id}/posts` Endpoint

Now fetches from both local DB and remote outbox:

**Implementation:**
1. Query local posts (`Post.ActorId == id`)
2. Query local boosts (`Boost.ActorId == id`)
3. Fetch remote outbox from `actor.OutboxUri`
4. Deduplicate by `FediverseUri`
5. Sort by `publishedAt`
6. Return merged results

**PostResponse Fields:**
| Field | Description |
|-------|-------------|
| `id` | Valid GUID for local posts, `Guid.Empty` for remote |
| `fediverse_uri` | Remote post URI (for deduplication) |
| `is_cached` | `true` if stored locally, `false` if from remote |
| `boostInfo` | Populated for boosted posts, null for original |

**Remote Post Parsing:**
- Parses ActivityPub `Create` and `Announce` activities
- Extracts content, title, author info
- Creates `SnPost`-compatible response

---

## 5. Fediverse Moderation

### New Model: `SnFediverseModerationRule`

```csharp
public class SnFediverseModerationRule : ModelBase
{
    public string Name { get; set; }
    public FediverseModerationRuleType Type { get; set; } // DomainBlock, DomainAllow, KeywordBlock, KeywordAllow, ReportThreshold
    public FediverseModerationAction Action { get; set; } // Block, Allow, Silence, Suspend, Flag, Derank
    public string? Domain { get; set; }
    public string? KeywordPattern { get; set; }
    public bool IsRegex { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsSystemRule { get; set; }
    public int? ReportThreshold { get; set; }
    public int Priority { get; set; }
    public Instant? ExpiresAt { get; set; }
}
```

### Configuration (appsettings.json)

```json
"FediverseModeration": {
  "Rules": [
    {
      "Name": "Block known spam instances",
      "Type": "DomainBlock",
      "Action": "Block",
      "Domain": "spam-instance.example",
      "IsEnabled": true,
      "Priority": 10
    }
  ]
}
```

### FediverseModerationService

```csharp
// Check domain against rules
Task<FediverseModerationResult> CheckDomainAsync(string domain)

// Check actor and content against rules
Task<FediverseActorResult> CheckActorAsync(string? actorUri, string? content, string? actorDomain)

// Check instance against rules
Task<FediverseModerationResult> CheckInstanceAsync(string domain)
```

### Admin API

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/fediverse/moderation/rules` | GET | List all rules |
| `/api/fediverse/moderation/rules/{id}` | GET | Get rule by ID |
| `/api/fediverse/moderation/rules` | POST | Create rule |
| `/api/fediverse/moderation/rules/{id}` | PUT | Update rule |
| `/api/fediverse/moderation/rules/{id}` | DELETE | Delete rule |
| `/api/fediverse/moderation/rules/{id}/toggle` | POST | Enable/disable |
| `/api/fediverse/moderation/check-domain` | POST | Check domain |
| `/api/fediverse/moderation/check-actor` | POST | Check actor/content |

### Integration with Activity Handler

`ActivityPubActivityHandler` now checks moderation rules before processing:
- Blocks activities from blocked domains
- Silences activities from silenced domains
- Logs when rules are matched

---

## 6. Race Condition Fixes

Multiple locations where actors are created could fail with duplicate key errors due to concurrent requests.

### Files Fixed

| File | Method | Fix |
|------|--------|-----|
| `ActivityPubDiscoveryService.cs` | `GetActorFromWebfingerAsync` | Catch `PostgresException` SQL state 23505, fetch existing actor |
| `ActivityPubSignatureService.cs` | `GetOrFetchPublicKeyAsync` | Catch duplicate key, fetch existing actor |
| `ActivityPubActivityHandler.cs` | `GetOrCreateActorAsync` | Catch duplicate key, fetch existing actor |

### Pattern Used

```csharp
try
{
    db.FediverseActors.Add(actor);
    await db.SaveChangesAsync();
    await discoveryService.FetchActorDataAsync(actor);
    return actor;
}
catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx && pgEx.SqlState == "23505")
{
    // Race condition - another request created the actor
    var existing = await db.FediverseActors.FirstOrDefaultAsync(a => a.Uri == actorUri);
    if (existing != null && string.IsNullOrEmpty(existing.PublicKey))
        await discoveryService.FetchActorDataAsync(existing);
    return existing ?? actor;
}
```

---

## 7. Garbage Collection

### Unused Actor Cleanup

**File:** `DysonNetwork.Sphere/ActivityPub/FediverseActorCleanupJob.cs`

Runs daily at 4 AM. Deletes actors with NO links:
- No posts
- No boosts
- No reactions
- No outgoing follows
- No incoming followers
- Not a local actor (`PublisherId == null`)

### Incomplete Actor Refresh

Before deleting, the job attempts to refresh incomplete actors (missing bio/displayname):

```csharp
// First, refetch incomplete actors
var (refetchedCount, stillIncompleteCount) = await RefetchIncompleteActorsAsync();

// Then delete unused actors
var deletedActorsCount = await DeleteUnusedActorsAsync();

// Finally, clean up orphaned instances
var deletedInstancesCount = await DeleteOrphanedInstancesAsync();
```

### Orphaned Instance Cleanup

After deleting actors, cleans up instances with zero actors.

---

## 8. Scheduled Jobs

### New Job: `FediverseActorCleanupJob`

**Schedule:** Daily at 4 AM (`0 0 4 * * ?`)
**Registered in:** `ScheduledJobsConfiguration.cs`

---

## 9. Redis Caching

### FediverseCachingService

Caches frequently accessed actor data:

| Cache Key | TTL |
|-----------|-----|
| Actor by handle | 1 hour |
| Actor by URI | 1 hour |
| Actor by ID | 1 hour |
| Instance by domain | 6 hours |
| Search results | 5 minutes |
| Relationships | 2 minutes |

### CachedActor

Includes full actor data plus:
- `FollowersCount`, `FollowingCount`
- `PostCount`, `TotalPostCount`
- `CachedInstance` for full instance data

---

## Migration

Run after updating code:

```bash
dotnet ef migrations add AddFediverseModerationRules
dotnet ef database update
```

---

## API Response Examples

### Actor Response (with stats)

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "username": "Gargr",
  "display_name": "Gargr",
  "bio": "Software developer",
  "avatar_url": "https://mastodon.social/avatars/Gargr.png",
  "full_handle": "Gargr@mastodon.social",
  "web_url": "https://mastodon.social/@Gargr",
  "followers_count": 150,
  "following_count": 89,
  "post_count": 5000,
  "total_post_count": 5000,
  "instance": {
    "domain": "mastodon.social",
    "name": "Mastodon",
    "software": "mastodon",
    "version": "4.2.0"
  }
}
```

### Post Response (with boost info)

```json
{
  "id": "00000000-0000-0000-0000-000000000000",
  "content": "Post content...",
  "published_at": "2026-03-27T10:00:00Z",
  "is_cached": false,
  "boost_info": {
    "boost_id": "boost-uuid",
    "boosted_at": "2026-03-27T09:30:00Z",
    "activity_pub_uri": "https://mastodon.social/users/booster/statuses/123",
    "original_post": { ... },
    "original_actor": { ... }
  }
}
```

---

## 10. Fediverse Availability

### Endpoint

```
GET /api/fediverse/actors/availability
```

**Authentication:** Required (regular user)

**Description:** Checks if the authenticated user has publishers with Fediverse features enabled. Used by the UI to determine if Fediverse options should be shown.

**Response:**

```json
{
  "available": true,
  "publishers": [
    {
      "id": "publisher-uuid",
      "username": "myblog",
      "fediverse_enabled": true,
      "fediverse_domain": "fediverse.mydomain.com"
    }
  ]
}
```

**Behavior:**
- Only returns publishers that belong to the authenticated user
- Filters to publishers with `FediverseEnabled == true`
- Returns empty `publishers` array if user has no Fediverse-enabled publishers

---

## 11. Stats Fetching Improvements

### FetchActorStatsAsync

New method in `ActivityPubDiscoveryService` that fetches accurate actor stats from remote collection endpoints:

```csharp
public async Task FetchActorStatsAsync(SnFediverseActor actor)
```

**Implementation:**
- Fetches `/followers` endpoint and gets `totalItems` for `FollowersCount`
- Fetches `/following` endpoint and gets `totalItems` for `FollowingCount`
- Fetches `/outbox` endpoint and gets `totalItems` for `PostCount`
- Updates `actor.TotalPostCount` if not already set

**When Stats Are Fetched:**
- When creating a new actor for the first time
- When the availability endpoint is called for remote actors (not local)
- `TotalPostCount` is cached from the actor object's `statusesCount` field

**Local vs Remote Actors:**
- Local actors (with `PublisherId`) use DB counts - no remote fetching
- Remote actors fetch stats from their instance's collection endpoints

---

## 12. Remote Post Attachments

### Attachment Parsing

Remote posts from outbox now include media attachments.

**Implementation:**

```csharp
// Parse ActivityPub attachment array
var attachments = new List<SnCloudFileReferenceObject>();
if (postObj["attachment"] is JsonElement attachArray && attachArray.ValueKind == JsonValueKind.Array)
{
    foreach (var attach in attachArray.EnumerateArray())
    {
        attachments.Add(new SnCloudFileReferenceObject
        {
            Name = attach.GetProperty("name").GetString() ?? "",
            Url = attach.GetProperty("url").GetString() ?? "",
            MimeType = attach.TryGetProperty("mediaType", out var mt) ? mt.GetString() : null,
            Width = attach.TryGetProperty("width", out var w) ? w.GetInt32() : null,
            Height = attach.TryGetProperty("height", out var h) ? h.GetInt32() : null,
            Blurhash = attach.TryGetProperty("blurhash", out var bh) ? bh.GetString() : null
        });
    }
}
```

**SnCloudFileReferenceObject Fields:**
| Field | Description |
|-------|-------------|
| `name` | Human-readable name for the attachment |
| `url` | Direct URL to the media file |
| `mime_type` | Media type (e.g., `image/jpeg`, `video/mp4`) |
| `width` | Width in pixels (images/videos) |
| `height` | Height in pixels (images/videos) |
| `blurhash` | BlurHash placeholder for images |

**ActivityPub Attachment Format:**
```json
{
  "attachment": [
    {
      "type": "Document",
      "mediaType": "image/jpeg",
      "url": "https://mastodon.social/media/example.jpg",
      "name": "Example image",
      "width": 1200,
      "height": 800,
      "blurhash": "LEHV6nWB2yk8pyo0adR*.7kCMdnj"
    }
  ]
}
```
