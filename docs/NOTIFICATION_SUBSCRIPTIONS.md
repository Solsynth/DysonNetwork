# Notification Subscriptions

API endpoints for managing push notification subscriptions across all providers.

## Supported Providers

| Provider | Enum Value | Description |
|----------|------------|-------------|
| FCM | `0` | Firebase Cloud Messaging (Android) |
| APNS | `1` | Apple Push Notification Service (iOS) |
| SOP | `2` | Solar Network Push (server-generated token, SSE) |
| UnifiedPush | `3` | Decentralized push (self-hosted endpoints) |

## Endpoints

All endpoints require authentication via `Authorization: Bearer <token>` unless noted.

### Register Push Subscription

Register or update a push subscription for the current device session. SOP subscriptions must use the SOP-specific endpoint instead.

```
PUT /api/notifications/subscription
```

**Query Parameters:**

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `force` | bool | `false` | Force registration even if SOP is already active on this device |

**Request Body:**

```json
{
  "device_token": "<platform-specific-token>",
  "provider": 0
}
```

- `device_token`: FCM registration token, APNS device token, or UnifiedPush endpoint URL
- `provider`: `0` (FCM), `1` (APNS), or `3` (UnifiedPush)
- For UnifiedPush, `device_token` must be a valid absolute HTTP(S) URL (not localhost)

**Behavior:**

- If SOP is already registered on the current device and `force=false`, returns `200 OK` with the existing SOP subscription (no-op)
- If `force=true`, proceeds with the non-SOP registration
- Deactivates other active subscriptions on the same device before registering

**Response:** `SnNotificationPushSubscription`

---

### Register SOP Subscription

Register an SOP subscription for the current session. Returns a server-generated token.

```
POST /api/notifications/sop/subscription
```

**Auth:** Bearer token

**Response:**

```json
{
  "token": "2ff9abc...",
  "subscription": {
    "id": "5c6ec8f6-...",
    "account_id": "f8f6f6d3-...",
    "device_id": "<session-client-id>:sop",
    "device_token": "2ff9abc...",
    "provider": 2,
    "is_activated": true,
    "created_at": "2026-03-09T05:40:00Z",
    "updated_at": "2026-03-09T05:40:00Z"
  }
}
```

The returned `token` is used to authenticate SOP list/stream requests. See [SOP Push API](SOP_PUSH_API.md) for details.

---

### List All Subscriptions

List all push subscriptions for the current account (all providers).

```
GET /api/notifications/subscription
```

**Response:** `SnNotificationPushSubscription[]`

---

### Get Current Device Subscription

Get the active subscription for the current device session.

```
GET /api/notifications/subscription/current
```

**Selection rule:**

1. If SOP is registered on this device, return it (SOP has priority)
2. Otherwise, return the most recently updated subscription for this device

**Response:** `SnNotificationPushSubscription | null`

---

### Unsubscribe (Delete Subscription)

Delete a specific subscription by ID. Only subscriptions belonging to the current account can be deleted.

```
DELETE /api/notifications/subscription/{subscription_id}
```

**Response:** Number of deleted rows (integer)

This works for all providers including SOP. To cancel an SOP subscription:

1. Call `GET /api/notifications/subscription` to find the SOP subscription ID
2. Call `DELETE /api/notifications/subscription/{id}` with that ID

---

## Data Model

### SnNotificationPushSubscription

```json
{
  "id": "uuid",
  "account_id": "uuid",
  "device_id": "string",
  "device_token": "string",
  "provider": 0,
  "is_activated": true,
  "last_used_at": "2026-03-09T05:40:00Z",
  "created_at": "2026-03-09T05:40:00Z",
  "updated_at": "2026-03-09T05:40:00Z"
}
```

| Field | Description |
|-------|-------------|
| `device_id` | Client session ID (or `<session-id>:sop` for SOP) |
| `device_token` | Platform token or SOP bearer token |
| `provider` | Provider enum value |
| `is_activated` | Whether this subscription is the active one for its device |

---

## SOP Token Transport

For SOP-authenticated endpoints (`/api/notifications/sop` and `/api/notifications/sop/stream`), the token can be provided via:

| Method | Example |
|--------|---------|
| Query param | `?token=<sop_token>` |
| Header | `X-SOP-Token: <sop_token>` |
| Authorization | `Authorization: SOP <sop_token>` |

---

## Examples

### Register FCM subscription

```bash
curl -X PUT "/api/notifications/subscription" \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"device_token": "fcm_token_here", "provider": 0}'
```

### Register SOP subscription

```bash
curl -X POST "/api/notifications/sop/subscription" \
  -H "Authorization: Bearer <token>"
```

### List all subscriptions

```bash
curl "/api/notifications/subscription" \
  -H "Authorization: Bearer <token>"
```

### Cancel SOP subscription

```bash
# 1. Find the SOP subscription ID
curl "/api/notifications/subscription" \
  -H "Authorization: Bearer <token>"

# 2. Delete it
curl -X DELETE "/api/notifications/subscription/5c6ec8f6-..." \
  -H "Authorization: Bearer <token>"
```

---

## Related

- [SOP Push API](SOP_PUSH_API.md) - SOP-specific list/stream endpoints
- [Notification Preferences](NOTIFICATION_PREFERENCES.md) - Per-topic delivery control
