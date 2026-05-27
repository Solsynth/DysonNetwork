# Block & Mute System

## Overview

Two relationship mechanisms control visibility between users:

- **Block** (`status = -100`) — mutual interaction restriction. Neither user sees or interacts with the other.
- **Mute** (`status = -50`) — one-directional visibility hide. The muting user stops seeing the muted user, but the muted user is unaffected.

Both are stored in the `account_relationships` table as directional records.

## Block

### Storage

```
account_id = A, related_id = B, status = -100
```

Behavioral checks query **both directions** — a block is mutual.

### Helpers

**`IsBlockedEitherDirection(userId, otherId)`** — returns `true` if A→B or B→A blocked.

- Passport: `RelationshipService.cs`
- Shared: `RemoteAccountService.cs`
- gRPC: `HasRelationship` with `EitherDirection = true`, `Status = -100`

**`ListAllBlockedAccountIds(accountId)`** — returns union of blocked + blocked-by IDs.

### Behavioral Rules

| Surface | Block Behavior |
|---|---|
| Post interactions (reply, react, forward, boost, award) | Blocked in both directions, returns 400 |
| Feed & timeline | Posts from blocked users hidden |
| Search & autocomplete | Blocked users/publishers hidden |
| Notifications | All suppressed between blocked users |
| Direct messages | Blocked, returns 403 |
| Mentions | Silently stripped (no notification sent) |
| Follow requests | Blocked, returns 400 |

## Mute

### Storage

```
account_id = A, related_id = B, status = -50
```

Mute is **one-directional** — only A's view is affected.

### Helpers

**`ListAccountMuted(accountId)`** — returns list of account IDs muted by this user.

- Passport: `RelationshipService.cs`
- gRPC: `ListMuted` RPC
- Shared: `RemoteAccountService.ListMutedAccountIds(accountId)`

### Behavioral Rules

| Surface | Mute Behavior |
|---|---|
| Post interactions (reply, react, forward, boost, award) | **Not restricted** — muted user can still interact |
| Feed & timeline | Posts from muted users hidden |
| Search & autocomplete | Muted users/publishers hidden |
| Notifications from muted user | **Suppressed** (push, subscription, mention) |
| Direct messages | **Not restricted** — DMs still work |
| Mentions | Silently stripped (no notification sent) |
| Follow requests | **Not restricted** |

### Key Differences from Block

| Aspect | Mute (A mutes B) | Block (A blocks B) |
|---|---|---|
| A sees B's content | No | No |
| B sees A's content | Yes | No |
| B can interact with A | Yes | No |
| A gets B's notifications | No | No |
| B gets A's notifications | Yes | No |
| Direction | One-directional | Mutual |

### Block Implies Mute

When A blocks B, B is automatically hidden from A's feed, search, and notifications. The block filtering code always merges blocked + muted IDs before filtering.

## Caching

All data is cached in Redis (shared across services):

| Key Pattern | Purpose | TTL |
|---|---|---|
| `accounts:blocked:{accountId}:{isRelated}` | One-direction blocked list | 1 hour |
| `accounts:blocked:all:{accountId}` | Both-directions blocked list | 1 hour |
| `accounts:blocked:either:{smallerId}:{largerId}` | Bidirectional check (symmetric) | 1 hour |
| `accounts:muted:{accountId}:False` | One-direction muted list | 1 hour |

Cache is purged on relationship changes via `PurgeRelationshipCache`.

## API Endpoints

| Endpoint | Method | Description |
|---|---|---|
| `/api/relationships/{accountId}/block` | POST | Block a user |
| `/api/relationships/{accountId}/block` | DELETE | Unblock a user |
| `/api/relationships/{accountId}/mute` | POST | Mute a user |
| `/api/relationships/{accountId}/mute` | DELETE | Unmute a user |
| `/api/relationships/inspect/{accountId}` | GET | Lists friends, blocked, muted, pending |

## What Does NOT Happen

Neither block nor mute cleans up existing data:

- Old reactions are **not** removed
- Old follows are **not** removed
- Old messages remain accessible
- Historical mentions are preserved

They only prevent **new** interactions (block) and **filter** from discovery surfaces (both).

## Profile Access

Public profiles remain accessible read-only for both blocked and muted users. Interaction UI is removed by the interaction-level checks above.

## Files Changed

### DysonSpec (proto)
- `proto/profile.proto` — `bool either_direction = 4` on `DyGetRelationshipRequest`, `rpc ListMuted`

### Passport
- `Account/RelationshipService.cs` — `IsBlockedEitherDirection`, `ListAllBlockedAccountIds`, `MuteAccount`, `UnmuteAccount`, `ListAccountMuted`, cache purge
- `Account/AccountServiceGrpc.cs` — `HasRelationship` handles `EitherDirection`, `ListMuted` implementation
- `Account/RelationshipController.cs` — Mute/unmute endpoints, inspect includes muted

### Shared
- `Models/Relationship.cs` — `Muted = -50` enum value
- `Models/ActionLog.cs` — `RelationshipMute`, `RelationshipUnmute` constants
- `Registry/RemoteAccountService.cs` — `IsBlockedEitherDirection`, `ListAllBlockedAccountIds`, `ListMutedAccountIds`

### Sphere
- `Post/PostActionController.cs` — Bidirectional block checks for all post interactions + forward block
- `Post/PostService.cs` — `FilterWithVisibility` block + mute params, notification filtering, mention stripping
- `Timeline/TimelineService.cs` — Blocked + muted IDs loading and feed filtering
- `Post/PostController.cs` — `ListPosts` block + mute filtering
- `Publisher/PublisherSubscriptionService.cs` — Notification block + mute filtering
- `Publisher/PublisherPublicController.cs` — Search block + mute filtering
- `Publisher/PublisherService.cs` — Follow request block check
- `Autocompletion/AutocompletionService.cs` — Autocomplete block + mute filtering

### Messager
- `Chat/ChatRoomController.cs` — Bidirectional DM creation and invite checks (block only)
- `Chat/ChatService.cs` — DM message sending block check, notification block + mute filtering
