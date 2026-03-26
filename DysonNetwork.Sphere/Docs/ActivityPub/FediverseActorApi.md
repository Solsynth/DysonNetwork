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

---

### Get Actor's Posts

Get public posts from a remote actor.

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
| `X-Total` | Total number of posts |

**Example:**
```bash
GET /api/fediverse/actors/3fa85f64-5717-4562-b3fc-2c963f66afa6/posts?take=20&offset=0
```

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
| `followersCount` | int | Number of followers |
| `followingCount` | int | Number of accounts being followed |
| `lastActivityAt` | datetime? | When the actor was last active |
| `lastFetchedAt` | datetime? | When local data was last updated |
| `webUrl` | string | Direct link to profile |
| `recentPosts` | Post[]? | Recent posts (when `includeActivity=true`) |

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

---

## Notes

- All timestamps are in UTC ISO 8601 format
- Pagination uses `take` and `offset` with `X-Total` header for total counts
- Actor data is fetched from remote servers and cached locally
- Public endpoints return cached data when available
- Authenticated endpoints (relationship) show personalized data
