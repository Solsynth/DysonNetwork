# Block System

## Overview

The block system enforces mutual interaction restrictions between two users. When user A blocks user B, a directional record is stored (`blocker_id=A, blocked_id=B`), but behavioral restrictions apply in **both directions** — neither user can interact with the other.

The core design principle: a block creates interaction separation, discovery separation, and notification separation without breaking public thread integrity.

## Storage Model

Blocks are stored in the `account_relationships` table with `status = -100` (`RelationshipStatus.Blocked`). Each block is a single directional record:

```
blocker_id = A, blocked_id = B, status = -100
```

If B also blocks A, a separate record exists. Behavioral checks always query **both directions**.

## Centralized Helpers

### `IsBlockedEitherDirection(userId, otherId)`

Checks if a block exists in either direction between two users. Returns `true` if A blocked B **or** B blocked A.

**Passport (direct DB access):**
`DysonNetwork.Passport/Account/RelationshipService.cs`

**Shared (gRPC wrapper):**
`DysonNetwork.Shared/Registry/RemoteAccountService.cs`

**gRPC:**
Uses `HasRelationship` RPC with `EitherDirection = true` and `Status = -100`:

```csharp
var response = await profiles.HasRelationshipAsync(new DyGetRelationshipRequest
{
    AccountId = userId.ToString(),
    RelatedId = otherId.ToString(),
    Status = (int)RelationshipStatus.Blocked,
    EitherDirection = true
});
```

### `ListAllBlockedAccountIds(accountId)`

Returns a `HashSet<Guid>` of all account IDs blocked by or blocking the given user (union of both directions). Used for feed filtering, search filtering, and notification filtering.

**Passport:**
`DysonNetwork.Passport/Account/RelationshipService.cs`

**Shared:**
`DysonNetwork.Shared/Registry/RemoteAccountService.cs`

## Caching

All block data is cached in Redis (shared across services):

| Key Pattern | Purpose | TTL |
|---|---|---|
| `accounts:blocked:{accountId}:{isRelated}` | One-direction blocked list | 1 hour |
| `accounts:blocked:all:{accountId}` | Both-directions blocked list | 1 hour |
| `accounts:blocked:either:{smallerId}:{largerId}` | Bidirectional check (symmetric) | 1 hour |

Cache is purged on block/unblock via `PurgeRelationshipCache`.

## Behavioral Rules

### Post Interactions

| Action | Check | File |
|---|---|---|
| Reply to post | Bidirectional block check | `PostActionController.cs` |
| React to post | Bidirectional block check | `PostActionController.cs` |
| Forward/repost | Bidirectional block check | `PostActionController.cs` |
| Boost post | Bidirectional block check | `PostActionController.cs` |
| Award post | Bidirectional block check | `PostActionController.cs` |

All return `400 Bad Request` with a descriptive message when blocked.

### Feed & Timeline

Posts from blocked users are filtered out at the database query level via `FilterWithVisibility`:

```csharp
if (blockedAccountIds is { Count: > 0 })
{
    result = result.Where(e =>
        e.Publisher == null
        || e.Publisher.AccountId == null
        || !blockedAccountIds.Contains(e.Publisher.AccountId.Value)
    );
}
```

Applied in:
- `TimelineService.ListEvents` (personalized feed)
- `PostController.ListPosts` (post listing/search)

### Direct Messages

| Action | Check | File |
|---|---|---|
| Create DM | Bidirectional block check | `ChatRoomController.cs` |
| Send DM message | Bidirectional block check | `ChatService.cs` |
| Invite to chat | Bidirectional block check | `ChatRoomController.cs` |

Returns `403 Forbidden` when blocked.

### Search & Discovery

| Surface | Filtering | File |
|---|---|---|
| Publisher search | Exclude blocked publishers | `PublisherPublicController.cs` |
| `@` mention autocomplete | Exclude blocked users/publishers | `AutocompletionService.cs` |

### Notifications

Notifications are suppressed between blocked users:

| Notification Type | Filtering | File |
|---|---|---|
| Publisher subscription (new post) | Filter blocked accounts | `PublisherSubscriptionService.cs` |
| Follower notification | Filter blocked accounts | `PublisherSubscriptionService.cs` |
| Post subscription (reaction/forward/edit) | Filter blocked accounts | `PostService.cs` |
| Chat message push | Filter blocked accounts | `ChatService.cs` |

### Mentions

Mentions of blocked users are **silently stripped** — the post is created normally, but no mention notification is sent. The mention text remains in the content but triggers no action.

`PostService.cs` → `SendMentionNotificationsAsync`

### Follow Requests

Follow requests are blocked if either direction is blocked:

`PublisherService.cs` → `CreateFollowRequest`

Returns `InvalidOperationException` which surfaces as a `400 Bad Request`.

## What Does NOT Happen on Block

Per design decision, blocking does **not** clean up existing data:

- Old reactions are **not** removed
- Old follows are **not** removed
- Old messages remain accessible
- Historical mentions are preserved

The block only prevents **new** interactions and **filters** from discovery surfaces.

## Profile Access

Public profiles remain accessible read-only. Interaction UI is removed by the interaction-level blocks above. Private content follows existing visibility rules (already enforced by `FilterWithVisibility`).

## Files Changed

### DysonSpec (proto)
- `proto/profile.proto` — Added `bool either_direction = 4` to `DyGetRelationshipRequest`

### Passport
- `Account/RelationshipService.cs` — `IsBlockedEitherDirection`, `ListAllBlockedAccountIds`, cache purge updates
- `Account/AccountServiceGrpc.cs` — `HasRelationship` handles `EitherDirection` flag

### Shared
- `Registry/RemoteAccountService.cs` — `IsBlockedEitherDirection`, `ListAllBlockedAccountIds`

### Sphere
- `Post/PostActionController.cs` — Bidirectional block checks for all post interactions + forward block
- `Post/PostService.cs` — `FilterWithVisibility` block param, notification filtering, mention stripping
- `Timeline/TimelineService.cs` — Blocked IDs loading and feed filtering
- `Post/PostController.cs` — `ListPosts` block filtering
- `Publisher/PublisherSubscriptionService.cs` — Notification block filtering
- `Publisher/PublisherPublicController.cs` — Search block filtering
- `Publisher/PublisherService.cs` — Follow request block check
- `Autocompletion/AutocompletionService.cs` — Autocomplete block filtering

### Messager
- `Chat/ChatRoomController.cs` — Bidirectional DM creation and invite checks
- `Chat/ChatService.cs` — DM message sending block check, notification filtering
