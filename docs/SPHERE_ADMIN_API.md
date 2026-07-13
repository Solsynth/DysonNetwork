# Sphere Admin API

Admin-facing moderation APIs in `DysonNetwork.Sphere` for posts, tags, categories, collections, and publishers.

All routes below are local development routes. In production, the gateway rewrites them to:

- `/sphere/admin/...`

All JSON fields are serialized as `snake_case`.

## Overview

| Resource | Controller | Base route |
| --- | --- | --- |
| Posts | `PostAdminController` | `/api/admin/posts` |
| Tags | `PostTagAdminController` | `/api/admin/tags` (also `/api/admin/posts/tags`) |
| Categories | `PostCategoryAdminController` | `/api/admin/categories` |
| Collections | `PostCollectionAdminController` | `/api/admin/collections` |
| Publishers | `PublisherAdminController` | `/api/admin/publishers` |
| Stats | `PostStatsAdminController` | `/api/admin/stats` |

Post moderation details live in [POST_ADMIN_API.md](./POST_ADMIN_API.md).

## Permissions

| Permission | Purpose |
| --- | --- |
| `posts.moderate` | List/detail for posts, tags, categories, and collections |
| `posts.lock` | Lock and unlock posts |
| `posts.delete` | Delete posts |
| `posts.tags.create` | Create tags as admin |
| `posts.tags.update` | Update tag name/description |
| `posts.tags.delete` | Delete tags |
| `posts.tags.assign` | Assign or unassign tag ownership |
| `posts.tags.protect` | Toggle protected flag |
| `posts.tags.event` | Toggle event flag |
| `post.categories.manage` | Create, update, delete categories |
| `post.collections.update` | Update collection metadata |
| `post.collections.delete` | Delete collections |
| `publishers.moderate` | List/detail publishers, shadowban, verification |
| `publishers.update` | Admin profile updates |
| `publishers.delete` | Delete publishers as admin |

---

## Tags

Base route: `/api/admin/tags`  
Legacy alias: `/api/admin/posts/tags`

### GET /api/admin/tags

Required permission: `posts.moderate`

Query parameters:

- `query` optional text search across slug, name, description
- `owner_publisher_id` optional owner filter
- `unowned` optional boolean — only unowned tags when `true`
- `is_protected` optional boolean
- `is_event` optional boolean
- `order` optional: `usage`, `name`, `created` (default updated desc)
- `offset` default `0`
- `take` default `50`, max `200`

Response headers:

- `X-Total` total matching count

Each tag includes `usage` (post count) and `owner_publisher` when owned.

### GET /api/admin/tags/{slug}

Required permission: `posts.moderate`

### POST /api/admin/tags

Required permission: `posts.tags.create`

```json
{
  "slug": "photography",
  "name": "Photography",
  "description": "Optional",
  "owner_publisher_id": null
}
```

### PATCH /api/admin/tags/{slug}

Required permission: `posts.tags.update`

```json
{
  "name": "Photo",
  "description": "Updated"
}
```

### POST /api/admin/tags/{slug}/assign

Required permission: `posts.tags.assign`

```json
{
  "publisher_id": "550e8400-e29b-41d4-a716-446655440000"
}
```

### DELETE /api/admin/tags/{slug}/assign

Required permission: `posts.tags.assign`

Clears ownership and protection.

### PATCH /api/admin/tags/{slug}/protect

Required permission: `posts.tags.protect`

```json
{
  "is_protected": true
}
```

### PATCH /api/admin/tags/{slug}/event

Required permission: `posts.tags.event`

```json
{
  "is_event": true,
  "ends_at": "2026-12-31T23:59:59Z"
}
```

### DELETE /api/admin/tags/{slug}

Required permission: `posts.tags.delete`

Detaches the tag from posts and removes it. Returns `204`.

---

## Categories

Base route: `/api/admin/categories`

### GET /api/admin/categories

Required permission: `posts.moderate`

