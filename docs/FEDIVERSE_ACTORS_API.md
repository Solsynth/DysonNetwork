# Fediverse Actor API

Endpoints for querying remote Fediverse actors (users from Mastodon, Misskey, and other ActivityPub-compatible servers).

## Base URL

```
/api/fediverse/actors
```

## Endpoints

### Get Actor by Handle

Get a remote actor by their full handle (`username@instance`).

```
GET /api/fediverse/actors/{username}@{instance}
```

**Parameters:**
| Name | In | Type | Required | Description |
|------|-----|------|----------|-------------|
| `username` | path | string | Yes | Actor's username |
| `instance` | path | string | Yes | Server domain (e.g., `mastodon.social`) |
| `includeActivity` | query | bool | No | Include recent posts (default: false) |

**Example:**
```bash
GET /api/fediverse/actors/Gargr@mastodon.social
```

**Response:**
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "type": "Person",
  "username": "Gargr",
  "fullHandle": "@Gargr@mastodon.social",
  "displayName": "Gargr",
  "bio": "Software developer",
  "avatarUrl": "https://mastodon.social/avatars/Gargr.png",
  "headerUrl": "https://mastodon.social/headers/Gargr.png",
  "isBot": false,
  "isLocked": false,
  "isDiscoverable": true,
  "instanceDomain": "mastodon.social",
  "instanceName": "Mastodon",
  "instanceSoftware": "mastodon",
  "followersCount": 150,
  "followingCount": 89,
  "lastActivityAt": "2026-03-27T10:30:00Z",
  "lastFetchedAt": "2026-03-27T12:00:00Z",
  "webUrl": "https://mastodon.social/@Gargr",
  "recentPosts": []
}
```

---

### Get Actor by ID

Get a remote actor by their database ID.

```
GET /api/fediverse/actors/{id}
```

**Parameters:**
| Name | In | Type | Required | Description |
|------|-----|------|----------|-------------|
| `id` | path | guid | Yes | Actor's database ID |
| `includeActivity` | query | bool | No | Include recent posts (default: false) |

**Example:**
```bash
GET /api/fediverse/actors/3fa85f64-5717-4562-b3fc-2c963f66afa6
```

---

### Search Actors

Search for remote actors by username or display name.

```
GET /api/fediverse/actors/search
```

**Parameters:**
| Name | In | Type | Required | Description |
|------|-----|------|----------|-------------|
| `query` | query | string | Yes | Search query |
| `limit` | query | int | No | Max results (1-50, default: 20) |

**Example:**
```bash
GET /api/fediverse/actors/search?query=gargr&limit=10
```

**Response Example:**
```json
[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "type": "Person",
    "username": "Gargr",
    "fullHandle": "@Gargr@mastodon.social",
    "displayName": "Gargr",
    "bio": "Software developer",
    "avatarUrl": "https://mastodon.social/avatars/Gargr.png",
    "headerUrl": "https://mastodon.social/headers/Gargr.png",
    "isBot": false,
    "isLocked": false,
    "isDiscoverable": true,
    "instanceDomain": "mastodon.social",
    "instanceName": "Mastodon",
    "instanceSoftware": "mastodon",
    "instance": {
      "id": "...",
      "domain": "mastodon.social",
      "name": "Mastodon",
      "description": "The original server operated by the Mastodon gGmbH non-profit",
      "software": "mastodon",
      "version": "4.2.0",
      "iconUrl": "https://mastodon.social/icons/icon.png",
      "thumbnailUrl": "https://mastodon.social/headers/thumbnail.png",
      "contactEmail": "staff@mastodon.social",
      "activeUsers": 1000000,
      "isBlocked": false,
      "isSilenced": false
    },
    "followersCount": 150,
    "followingCount": 89,
    "lastActivityAt": "2026-03-27T10:30:00Z",
    "lastFetchedAt": "2026-03-27T12:00:00Z",
    "webUrl": "https://mastodon.social/@Gargr"
  }
]
```

---

### Get Actor's Posts

Get public posts from a remote actor, including both original posts and boosts. This endpoint fetches posts from both locally stored data and the remote actor's ActivityPub outbox.

```
GET /api/fediverse/actors/{id}/posts
```

**Parameters:**
| Name | In | Type | Required | Description |
|------|-----|------|----------|-------------|
| `id` | path | guid | Yes | Actor's database ID |
| `take` | query | int | No | Number of posts (default: 20) |
| `offset` | query | int | No | Skip count (default: 0) |

**Headers:**
| Name | Description |
|------|-------------|
| `X-Total` | Total number of posts and boosts |

**Example:**
```bash
GET /api/fediverse/actors/3fa85f64-5717-4562-b3fc-2c963f66afa6/posts?take=20&offset=0
```

**Response:** Returns a list of `PostResponse` objects containing both original posts and boosted posts.

**Data Sources:**
- **Local posts:** Posts stored in our database for this actor (received via ActivityPub federation)
- **Local boosts:** Boosts by this actor stored in our database
- **Remote outbox:** Posts fetched directly from the actor's ActivityPub outbox endpoint

**Example Response (original post from remote outbox):**
```json
{
  "id": "00000000-0000-0000-0000-000000000000",
  "title": "Post Title",
  "content": "Post content here...",
  "publishedAt": "2026-03-27T10:00:00Z",
  "visibility": "Public",
  "actorId": "actor-uuid",
  "actor": { ... },
  "boostInfo": null
}
```

**Example Response (boosted post):**
```json
{
  "id": "00000000-0000-0000-0000-000000000000",
  "title": "Original Post Title",
  "content": "Original post content...",
  "publishedAt": "2026-03-27T08:00:00Z",
  "visibility": "Public",
  "actorId": "booster-actor-uuid",
  "actor": { "id": "booster-uuid", ... },
  "boostInfo": {
    "boostId": "00000000-0000-0000-0000-000000000000",
    "boostedAt": "2026-03-27T09:30:00Z",
    "activityPubUri": "https://mastodon.social/users/booster/statuses/123",
    "webUrl": "https://mastodon.social/@booster/123",
    "originalPost": { ... },
    "originalActor": { "id": "original-author-uuid", ... }
  }
}
```

**Notes:**
- Posts are fetched from both local database and remote outbox, then merged and deduplicated
- Boosted posts are merged with original posts and sorted by `publishedAt` (original post date)
- For boosted posts, `actorId` is the booster, not the original author
- Remote posts will have `id = "00000000-0000-0000-0000-000000000000"` since they don't exist in our DB
- `X-Total` header includes all posts (local + remote)
- Uses `fediverseUri` field for deduplication between local and remote data

---

### Get Actor's Followers

Get list of actors who follow this actor.

```
GET /api/fediverse/actors/{id}/followers
```

**Parameters:**
| Name | In | Type | Required | Description |
|------|-----|------|----------|-------------|
| `id` | path | guid | Yes | Actor's database ID |
| `take` | query | int | No | Number of followers (default: 40) |
| `offset` | query | int | No | Skip count (default: 0) |

**Headers:**
| Name | Description |
|------|-------------|
| `X-Total` | Total number of followers |

**Example:**
```bash
GET /api/fediverse/actors/3fa85f64-5717-4562-b3fc-2c963f66afa6/followers?take=40&offset=0
```

---

### Get Actor's Following

Get list of actors this actor follows.

```
GET /api/fediverse/actors/{id}/following
```

**Parameters:**
| Name | In | Type | Required | Description |
|------|-----|------|----------|-------------|
| `id` | path | guid | Yes | Actor's database ID |
| `take` | query | int | No | Number of following (default: 40) |
| `offset` | query | int | No | Skip count (default: 0) |

**Headers:**
| Name | Description |
|------|-------------|
| `X-Total` | Total number of following |

**Example:**
```bash
GET /api/fediverse/actors/3fa85f64-5717-4562-b3fc-2c963f66afa6/following?take=40&offset=0
```

---

### Get Relationship (Authenticated)

Get the current user's relationship with a remote actor.

```
GET /api/fediverse/actors/{id}/relationship
```

**Authentication:** Required

**Parameters:**
| Name | In | Type | Required | Description |
|------|-----|------|----------|-------------|
| `id` | path | guid | Yes | Actor's database ID |

**Example:**
```bash
GET /api/fediverse/actors/3fa85f64-5717-4562-b3fc-2c963f66afa6/relationship
Authorization: Bearer <token>
```

**Response:**
```json
{
  "actorId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "actorUsername": "Gargr",
  "actorInstance": "mastodon.social",
  "actorHandle": "@Gargr@mastodon.social",
  "isFollowing": true,
  "isFollowedBy": false,
  "isPending": false
}
```

---

## Response Models

### FediverseActorResponse

| Field | Type | Description |
|-------|------|-------------|
| `id` | guid | Unique identifier |
| `type` | string | Actor type (Person, Service, Application, etc.) |
| `username` | string | Username without instance |
| `fullHandle` | string | Full handle including instance |
| `displayName` | string? | Display name or nickname |
| `bio` | string? | Biography/description |
| `avatarUrl` | string? | Profile picture URL |
| `headerUrl` | string? | Profile banner URL |
| `isBot` | bool | Whether this is an automated account |
| `isLocked` | bool | Whether follow requests require approval |
| `isDiscoverable` | bool | Whether actor appears in discovery |
| `instanceDomain` | string? | Fediverse server domain |
| `instanceName` | string? | Human-readable server name |
| `instanceSoftware` | string? | Server software (mastodon, misskey, etc.) |
| `instance` | FediverseInstanceResponse? | Full instance information |
| `followersCount` | int | Number of followers |
| `followingCount` | int | Number of accounts being followed |
| `lastActivityAt` | datetime? | When the actor was last active |
| `lastFetchedAt` | datetime? | When local data was last updated |
| `webUrl` | string | Direct link to profile |
| `recentPosts` | Post[]? | Recent posts (when `includeActivity=true`) |

### FediverseInstanceResponse

Full information about a Fediverse instance (server).

| Field | Type | Description |
|-------|------|-------------|
| `id` | guid? | Instance ID |
| `domain` | string | Server domain (e.g., `mastodon.social`) |
| `name` | string? | Human-readable server name |
| `description` | string? | Server description/about page |
| `software` | string? | Software name (mastodon, misskey, etc.) |
| `version` | string? | Software version |
| `iconUrl` | string? | Server icon/avatar |
| `thumbnailUrl` | string? | Server banner image |
| `contactEmail` | string? | Contact email address |
| `contactAccountUsername` | string? | Contact account username |
| `activeUsers` | int? | Approximate active user count |
| `metadata` | object? | Additional instance metadata |
| `isBlocked` | bool | Whether instance is blocked |
| `isSilenced` | bool | Whether instance is silenced |
| `lastFetchedAt` | datetime? | When instance data was last fetched |
| `lastActivityAt` | datetime? | Last recorded activity |

### FediverseRelationshipResponse

| Field | Type | Description |
|-------|------|-------------|
| `actorId` | guid | The remote actor's ID |
| `actorUsername` | string | The actor's username |
| `actorInstance` | string? | The actor's server domain |
| `actorHandle` | string | Full handle |
| `isFollowing` | bool | Current user follows this actor |
| `isFollowedBy` | bool | This actor follows the current user |
| `isPending` | bool | Follow request is pending (for locked accounts) |

### PostResponse

Post response model that extends `SnPost` with boost information.

| Field | Type | Description |
|-------|------|-------------|
| `id` | guid | Post ID (original post ID for boosted posts) |
| `title` | string? | Post title |
| `content` | string? | Post content |
| `publishedAt` | datetime? | Original post publish date (not boost date) |
| `visibility` | enum | Post visibility (Public, Followers, Direct) |
| `actorId` | guid | Actor who posted OR boosted (booster for boosts) |
| `actor` | FediverseActorResponse? | The actor object |
| `publisherId` | guid? | Publisher ID |
| `tags` | PostTag[]? | Post tags |
| `attachments` | CloudFileReferenceObject[]? | Media attachments |
| `boostInfo` | BoostInfo? | Non-null if this is a boosted post |

### BoostInfo

Contains information about the boost, including the original post and author.

| Field | Type | Description |
|-------|------|-------------|
| `boostId` | guid | The boost record ID |
| `boostedAt` | datetime | When the boost occurred |
| `activityPubUri` | string? | ActivityPub URI of the boost activity |
| `webUrl` | string? | Web URL to the boost |
| `originalPost` | SnPost | The original post that was boosted |
| `originalActor` | FediverseActorResponse? | The original post's author |

---

## Notes

- All timestamps are in UTC ISO 8601 format
- Pagination uses `take` and `offset` with `X-Total` header for total counts
- Actor data is fetched from remote servers and cached locally
- Public endpoints return cached data when available
- Authenticated endpoints (relationship) show personalized data
- The `instance` field includes full instance data (description, version, icon, etc.) when available
- Flat instance fields (`instanceDomain`, `instanceName`, `instanceSoftware`) are provided for convenience
- Posts endpoint (`/posts`) fetches from both local DB and remote ActivityPub outbox for comprehensive results
