# Account Timeline API

Retrieves a unified timeline of account events, including status changes and presence activities.

## Endpoint

```
GET /api/accounts/{name}/timeline
```

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

Returns an array of `AccountTimelineItem` objects ordered by `createdAt` descending (newest first).

```json
[
  {
    "id": "uuid",
    "createdAt": "2024-01-15T10:30:00Z",
    "eventType": "StatusChange",
    "status": {
      "id": "uuid",
      "attitude": "Neutral",
      "type": "Default",
      "label": "Online",
      "symbol": null,
      "isAutomated": false,
      "accountId": "uuid",
      "createdAt": "2024-01-15T10:30:00Z"
    },
    "activity": null
  },
  {
    "id": "uuid",
    "createdAt": "2024-01-15T09:15:00Z",
    "eventType": "Activity",
    "status": null,
    "activity": {
      "id": "uuid",
      "type": "Gaming",
      "manualId": "elden-ring",
      "title": "Elden Ring",
      "subtitle": "Level 45",
      "caption": "Leyndell, Royal Capital",
      "largeImage": null,
      "smallImage": null,
      "leaseMinutes": 5,
      "leaseExpiresAt": "2024-01-15T09:20:00Z",
      "accountId": "uuid",
      "createdAt": "2024-01-15T09:15:00Z"
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
