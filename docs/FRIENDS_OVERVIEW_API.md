# Friends Overview API

## Overview

The Friends Overview endpoint returns the current user's friends with their online status and active presence activities.

This service is handled by DysonNetwork.Passport. When using the gateway, replace `/api` with `/passport`.

## Endpoint

### `GET /api/friends/overview`

Returns a list of friends with their account info, status, and activities.

**Authentication:** Required (`[Authorize]`)

**Query Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `include_offline` | bool | `false` | When `true`, returns all friends regardless of online status. |

**Response:** `200 OK` — `List<FriendOverviewItem>`

```json
[
  {
    "account": { "...": "SnAccount fields" },
    "status": {
      "is_online": true,
      "is_idle": false,
      "label": "Online",
      "attitude": "neutral",
      "...": "..."
    },
    "activities": [
      {
        "type": 1,
        "label": "Playing CS2",
        "lease_expires_at": "2025-01-15T10:30:00Z",
        "...": "..."
      }
    ]
  }
]
```

## Visibility Rules

The endpoint intelligently decides which friends to show based on their activity:

| Priority | Condition | Behavior |
|----------|-----------|----------|
| 1 | `include_offline=true` | Returns all friends |
| 2 | Friend is online | Always shown |
| 3 | Friend has active presence activities | Always shown (even if offline) |
| 4 | No online friends and no friends with activities | Falls back to friends whose status was updated in the last **1 hour** |
| 5 | None of the above | Returns empty list |

### Fallback Detail

When the fallback (rule 4) triggers, the endpoint queries `AccountStatuses` for friends who have had any status change within the last hour (`UpdatedAt >= now - 1h`). This surfaces recently active friends even when nobody is currently online.

## Data Model

### `FriendOverviewItem`

| Field | Type | Description |
|-------|------|-------------|
| `account` | `SnAccount` | Full account object with profile |
| `status` | `SnAccountStatus` | Current status (falls back to default offline status if none set) |
| `activities` | `List<SnPresenceActivity>` | Active presence activities (e.g., gaming, listening) |
