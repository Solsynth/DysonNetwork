# SOP Push API (Solar Network Push)

This document describes the SOP push provider in Ring.

## Overview

SOP is a Ring-native push provider that uses:

- server-generated SOP token (registration API)
- notification list API (read without auto-mark)
- SSE stream API (receive new notifications in real time)

Unlike Apple/Google push providers, SOP does not require the client to provide a device push token.

## Provider Name

`PushProvider.Sop`

## Endpoints

### 1. Register SOP Token

Registers/updates an SOP subscription for the current authenticated session and returns a server-generated SOP token.

- **Method:** `POST`
- **Path:** `/api/notifications/sop/subscription`
- **Auth:** Bearer auth (normal Dyson auth)

#### Response Example

```json
{
  "token": "2ff9...",
  "subscription": {
    "id": "5c6ec8f6-3bc1-4de7-8ed6-9f9268ef642f",
    "account_id": "f8f6f6d3-2f64-44e8-b377-66f8935a54a7",
    "device_id": "<session-client-id>:sop",
    "device_token": "2ff9...",
    "provider": 2,
    "created_at": "2026-03-09T05:40:00Z",
    "updated_at": "2026-03-09T05:40:00Z"
  }
}
```

## 2. List Notifications via SOP Token

Lists notifications for the account bound to the SOP token.

Important behavior: this endpoint does **not** mark notifications as viewed.

- **Method:** `GET`
- **Path:** `/api/notifications/sop`
- **Auth:** SOP token (see token transport below)
- **Query:**
  - `offset` (default `0`)
  - `take` (default `8`)

#### Response Headers

- `X-Total`: total notification count for this account

#### Token Transport

One of the following:

- query: `?token=<sop_token>`
- header: `X-SOP-Token: <sop_token>`
- header: `Authorization: SOP <sop_token>`

#### Example

```bash
curl "https://ring.example.com/api/notifications/sop?offset=0&take=20&token=<sop_token>"
```

## 3. Stream Notifications via SOP Token (SSE)

Opens an SSE stream for real-time notifications.

- **Method:** `GET`
- **Path:** `/api/notifications/sop/stream`
- **Auth:** SOP token (same transport options)
- **Content-Type:** `text/event-stream`

### SSE Events

#### Ready Event

Sent after the stream is established.

```text
event: ready
data: {"status":"connected"}
```

#### Notification Event

Sent for each new notification delivered to the account.

```text
event: notification
data: { ...sn_notification_json... }
```

### Browser Example

```javascript
const token = "<sop_token>";
const es = new EventSource(`/api/notifications/sop/stream?token=${encodeURIComponent(token)}`);

es.addEventListener("ready", (evt) => {
  console.log("SOP stream ready", evt.data);
});

es.addEventListener("notification", (evt) => {
  const notification = JSON.parse(evt.data);
  console.log("new notification", notification);
});

es.onerror = (err) => {
  console.error("SOP stream error", err);
};
```

## Integration Flow

1. Authenticate user with normal Dyson auth.
2. Call `POST /api/notifications/sop/subscription` to get SOP token.
3. Call `GET /api/notifications/sop` for initial list (without marking viewed).
4. Open `GET /api/notifications/sop/stream` via SSE to receive new notifications.

## Notes

- Existing endpoint `PUT /api/notifications/subscription` does not accept `PushProvider.Sop`.
- Use `/api/notifications/sop/subscription` for SOP registration.
- SOP tokens are currently persisted in push subscription storage and are treated as bearer credentials for SOP APIs.

## Push Device Management

The regular notification controller exposes account-scoped push subscription management for all providers.

### List Registered Push Devices

- **Method:** `GET`
- **Path:** `/api/notifications/subscription`
- **Auth:** Bearer auth

Returns all registered push subscriptions for the current account, including SOP subscriptions.

### Unregister by Subscription ID

- **Method:** `DELETE`
- **Path:** `/api/notifications/subscription/{subscription_id}`
- **Auth:** Bearer auth

Deletes the specified subscription if it belongs to the current account.
