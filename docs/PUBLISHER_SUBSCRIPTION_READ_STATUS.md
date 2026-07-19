# Publisher Subscription Read Status API

## Overview

Publisher subscriptions maintain a `last_read_at` marker for each account. The marker is used to determine whether a subscription has content newer than the user's most recently read content.

With the production gateway, the `/api/publishers` base path is exposed as `/sphere/publishers`.

## Authentication and permission

All endpoints require a bearer token. Mutating endpoints also require `publishers.subscriptions.manage`.

```text
Authorization: Bearer <token>
```

## Get a publisher read status

```text
GET /api/publishers/{name}/subscription/read-status
```

Returns the active subscription, its latest public post or live-stream time, and whether it has unread content.

```json
{
  "subscription": {
    "id": "550e8400-e29b-41d4-a716-446655440000",
    "publisher_id": "550e8400-e29b-41d4-a716-446655440001",
    "account_id": "550e8400-e29b-41d4-a716-446655440002",
    "last_read_at": "2026-07-20T08:30:00Z",
    "notify": true,
    "ended_at": null
  },
  "latest_content_at": "2026-07-20T10:00:00Z",
  "has_new_content": true
}
```

## List subscriptions by latest post

```text
GET /api/publishers/subscriptions?order=latest_posted_at&offset=0&take=20
```

The default order is `created_at` descending. Set `order=latest_posted_at` to sort subscriptions by the latest public root post from each publisher, newest first. Publishers without a public root post are listed after publishers with posts. Pagination is applied after ordering.

## Mark one subscription as read

```text
PUT /api/publishers/{name}/subscription/read-status
```

An optional `last_read_at` may be supplied. When omitted, the server uses the current instant.

```json
{
  "last_read_at": "2026-07-20T10:00:00Z"
}
```

The response is the refreshed read-status object.

## Mark all subscriptions as read

```text
PUT /api/publishers/subscriptions/read-status
```

No request body is required.

```json
{
  "updated_count": 12
}
```

The endpoint considers active subscriptions only. For every subscribed publisher with public post or live-stream content, it advances `last_read_at` to that publisher's latest content timestamp. Existing markers that are already newer are not changed. Publishers without eligible content are not changed.

## Timeline behavior

When an authenticated user receives posts in the Sphere timeline, the service advances active subscription markers to the newest returned post time for each matching publisher. It does not record candidate posts that were discarded during ranking.

## Errors

| Status | Code | Meaning |
| --- | --- | --- |
| 401 | `UNAUTHORIZED` | Authentication is missing or invalid. |
| 404 | `PUBLISHER_NOT_FOUND` | The named publisher does not exist. |
| 404 | `PUBLISHER_SUBSCRIPTION_NOT_FOUND` | There is no active subscription for the named publisher. |

## Example

```bash
curl -X PUT "https://api.example.com/sphere/publishers/subscriptions/read-status" \
  -H "Authorization: Bearer <token>"
```