Query parameters:

- `query` optional text search across slug and name
- `order` optional: `usage`, `name`, `created`
- `offset` / `take`

Response includes `usage` (post count) and `X-Total`.

### GET /api/admin/categories/{slug}

Required permission: `posts.moderate`

### POST /api/admin/categories

Required permission: `post.categories.manage`

```json
{
  "slug": "tech",
  "name": "Technology"
}
```

### PATCH /api/admin/categories/{slug}

Required permission: `post.categories.manage`

```json
{
  "slug": "technology",
  "name": "Technology"
}
```

### DELETE /api/admin/categories/{slug}

Required permission: `post.categories.manage`

Detaches posts and subscriptions, then deletes. Returns `204`.

---

## Collections

Base route: `/api/admin/collections`

### GET /api/admin/collections

Required permission: `posts.moderate`

Query parameters:

- `query` optional search across slug, name, description, publisher name/nick
- `publisher_id` optional publisher filter
- `offset` / `take`

Each collection includes `publisher` and `item_count`.

### GET /api/admin/collections/{id}

Required permission: `posts.moderate`

### PATCH /api/admin/collections/{id}

Required permission: `post.collections.update`

```json
{
  "name": "Featured",
  "description": "Admin-curated list"
}
```

### DELETE /api/admin/collections/{id}

Required permission: `post.collections.delete`

Returns `204`.

---

## Publishers

Base route: `/api/admin/publishers`

### GET /api/admin/publishers

Required permission: `publishers.moderate`

Query parameters:

- `query` optional search across name, nick, bio
- `type` optional `individual` / `organizational`
- `shadowban_reason` optional `PublisherShadowbanReason`
- `shadowbanned` optional boolean
- `gatekept` optional boolean
- `account_id` optional account filter
- `offset` / `take`

### GET /api/admin/publishers/{name}

Required permission: `publishers.moderate`

Response shape:

```json
{
  "publisher": { "...": "..." },
  "member_count": 3,
  "post_count": 42,
  "collection_count": 2,
  "subscriber_count": 100
}
```

### PATCH /api/admin/publishers/{name}

Required permission: `publishers.update`

```json
{
  "name": "new-name",
  "nick": "Display Name",
  "bio": "About",
  "gatekept_follows": true,
  "moderate_subscription": false
}
```

### POST /api/admin/publishers/{name}/shadowban

Required permission: `publishers.moderate`

```json
{
  "reason": "spam"
}
```

Supported reasons: `none`, `spam`, `advertising`, `harassment`, `hate_speech`, `misinformation`, `illegal`, `other`.

### DELETE /api/admin/publishers/{name}/shadowban

Required permission: `publishers.moderate`

### POST /api/admin/publishers/{name}/verification

Required permission: `publishers.moderate`

```json
{
  "type": "creator",
  "title": "Verified Creator",
  "description": "Optional",
  "verified_by": "admin"
}
```

### DELETE /api/admin/publishers/{name}/verification

Required permission: `publishers.moderate`

### DELETE /api/admin/publishers/{name}

Required permission: `publishers.delete`

Returns `204`.

---

## Action Logging

Admin actions are recorded through `RemoteActionLogService`.

| Action type | Used by |
| --- | --- |
| `posts.moderate` / `posts.delete` | Post admin |
| `posts.tags.admin` | Tag admin |
| `post.categories.manage` | Category admin |
| `post.collections.admin` | Collection admin |
| `publishers.moderate` / `publishers.update` / `publishers.delete` | Publisher admin |

---

## Related Docs

- [POST_ADMIN_API.md](./POST_ADMIN_API.md)
- [POST_TAG_OWNERSHIP.md](./POST_TAG_OWNERSHIP.md)
- [POST_COLLECTIONS.md](./POST_COLLECTIONS.md)
- [ADMIN_STATS_API.md](./ADMIN_STATS_API.md)
- [PERMISSIONS.md](./PERMISSIONS.md)
