# Cross-Device Login Approval

During multi-factor authentication, other logged-in devices can approve or decline the login attempt in real-time via WebSocket. Once approved, the challenge becomes immediately eligible for token exchange.

> **Note:** All endpoints below are prefixed with `/padlock` in production (e.g. `/padlock/auth/challenge/pending`). All request and response bodies use `snake_case` for JSON property names.

## Overview

1. User starts login on Device A — a challenge is created with `step_remain > 0`
2. User completes at least one authentication factor on Device A
3. Server pushes `auth.challenge.pending` to all other devices via WebSocket
4. User opens Device B — fetches pending challenges, taps approve (PIN required)
5. Server sets `step_remain = 0`, pushes `auth.challenge.approved` back to Device A
6. Device A exchanges the challenge for tokens

> **Important:** The `auth.challenge.pending` packet is only sent after the first factor is successfully completed on the initiating device. Challenges where no factors have been verified yet will not trigger cross-device approval.

## WebSocket Packets

### `auth.challenge.pending`

Sent to all user devices (except the one that initiated the login) when a new challenge is created.

```json
{
  "type": "auth.challenge.pending",
  "data": {
    "challenge_id": "550e8400-...",
    "device_name": "iPhone 16",
    "ip_address": "203.0.113.42",
    "platform": "Ios",
    "created_at": "2026-06-04T10:30:00Z"
  }
}
```

### `auth.challenge.approved`

Sent to all user devices (except the approving device) when a challenge is approved.

```json
{
  "type": "auth.challenge.approved",
  "data": {
    "challenge_id": "550e8400-...",
    "approved_by_device": "session-id-uuid"
  }
}
```

The initiating device should call `POST /api/auth/token` with the `challenge_id` as the `code` immediately upon receiving this packet.

### `auth.challenge.declined`

Sent to all user devices (except the declining device) when a challenge is declined.

```json
{
  "type": "auth.challenge.declined",
  "data": {
    "challenge_id": "550e8400-...",
    "declined_by_device": "session-id-uuid"
  }
}
```

The initiating device should show an error and restart the login flow.

---

## Endpoints

All endpoints require authentication (`Authorization: Bearer <token>`) and an interactive session (API keys are rejected).

### List Pending Challenges

```
GET /api/auth/challenge/pending
```

Returns all challenges for the current user that are awaiting approval (not yet approved, not declined, not expired, `step_remain > 0`).

**Response:** `SnAuthChallenge[]`

```json
[
  {
    "id": "550e8400-...",
    "expired_at": "2026-06-04T11:30:00Z",
    "step_remain": 2,
    "step_total": 2,
    "failed_attempts": 0,
    "ip_address": "203.0.113.42",
    "device_name": "MacBook Pro",
    "platform": "MacOs",
    "created_at": "2026-06-04T10:30:00Z"
  }
]
```

---

### Approve Challenge

```
POST /api/auth/challenge/{challenge_id}/approve
```

Approves a pending challenge. Requires sudo mode (PIN verification).

**Request Body:**

```json
{
  "pin_code": "123456"
}
```

- `pin_code` — The user's PIN code. Can be `null` if the account has no PIN configured.

**Behavior:**

- Sets `step_remain = 0` on the challenge
- Records `approved_at` and `approved_by_session_id`
- Sends `auth.challenge.approved` WebSocket packet to other devices
- Sends a push notification to the user

**Response:** `200 OK`

---

### Decline Challenge

```
POST /api/auth/challenge/{challenge_id}/decline
```

Declines a pending challenge. Requires sudo mode (PIN verification).

**Request Body:**

```json
{
  "pin_code": "123456"
}
```

**Behavior:**

- Records `declined_at` on the challenge
- Sends `auth.challenge.declined` WebSocket packet to other devices
- Sends a push notification to the user

**Response:** `200 OK`

---

## Client Implementation Guide

### Device A (Login Initiator)

1. After creating the challenge and starting MFA, listen for WebSocket packets
2. On receiving `auth.challenge.approved`:
   - Call `POST /api/auth/token` with `grant_type: "authorization_code"` and `code: <challenge_id>`.
     The request body uses `grant_type` and `code` as field names.
   - Complete the login flow
3. On receiving `auth.challenge.declined`:
   - Show an error message ("Login was declined from another device")
   - Optionally restart the login flow

### Device B (Approver)

1. Listen for `auth.challenge.pending` WebSocket packets
2. When received, show a notification or in-app prompt with the device info
3. User taps "Approve":
   - Collect PIN if needed
   - Call `POST /api/auth/challenge/{id}/approve` with `{ "pin_code": "..." }`
4. User taps "Decline":
   - Call `POST /api/auth/challenge/{id}/decline` with `{ "pin_code": "..." }`

Alternatively, on app launch, call `GET /api/auth/challenge/pending` to fetch any pending challenges that arrived while the app was closed.

### Session Hierarchy

When a challenge is approved by another device, the resulting login session becomes a **child session** of the approving device's session (`parent_session_id` is set). This is visible in the session management API and can be used for audit trails.

---

## Related

- [Unified JWT Auth](UNIFIED_JWT_AUTH.md) — Authentication flow overview
- [Session Management API](SESSION_MANAGEMENT_API.md) — Managing sessions and devices
- [Notification Subscriptions](NOTIFICATION_SUBSCRIPTIONS.md) — Push notification setup
