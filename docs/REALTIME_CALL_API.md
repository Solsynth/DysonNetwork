# Realtime Call API

This document describes the Realtime Call API for voice/video calls in DysonNetwork. The implementation uses LiveKit as the underlying real-time communication provider.

## Overview

The Realtime Call API provides endpoints for:
- Starting/ending calls
- Joining calls with authentication tokens
- Managing call participants (kick, mute)
- Getting participant information via periodic polling

**Note:** Webhooks have been replaced with periodic GET requests for participant synchronization.

## Base URL

```
/api/chat/realtime
```

## Authentication

All endpoints require a valid Bearer token in the `Authorization` header.

## Endpoints

### 1. Get Ongoing Call

Get information about an ongoing call in a chat room.

**Endpoint:** `GET /{roomId:guid}`

**Response:** `SnRealtimeCall`

```json
{
  "id": "uuid",
  "roomId": "uuid",
  "senderId": "uuid",
  "sessionId": "string",
  "providerName": "LiveKit",
  "endedAt": null,
  "createdAt": "2024-01-01T00:00:00Z"
}
```

---

### 2. Join Call

Join an ongoing call and get authentication token for LiveKit.

**Endpoint:** `GET /{roomId:guid}/join`

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

**Note:** The `isAdmin` field indicates if the user can kick/mute participants. This is true for:
- The room owner
- Direct message conversations (both participants are admins)

---

### 3. Get Participants

Get current participants in a call. This endpoint syncs participants from LiveKit to cache.

**Endpoint:** `GET /{roomId:guid}/participants`

**Response:** `List<CallParticipant>`

```json
[
  {
    "identity": "username",
    "name": "Display Name",
    "accountId": "uuid",
    "joinedAt": "2024-01-01T00:00:00Z",
    "trackSid": "TR_xxx",
    "profile": { ... }
  }
]
```

**Usage:** Poll this endpoint periodically (e.g., every 5-10 seconds) to get updated participant list instead of relying on webhooks.

---

### 4. Start Call

Start a new call in a chat room.

**Endpoint:** `POST /{roomId:guid}`

**Response:** `SnRealtimeCall`

**Errors:**
- `403` - Not a member or timed out
- `423` - Call already in progress

---

### 5. End Call

End an ongoing call.

**Endpoint:** `DELETE /{roomId:guid}`

**Response:** `204 No Content`

---

### 6. Kick Participant

Kick a participant from the call. Optionally ban them from the chat room.

**Endpoint:** `POST /{roomId:guid}/kick/{targetAccountId:guid}`

**Request Body:**

```json
{
  "banDurationMinutes": 30,
  "reason": "Violation of community guidelines"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `banDurationMinutes` | int | No | Duration to ban from chat (0 or null = no ban) |
| `reason` | string | No | Reason for kick/ban |

**Response:** `204 No Content`

**Authorization:** Only room owner/admin can kick participants.

**Behavior:**
- Removes participant from LiveKit room
- If `banDurationMinutes > 0`, sets `TimeoutUntil` on the member to prevent joining

---

### 7. Mute Participant

Mute a participant's audio track.

**Endpoint:** `POST /{roomId:guid}/mute/{targetAccountId:guid}`

**Response:** `204 No Content`

---

### 8. Unmute Participant

Unmute a participant's audio track.

**Endpoint:** `POST /{roomId:guid}/unmute/{targetAccountId:guid}`

**Response:** `204 No Content`

---

## Data Models

### JoinCallResponse

```csharp
public class JoinCallResponse
{
    public string Provider { get; set; }      // e.g., "LiveKit"
    public string Endpoint { get; set; }     // LiveKit WebSocket endpoint
    public string Token { get; set; }        // JWT token for authentication
    public Guid CallId { get; set; }         // Call identifier
    public string RoomName { get; set; }     // LiveKit room name
    public bool IsAdmin { get; set; }         // Whether user can manage participants
    public List<CallParticipant> Participants { get; set; }
}
```

### CallParticipant

```csharp
public class CallParticipant
{
    public string Identity { get; set; }     // LiveKit identity (username)
    public string Name { get; set; }         // Display name
    public Guid? AccountId { get; set; }     // DysonNetwork account ID
    public DateTime JoinedAt { get; set; }   // When participant joined
    public string? TrackSid { get; set; }    // Track SID for muting
    public SnChatMember? Profile { get; set; } // Chat member profile
}
```

### KickParticipantRequest

```csharp
public class KickParticipantRequest
{
    public int? BanDurationMinutes { get; set; }  // Ban duration in minutes
    public string? Reason { get; set; }           // Reason for kick/ban
}
```

---

## Client Implementation Guide

### Joining a Call

```typescript
interface CallJoinResponse {
  provider: string;
  endpoint: string;
  token: string;
  callId: string;
  roomName: string;
  isAdmin: boolean;
  participants: CallParticipant[];
}

