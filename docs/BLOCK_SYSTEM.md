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
| `/api/relationships/{accountId}/block` | POST | Block a user (accepts optional expiry body) |
| `/api/relationships/{accountId}/block` | DELETE | Unblock a user |
| `/api/relationships/{accountId}/mute` | POST | Mute a user (accepts optional expiry body) |
| `/api/relationships/{accountId}/mute` | DELETE | Unmute a user |
| `/api/relationships/{accountId}/close-friend` | POST | Add user to close friends |
| `/api/relationships/{accountId}/close-friend` | DELETE | Remove user from close friends |
| `/api/relationships/close-friends` | GET | List current user's close friends |
| `/api/relationships/{accountId}/alias` | PATCH | Set alias for a relationship |
| `/api/relationships/{accountId}/mutual-friends` | GET | Get mutual friends with another user |
| `/api/relationships/sync` | POST | Delta sync — returns changes since timestamp |
| `/api/relationships/inspect/{accountId}` | GET | Lists friends, blocked, muted, pending, close friends |

### Request Body (block/mute POST)

```json
{
  "expires_in": "7d",
  "degrade_to": -50
}
```

| Field | Type | Description |
|---|---|---|
| `expires_in` | `string?` | Duration string: `30m`, `1h`, `24h`, `7d`, `30d`. Null = permanent. |
| `degrade_to` | `int?` | `RelationshipStatus` value to transition to on expiry. Null = remove. `-50` = Muted. |

Examples:
- Block for 7 days, then remove: `{ "expires_in": "7d" }`
- Block for 7 days, then mute: `{ "expires_in": "7d", "degrade_to": -50 }`
- Mute for 24 hours: `{ "expires_in": "24h" }`

## Expiry

Block and mute support optional expiry with automatic degradation.

### How It Works

1. Relationship is created with `ExpiredAt` and optional `DegradeToStatus`
2. A Quartz job (`RelationshipExpiryJob`) runs every 5 minutes
3. For each expired relationship:
   - If `DegradeToStatus` is set → status transitions (e.g., Block → Mute), `ExpiredAt` cleared
   - If `DegradeToStatus` is null → relationship is deleted
4. Cache is purged for both users

### Model

`SnAccountRelationship` has two expiry-related fields:
- `ExpiredAt` (existing) — when the relationship expires
- `DegradeToStatus` (new) — what status to transition to on expiry

### Job

`RelationshipExpiryJob` in `DysonNetwork.Passport/Account/` — registered in `ScheduledJobsConfiguration.cs`, runs every 5 minutes.

## Close Friends

