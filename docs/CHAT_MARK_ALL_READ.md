# Chat Mark All as Read API

## Overview

The Mark All as Read API allows a user to mark all their chat rooms as read in a single request. This updates the `LastReadAt` timestamp on all active chat memberships, resetting the unread count across all rooms.

When using with the gateway, the `/api/chat` will be `/messager/chat` as the base path.

## Endpoints

### Mark All Rooms as Read

```
POST /api/chat/read-all
```

#### Authentication

Requires a valid authentication token.

```
Authorization: Bearer <token>
```

#### Request

No request body required.

#### Response

Returns `200 OK` on success.

```json
{}
```

#### Error Responses

| Status Code | Description |
|-------------|-------------|
| 401 Unauthorized | Invalid or missing authentication token |

## Behavior

- Updates `LastReadAt` to the current instant for all active memberships (`JoinedAt != null` and `LeaveAt == null`) of the authenticated user.
- Only affects memberships belonging to the current user; other users' read states are untouched.
- Rooms the user has left are not affected.

## WebSocket Integration

### Per-Room Read Sync

When a client sends a `messages.read` WebSocket packet for a specific room, the server processes the read and rebroadcasts the event to all of the user's other connected devices (excluding the sender). This keeps read state in sync across multiple clients.

```
Type: messages.read
```

Payload:
```json
{
  "chat_room_id": "550e8400-e29b-41d4-a716-446655440000",
  "account_id": "880e8400-e29b-41d4-a716-446655440003"
}
```

The originating device does not receive this broadcast (it already knows it performed the read).

## Client Integration

After marking rooms as read (via REST or WebSocket), clients should:

1. Clear local unread badge counts for the affected room(s).
2. Optionally refresh the chat summary via `GET /api/chat/summary` to confirm the updated state.

### Example Usage

```bash
curl -X POST https://api.example.com/messager/chat/read-all \
  -H "Authorization: Bearer <token>"
```

## Related Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/chat/summary` | GET | Get unread counts and last messages per room |
| `/api/chat/unread` | GET | Get total unread count across all rooms |
| `/api/chat/{roomId}/sync` | POST | Sync messages for a specific room (also updates read state) |
