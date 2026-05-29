# Account Timeline API

Retrieves a unified timeline of account events, including status changes and presence activities.

This is the Passport per-account profile timeline. It is not the same as the Sphere home timeline at `GET /api/timeline`.

## Endpoint

```
GET /api/accounts/{name}/timeline
```

## Scope

This endpoint returns timeline history for one account only.

It is separate from the Sphere homepage timeline:

- Passport account timeline: one account's own `StatusChange` and `Activity` history
- Sphere home timeline: feed of posts plus friend social events like `presence.friend` and `status.friend`

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `name` | string (path) | required | Account username |
| `take` | int (query) | 20 | Number of items to return |
| `offset` | int (query) | 0 | Number of items to skip |

## Response

### Headers

| Header | Description |
|--------|-------------|
| `X-Total` | Total count of timeline items |

### Body

Returns an array of `AccountTimelineItem` objects ordered by `created_at` descending (newest first).

```json
[
  {
    "id": "uuid",
    "created_at": "2024-01-15T10:30:00Z",
    "event_type": "StatusChange",
    "status": {
      "id": "uuid",
      "attitude": "Neutral",
      "type": "Default",
      "label": "Online",
      "symbol": null,
      "is_automated": false,
      "account_id": "uuid",
      "created_at": "2024-01-15T10:30:00Z"
    },
    "activity": null
  },
  {
    "id": "uuid",
    "created_at": "2024-01-15T09:15:00Z",
    "event_type": "Activity",
    "status": null,
    "activity": {
      "id": "uuid",
      "type": "Gaming",
      "manual_id": "elden-ring",
      "title": "Elden Ring",
      "subtitle": "Level 45",
      "caption": "Leyndell, Royal Capital",
      "large_image": null,
      "small_image": null,
      "lease_minutes": 5,
      "lease_expires_at": "2024-01-15T09:20:00Z",
      "account_id": "uuid",
      "created_at": "2024-01-15T09:15:00Z"
    }
  }
]
```

## Event Types

### `StatusChange`

Represents a status update. The `status` field will be populated.

### `Activity`

Represents a presence activity (rich presence). The `activity` field will be populated.

## Status Types

| Value | Description |
|-------|-------------|
| `Default` | Default status |
| `Busy` | User is busy |
| `DoNotDisturb` | Do not disturb mode |
| `Invisible` | Hidden/invisible status |

## Presence Types

| Value | Description |
|-------|-------------|
| `Unknown` | Unspecified activity |
| `Gaming` | Gaming activity |
| `Music` | Music/listening activity |
| `Workout` | Workout/fitness activity |

## Errors

| Status | Code | Description |
|--------|------|-------------|
| 400 | `NOT_FOUND` | Account not found |

## Example

```bash
curl -X GET "http://localhost:5000/api/accounts/johndoe/timeline?take=10&offset=0" \
  -H "Accept: application/json"
```