A per-account curated list of up to **200** close friends. One-directional (like Mute — A adding B doesn't mean B has A).

### Behavior

- `CloseFriend = 200` in `RelationshipStatus` enum
- Adding/removing creates action logs: `relationships.close_friend.add`, `relationships.close_friend.remove`
- Cache key: `accounts:close_friends:{accountId}`
- Max 200 close friends per account (enforced in `RelationshipService.AddCloseFriend`)

### API

| Endpoint | Method | Description |
|---|---|---|
| `/api/relationships/{accountId}/close-friend` | POST | Add to close friends |
| `/api/relationships/{accountId}/close-friend` | DELETE | Remove from close friends |
| `/api/relationships/close-friends` | GET | List close friends (returns hydrated accounts) |

## Relationship Alias

Users can set a custom display name (alias) for any related user. The alias is stored per-relationship (directional — A's alias for B is separate from B's alias for A).

### API

| Endpoint | Method | Description |
|---|---|---|
| `/api/relationships/{accountId}/alias` | PATCH | Set/clear alias. Body: `{ "alias": "nickname" }` (null to clear) |

- Max 128 characters
- Alias is included in the `DyRelationship` proto as `optional string alias`
- Hydrated by the client from the sync endpoint

## Mutual Friends

Get the intersection of two users' friend lists.

### API

| Endpoint | Method | Description |
|---|---|---|
| `/api/relationships/{accountId}/mutual-friends` | GET | Returns `List<SnAccount>` of mutual friends |

## Delta Sync

Client-side sync endpoint that returns all relationship changes since a given timestamp. Uses `ModelBase.CreatedAt`, `UpdatedAt`, `DeletedAt` for tracking.

### API

```
POST /api/relationships/sync
Body: { "last_sync_timestamp": 1716940800000 }
```

### Response

```json
{
  "added": [ /* new SnAccountRelationship objects */ ],
  "updated": [ /* modified SnAccountRelationship objects */ ],
  "removed": [ "guid-of-deleted-relationship", ... ],
  "server_timestamp": 1716940800000
}
```

| Field | Description |
|---|---|
| `added` | Relationships created since timestamp (hydrated with accounts) |
| `updated` | Relationships modified since timestamp (hydrated with accounts) |
| `removed` | Related IDs of soft-deleted relationships |
| `server_timestamp` | Current server time — client stores this for next sync |

### Client Flow

1. First sync: pass `0` as `last_sync_timestamp` to get all relationships
2. Store `server_timestamp` from response
3. Next sync: pass stored `server_timestamp` as `last_sync_timestamp`
4. Apply additions, updates, and removals to local DB

## Post Visibility

Two new `PostVisibility` values:

### CloseFriendsOnly (4)

- Only the publisher's close friends can see the post
- Publisher members (owner/manager/editor/viewer) always see it
- Requires a publisher (returns 400 if set on a post without publisher)
- Notifications are sent only to close friends

### SubscriberOnly (5)

- Only users with an active subscription (accepted follow request) can see the post
- Publisher members always see it
- Requires a publisher (returns 400 if set on a post without publisher)
- Works independently of publisher-level Gatekept (AND logic):
  - If publisher is Gatekept AND post is SubscriberOnly → subscription satisfies both
  - If publisher is not Gatekept but post is SubscriberOnly → subscription required for that post

### Proto

`DY_POST_CLOSE_FRIENDS_ONLY = 5`, `DY_POST_SUBSCRIBER_ONLY = 6` in `DyPostVisibility` enum.

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
- `proto/profile.proto` — `bool either_direction = 4` on `DyGetRelationshipRequest`, `rpc ListMuted`, `rpc ListCloseFriends`
- `proto/post.proto` — `DY_POST_CLOSE_FRIENDS_ONLY = 5`, `DY_POST_SUBSCRIBER_ONLY = 6`
- `proto/account.proto` — `optional string alias = 8` on `DyRelationship`

### Passport
- `Account/RelationshipService.cs` — `IsBlockedEitherDirection`, `ListAllBlockedAccountIds`, `MuteAccount`, `UnmuteAccount`, `ListAccountMuted`, `AddCloseFriend`, `RemoveCloseFriend`, `ListCloseFriends`, `IsCloseFriend`, `UpdateAlias`, `GetMutualFriends`, `GetRelationshipDelta`, `ProcessExpiredRelationshipsAsync`, cache purge
- `Account/AccountServiceGrpc.cs` — `HasRelationship` handles `EitherDirection`, `ListMuted`, `ListCloseFriends` implementation
- `Account/RelationshipController.cs` — Block/mute/close-friend endpoints with expiry support, alias PATCH, mutual-friends GET, sync POST, inspect includes muted + close friends
- `Account/RelationshipExpiryJob.cs` — Quartz job for processing expired relationships
- `Startup/ScheduledJobsConfiguration.cs` — Registers `RelationshipExpiryJob` (every 5 min)

### Shared
- `Models/Relationship.cs` — `Muted = -50`, `CloseFriend = 200` enum values, `DegradeToStatus`, `Alias` properties
- `Models/Post.cs` — `CloseFriendsOnly`, `SubscriberOnly` in `PostVisibility` enum
- `Models/ActionLog.cs` — `RelationshipMute`, `RelationshipUnmute`, `RelationshipCloseFriend`, `RelationshipUnCloseFriend` constants
- `Registry/RemoteAccountService.cs` — `IsBlockedEitherDirection`, `ListAllBlockedAccountIds`, `ListMutedAccountIds`, `ListCloseFriendAccountIds`, `GetCloseFriendPublisherIds`, `IsCloseFriend`

### Sphere
- `Post/PostActionController.cs` — Bidirectional block checks for all post interactions + forward block, publisher-only visibility validation
- `Post/PostService.cs` — `FilterWithVisibility` block + mute + close friends + subscriber only params, notification filtering, mention stripping, `FilterUsersByPostVisibility` new cases
- `Timeline/TimelineService.cs` — Blocked + muted + close friends IDs loading and feed filtering
- `Post/PostController.cs` — `ListPosts` block + mute + close friends + subscriber only filtering, inline subscriber/close-friend access checks, `GetCloseFriendPublisherIdsAsync` helper, `GetGatekeepInfoAsync` returns close friend publisher IDs
- `Publisher/PublisherSubscriptionService.cs` — Notification block + mute filtering, `NotifyCloseFriendsPost`, `NotifySubscriberPost` handles `SubscriberOnly` and `CloseFriendsOnly`
- `Publisher/PublisherPublicController.cs` — Search block + mute filtering
- `Publisher/PublisherService.cs` — Follow request block check
- `Autocompletion/AutocompletionService.cs` — Autocomplete block + mute filtering

### Messager
- `Chat/ChatRoomController.cs` — Bidirectional DM creation and invite checks (block only)
- `Chat/ChatService.cs` — DM message sending block check, notification block + mute filtering
