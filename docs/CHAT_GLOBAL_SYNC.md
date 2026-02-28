# Chat Global Sync API

## Overview

The global sync endpoint allows clients to synchronize their local database with the remote database across all chat rooms the user is a member of. This is useful for offline-first clients that need to keep local data in sync with the server.

## Endpoint

```
POST /messager/chat/sync
```

## Authentication

Requires valid authentication token. Include the token in the Authorization header:

```
Authorization: Bearer <token>
```

## Request Body

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| last_sync_timestamp | int64 | Yes | Unix timestamp in milliseconds. The client should save this value after each successful sync and send it back on the next sync request. |

Example:
```json
{
  "last_sync_timestamp": 1706745600000
}
```

## Response

| Field | Type | Description |
|-------|------|-------------|
| messages | list | Flat list of chat messages from all rooms the user is a member of, sorted by creation time |
| current_timestamp | int64 | Unix timestamp in milliseconds. The client should save this value for the next sync request |
| total_count | int64 | Total number of messages returned |

Example:
```json
{
  "messages": [
    {
      "id": "550e8400-e29b-41d4-a716-446655440000",
      "content": "Hello world",
      "chat_room_id": "660e8400-e29b-41d4-a716-446655440001",
      "sender_id": "770e8400-e29b-41d4-a716-446655440002",
      "created_at": "2024-02-02T10:00:00Z",
      "type": "text",
      "nonce": "abc123",
      "attachments": [],
      "meta": {}
    }
  ],
  "current_timestamp": 1706832000000,
  "total_count": 1
}
```

## Usage

1. On initial sync, send `last_sync_timestamp` as `0`
2. After each successful sync, save the `current_timestamp` value
3. On subsequent syncs, send the saved timestamp
4. The server returns all messages created after the provided timestamp across all chat rooms

## Error Responses

| Status Code | Description |
|-------------|-------------|
| 401 Unauthorized | Invalid or missing authentication token |
| 400 Bad Request | Invalid request body (e.g., missing last_sync_timestamp) |

## Notes

- Messages are sorted by `created_at` in ascending order
- The sync includes all chat rooms where the user is a member (where `joined_at` is not null and `leave_at` is null)
- Each message in the response already contains its `chat_room_id`, allowing the client to organize messages by room
- The limit per room is 500 messages; if a room has more than 500 new messages, only the most recent 500 will be returned
- E2EE messages are returned with encrypted payload fields (`is_encrypted`, `ciphertext`, `encryption_*`) and should be decrypted client-side
