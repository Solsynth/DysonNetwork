# Realtime Call API

This document describes the Realtime Call API for voice/video calls in DysonNetwork.
The current implementation uses LiveKit as the underlying provider.

## Overview

Realtime calls now follow a **long-lived call concept**:

- Calls are auto-provisioned on demand
- Clients do not need a strict start/end lifecycle before joining
- LiveKit room lifecycle is managed by LiveKit (auto-disposed when empty)
- Server ensures the provider room exists before issuing join tokens

The API provides endpoints for:

- Ensuring/getting call context
- Joining calls with provider token
- Polling participants
- Participant moderation (kick/mute/unmute)
- Leaving a call

**Note:** Webhooks are not used for participant sync. Clients should poll participants.

## Base URL

```
/api/chat/realtime
```

## Authentication

All endpoints require a valid Bearer token in the `Authorization` header.

## Endpoints

### 1. Get Call Context

Get call information for a chat room.

**Endpoint:** `GET /{roomId:guid}`

**Behavior:**

- Verifies caller is a joined chat member
- Ensures a call record/session exists (auto-create if missing)
- Returns `SnRealtimeCall`

**Response:** `SnRealtimeCall`

```json
{
  "id": "uuid",
  "roomId": "uuid",
  "senderId": "uuid",
  "sessionId": "Call_xxx",
  "providerName": "LiveKit",
  "endedAt": null,
  "createdAt": "2024-01-01T00:00:00Z"
}
```

---

### 2. Join Call

Join call and get authentication token.

**Endpoint:** `GET /{roomId:guid}/join`

**Query:** `tool` (optional boolean)

**Behavior:**

- Verifies caller is joined member and not timed out
- Ensures call/session exists
- Ensures LiveKit room exists before generating token
- Returns current participants snapshot

**Response:** `JoinCallResponse`

```json
{
  "provider": "LiveKit",
  "endpoint": "wss://livekit.example.com",
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "callId": "uuid",
  "roomName": "Call_xxx",
  "isAdmin": true,
  "participants": [
    {
      "identity": "username",
      "name": "Display Name",
      "accountId": "uuid",
      "joinedAt": "2024-01-01T00:00:00Z",
      "trackSid": "TR_xxx",
      "profile": {
        "id": "uuid",
        "nick": "nickname",
        "joinedAt": "2024-01-01T00:00:00Z"
      }
    }
  ]
}
```

`isAdmin` is true for:

- Room owner
- Direct-message rooms (participants treated as admin)

---

### 3. Get Participants

Get current participants in the provider room.

**Endpoint:** `GET /{roomId:guid}/participants`

**Behavior:**

- Verifies caller is joined member
- Ensures call/session exists
- Syncs participant list from LiveKit

**Response:** `List<CallParticipant>`

```json
[
  {
    "identity": "username",
    "name": "Display Name",
    "accountId": "uuid",
    "joinedAt": "2024-01-01T00:00:00Z",
    "trackSid": "TR_xxx",
    "profile": { "id": "uuid" }
  }
]
```

**Usage:** poll every 5-10 seconds.

---

### 4. Ensure Call (Backward-Compatible Start)

Ensure call/session exists for a room.

**Endpoint:** `POST /{roomId:guid}`

**Behavior change:** this endpoint is now idempotent ensure behavior, not strict "start once" behavior.

**Response:** `SnRealtimeCall`

**Errors:**

- `403` - Not a joined member or member timed out

---

### 5. Leave Call (Backward-Compatible End)

Leave call for current user.

**Endpoint:** `DELETE /{roomId:guid}`

**Behavior change:** this endpoint now removes the current participant from provider room if present. It does not terminate global call state for everyone.

**Response:** `204 No Content`

---

### 6. Kick Participant

Kick a participant from call, optionally apply chat timeout.

**Endpoint:** `POST /{roomId:guid}/kick/{targetAccountId:guid}`

**Request Body:**

```json
{
  "banDurationMinutes": 30,
  "reason": "Violation of community guidelines"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `banDurationMinutes` | int | No | Chat timeout duration in minutes |
| `reason` | string | No | Reason for timeout |

**Response:** `204 No Content`

**Authorization:** room admin/owner only.

**Behavior:**

- Removes participant from LiveKit room when present
- If `banDurationMinutes > 0`, sets member timeout in chat

---

### 7. Mute Participant

Mute a participant's published track.

**Endpoint:** `POST /{roomId:guid}/mute/{targetAccountId:guid}`

**Response:** `204 No Content`

---

### 8. Unmute Participant

Unmute a participant's published track.

**Endpoint:** `POST /{roomId:guid}/unmute/{targetAccountId:guid}`

**Response:** `204 No Content`

---

## System Messages

Call membership emits system messages into chat timeline:

- `system.call.member.joined`
- `system.call.member.left`

See [Chat System Messages](./CHAT_SYSTEM_MESSAGES.md).

## Data Models

### JoinCallResponse

```csharp
public class JoinCallResponse
{
    public string Provider { get; set; }
    public string Endpoint { get; set; }
    public string Token { get; set; }
    public Guid CallId { get; set; }
    public string RoomName { get; set; }
    public bool IsAdmin { get; set; }
    public List<CallParticipant> Participants { get; set; }
}
```

### CallParticipant

```csharp
public class CallParticipant
{
    public string Identity { get; set; }
    public string Name { get; set; }
    public Guid? AccountId { get; set; }
    public DateTime JoinedAt { get; set; }
    public string? TrackSid { get; set; }
    public SnChatMember? Profile { get; set; }
}
```

### KickParticipantRequest

```csharp
public class KickParticipantRequest
{
    public int? BanDurationMinutes { get; set; }
    public string? Reason { get; set; }
}
```

## Client Recommendations

1. Join directly via `GET /join`; do not require explicit start/end buttons for call lifecycle.
2. Poll `/participants` every 5-10 seconds for participant list.
3. Refresh join token before expiry (LiveKit token TTL is 1 hour).
4. Use `isAdmin` to gate moderation controls.
5. Handle `403` (membership/permission), `400` (invalid call/session state), and network errors.

## Related Documentation

- [Chat System Messages](./CHAT_SYSTEM_MESSAGES.md)
- [Presence Activity API](./PRESENCE_ACTIVITY_API.md)
- [LiveKit Documentation](https://docs.livekit.io)
