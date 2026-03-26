# Timeline Boost Display

## Overview

Posts boosted by Fediverse actors that you follow now appear in your timeline. This document explains how the timeline boost display feature works.

## How It Works

### Timeline Generation Flow

1. **Build Base Post Query** - Fetches posts from:
   - Publishers you follow
   - Public realms you belong to

2. **Fetch Boosted Post IDs** - Queries `SnBoost` records to find posts boosted by:
   - Fediverse actors linked to your local publishers
   - Actors that your local publishers follow (via `SnFediverseRelationship`)

3. **Merge and Deduplicate** - Adds boosted posts that aren't already in results

4. **Rank and Return** - All posts go through the ranking algorithm together

### Key Components

#### `GetBoostedPostIdsForTimelineAsync`

```csharp
private async Task<List<Guid>> GetBoostedPostIdsForTimelineAsync(
    Guid accountId,
    List<SnPublisher> userPublishers,
    Instant? cursor
)
```

**Logic:**
1. Get FediverseActor IDs linked to user's publishers
2. Find all Fediverse relationships where `ActorId` is in those local actors
3. Query `SnBoost` records for posts boosted by followed actors
4. Return distinct PostIds

**Database Query Pattern:**
```
local_actor_ids = SELECT Id FROM FediverseActors 
                   WHERE PublisherId IN (user_publisher_ids)

followed_actor_ids = SELECT TargetActorId FROM FediverseRelationships 
                     WHERE ActorId IN (local_actor_ids) 
                     AND State = Accepted

boosted_post_ids = SELECT DISTINCT PostId FROM Boosts 
                   WHERE ActorId IN (followed_actor_ids)
```

#### Modified `ListEvents` Flow

```
1. Get user's publishers and Fediverse actors
2. Get boosted post IDs from followed actors
3. Fetch regular timeline posts
4. Filter out posts already in results
5. Fetch boosted posts not already included
6. Merge all posts
7. Rank all posts together
8. Return timeline
```

## Data Models

### SnBoost

```csharp
public class SnBoost : ModelBase
{
    public Guid Id { get; set; }
    public Guid PostId { get; set; }
    public SnPost Post { get; set; }
    public Guid ActorId { get; set; }           // Who boosted
    public SnFediverseActor Actor { get; set; } // The actor who boosted
    public string? ActivityPubUri { get; set; }
    public string? WebUrl { get; set; }
    public string? Content { get; set; }
    public Instant BoostedAt { get; set; }
}
```

### SnFediverseRelationship

```csharp
public class SnFediverseRelationship : ModelBase
{
    public Guid Id { get; set; }
    public Guid ActorId { get; set; }           // The follower
    public SnFediverseActor Actor { get; set; }
    public Guid TargetActorId { get; set; }     // Who is being followed
    public SnFediverseActor TargetActor { get; set; }
    public RelationshipState State { get; set; }
    public bool IsMuting { get; set; }
    public bool IsBlocking { get; set; }
    public Instant? FollowedAt { get; set; }
    public Instant? FollowedBackAt { get; set; }
}

public enum RelationshipState
{
    Pending,
    Accepted,
    Rejected
}
```

### SnFediverseActor (relevant fields)

```csharp
public class SnFediverseActor : ModelBase
{
    public Guid Id { get; set; }
    public Guid? PublisherId { get; set; }  // Links to local publisher
    
    // Navigation
    public List<SnFediverseRelationship> FollowingRelationships { get; set; }
    public List<SnFediverseRelationship> FollowerRelationships { get; set; }
}
```

## Example Scenario

**Setup:**
- User Alice has Publisher P1 (linked to FediverseActor F1)
- F1 follows FediverseActor F2
- F2 boosted Post X (boosted_at = 2026-03-27)

**Result:**
When Alice loads her timeline, Post X will appear (unless P1 also authored Post X directly, in which case it's deduplicated).

## Caching

Boosted posts are NOT cached separately - they go through the normal timeline ranking and caching. The `GetBoostedPostIdsForTimelineAsync` query runs on every timeline request.

## Filtering

- `showFediverse` parameter still filters out posts with `FediverseUri == null`
- Boosted posts that are replies are excluded (same as regular timeline)
- Cursor-based pagination works across boosted and regular posts

## Performance Considerations

- The boost query uses indexed columns: `ActorId`, `BoostedAt`
- Deduplication happens in-memory after fetching
- Maximum boosted posts added is capped by timeline ranking (only top posts survive ranking)

## Related Files

- `DysonNetwork.Sphere/Timeline/TimelineService.cs` - Contains `GetBoostedPostIdsForTimelineAsync` and modified `ListEvents`
- `DysonNetwork.Shared/Models/Boost.cs` - `SnBoost` model
- `DysonNetwork.Shared/Models/FediverseActor.cs` - `SnFediverseActor` model
- `DysonNetwork.Shared/Models/FediverseRelationship.cs` - `SnFediverseRelationship` model