async function joinCall(roomId: string, authToken: string): Promise<CallJoinResponse> {
  const response = await fetch(`/api/chat/realtime/${roomId}/join`, {
    headers: {
      'Authorization': `Bearer ${authToken}`
    }
  });
  
  if (!response.ok) {
    throw new Error('Failed to join call');
  }
  
  return response.json();
}
```

### Polling for Participants

```typescript
interface CallParticipant {
  identity: string;
  name: string;
  accountId: string | null;
  joinedAt: string;
  trackSid: string | null;
}

async function getParticipants(roomId: string, authToken: string): Promise<CallParticipant[]> {
  const response = await fetch(`/api/chat/realtime/${roomId}/participants`, {
    headers: {
      'Authorization': `Bearer ${authToken}`
    }
  });
  
  if (!response.ok) {
    throw new Error('Failed to get participants');
  }
  
  return response.json();
}

// Poll every 5 seconds
setInterval(async () => {
  const participants = await getParticipants(roomId, authToken);
  updateParticipantList(participants);
}, 5000);
```

### Kicking a Participant

```typescript
async function kickParticipant(
  roomId: string, 
  targetAccountId: string, 
  authToken: string,
  options?: { banMinutes?: number; reason?: string }
): Promise<void> {
  const response = await fetch(`/api/chat/realtime/${roomId}/kick/${targetAccountId}`, {
    method: 'POST',
    headers: {
      'Authorization': `Bearer ${authToken}`,
      'Content-Type': 'application/json'
    },
    body: JSON.stringify({
      banDurationMinutes: options?.banMinutes,
      reason: options?.reason
    })
  });
  
  if (!response.ok) {
    throw new Error('Failed to kick participant');
  }
}
```

### Muting a Participant

```typescript
async function muteParticipant(
  roomId: string,
  targetAccountId: string,
  authToken: string
): Promise<void> {
  const response = await fetch(`/api/chat/realtime/${roomId}/mute/${targetAccountId}`, {
    method: 'POST',
    headers: {
      'Authorization': `Bearer ${authToken}`
    }
  });
  
  if (!response.ok) {
    throw new Error('Failed to mute participant');
  }
}
```

---

## Best Practices

1. **Polling Strategy**: Poll `/participants` every 5-10 seconds for accurate participant list. Don't rely on webhooks (they're not used).

2. **Token Refresh**: LiveKit tokens expire after 1 hour. Re-fetch the join endpoint to get a new token.

3. **Permission Checks**: Only show kick/mute buttons for admin users (`isAdmin: true` from join response).

4. **Track Handling**: Use `trackSid` from participant data when calling mute/unmute endpoints. Note that `trackSid` may be null if the participant hasn't published any tracks.

5. **Error Handling**: Handle 403 (not authorized), 404 (no ongoing call), and network errors gracefully.

6. **Reconnection**: Implement reconnection logic - if the call ends (404 from participants), show appropriate UI.

---

## Migration from Webhooks

The previous implementation used LiveKit webhooks for participant updates. This has been replaced with periodic polling:

| Before | After |
|--------|-------|
| Webhook endpoint receives events | Client polls GET /participants |
| Real-time updates via webhook | Polling every 5-10 seconds |
| Server-side participant tracking | Client fetches on join and polls |

**Migration Steps:**
1. Remove webhook receiver code
2. Implement polling in client
3. Call `/participants` on call join
4. Set up interval to poll `/participants`

---

## Related Documentation

- [Chat API](./CHAT_API.md)
- [Presence Activity API](./PRESENCE_ACTIVITY_API.md)
- [LiveKit Documentation](https://docs.livekit.io)
