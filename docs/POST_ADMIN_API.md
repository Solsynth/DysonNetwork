# Post Admin API

This document describes the admin-facing post moderation APIs in `DysonNetwork.Sphere`.

All routes below are local development routes. In production, the gateway rewrites them to:

- `/sphere/admin/posts/...`

All JSON fields are serialized as `snake_case`.

## Overview

Post admin routes live in:

- `DysonNetwork.Sphere/Post/PostAdminController.cs`

They are intended for global moderation and admin tooling rather than normal publisher editing.

Use them for:

- listing posts across the system with moderation-oriented filters
- reading a single post with hydrated admin detail
- locking and unlocking posts
- changing post visibility
- shadowbanning and unshadowbanning posts
- removing a post from a realm
- deleting a post as an admin

## Permissions

| Permission | Purpose |
| --- | --- |
| `posts.moderate` | View admin post lists and details, change visibility, shadowban, and remove posts from realms |
| `posts.lock` | Lock and unlock posts |
| `posts.delete` | Permanently delete a post through the admin API |

## Base Route

```text
/api/admin/posts
```

## Listing And Detail

### GET /api/admin/posts

Returns posts across the system.

Required permission:

- `posts.moderate`

Query parameters:

- `query` optional text search across `title`, `description`, and `content`
- `publisher_id` optional publisher filter
- `realm_id` optional realm filter
- `visibility` optional `PostVisibility`
- `shadowban_reason` optional `PostShadowbanReason`
- `locked` optional boolean filter
- `drafted` optional boolean filter
- `offset` default `0`
- `take` default `50`, max `200`

Response headers:

- `X-Total` total matching post count

Notes:

- results are ordered by `published_at ?? created_at` descending
- responses include `publisher`, `tags`, and `categories`

### GET /api/admin/posts/{id}

Returns one post by id.

Required permission:

- `posts.moderate`

Behavior:

- loads the post with publisher, tags, categories, reply/forward references, and featured records
- runs the normal `PostService.LoadPostInfo(...)` hydration path before returning the response

## Lock Management

### GET /api/admin/posts/{id}/lock

Returns the current lock state for a post.

Auth:

- requires authentication

Response shape:

```json
{
  "locked": true,
  "locked_at": "2026-07-07T12:00:00Z"
}
```

### POST /api/admin/posts/{id}/lock

Locks a post.

Required permission:

- `posts.lock`

Behavior:

- sets `locked_at` to the current instant
- returns `400` if the post is already locked

### DELETE /api/admin/posts/{id}/lock

Unlocks a post.

Required permission:

- `posts.lock`

Behavior:

- clears `locked_at`
- returns `400` if the post is not locked

### POST /api/admin/posts/{id}/lock/batch

Locks multiple posts at once.

Required permission:

- `posts.lock`

Request body:

```json
[
  "550e8400-e29b-41d4-a716-446655440000",
  "7a1bd1c9-9d7d-4e77-b25c-c48a7c6b8956"
]
```

Response shape:

```json
{
  "locked": 2
}
```

### DELETE /api/admin/posts/lock/batch

Unlocks multiple posts at once.

Required permission:

- `posts.lock`

Request body:

```json
[
  "550e8400-e29b-41d4-a716-446655440000",
  "7a1bd1c9-9d7d-4e77-b25c-c48a7c6b8956"
]
```

Response shape:

```json
{
  "unlocked": 2
}
```

## Moderation Actions

### POST /api/admin/posts/{id}/visibility

Changes a post’s visibility.

Required permission:

- `posts.moderate`

Request body:

```json
{
  "visibility": "private"
}
```

Notes:

- the controller delegates to `PostService.UpdatePostAsync(...)`
- this preserves the normal post-update side effects such as edit timestamps and any downstream visibility/indexing logic

### POST /api/admin/posts/{id}/shadowban

Sets a post shadowban reason.

Required permission:

- `posts.moderate`

Request body:

```json
{
  "reason": "spam"
}
```

Supported values come from `PostShadowbanReason`:

- `none`
- `spam`
- `advertising`
- `harassment`
- `hate_speech`
- `misinformation`
- `illegal`
- `other`

Behavior:

- updates `shadowban_reason`
- sets `shadowbanned_at` to the current instant
- returns `400` if the request uses `none`; use the delete endpoint instead

### DELETE /api/admin/posts/{id}/shadowban

Clears a post shadowban.

Required permission:

- `posts.moderate`

Behavior:

- sets `shadowban_reason` back to `none`
- clears `shadowbanned_at`

### POST /api/admin/posts/{id}/realm/remove

Removes a post from its realm.

Required permission:

- `posts.moderate`

Request body:

```json
{
  "reason": "Off-topic for this realm"
}
```

Behavior:

1. verifies that the post is currently linked to a realm
2. writes a `realm_post_moderation_logs` row
3. delegates to `PostService.RemovePostFromRealmAsync(...)`

Current `RemovePostFromRealmAsync(...)` behavior:

- changes the post visibility to `private`
- clears `realm_id`
- sends a moderation notification to the post author when the publisher is account-owned

Common errors:

- `400` when the post is not linked to a realm
- `400` when the same realm-removal moderation log already exists

### DELETE /api/admin/posts/{id}

Deletes a post through the admin API.

Required permission:

- `posts.delete`

Behavior:

- delegates to `PostService.DeletePostAsync(...)`
- cascades the normal post delete side effects:
  - soft-deletes reactions
  - marks reply/forward references as gone
  - removes the post row
  - broadcasts the `post.deleted` update
  - sends ActivityPub delete delivery for public posts

Response:

- `204 No Content`

## Action Logging

Admin actions are recorded through `RemoteActionLogService`.

Current action types used by this controller:

- `posts.moderate`
- `posts.delete`

Extra metadata varies by operation, but typically includes:

- `post_id`
- `operation`
- `visibility`
- `reason`
- `realm_id`

## Related Docs

- `docs/ACCOUNT_ADMIN_API.md`
- `docs/API_POSTS.md`
- `docs/POST_SUBSCRIPTIONS.md`
