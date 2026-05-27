# Chat Room Sync API

## Overview

The chat room sync endpoint lets clients incrementally update the signed-in user's joined room list without fetching every room on each app launch.

Use this endpoint for room-list state such as joined rooms, removed rooms, direct-message metadata, room names, pictures, realms, and the current user's membership row. Keep using the chat message sync endpoint for message history.

## Endpoint

```
POST /messager/chat/rooms/sync
```

Local development route:

```
POST /api/chat/rooms/sync
```

## Authentication

Requires a valid authentication token:

```
Authorization: Bearer <token>
```

## Request Body

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| last_sync_timestamp | int64 | Yes | Unix timestamp in milliseconds from the previous successful room sync. Use `0` for the initial sync. |

Example:

```json
{
  "last_sync_timestamp": 1706745600000
}
```

## Response

| Field | Type | Description |
|-------|------|-------------|
| changes | list | Room-list changes sorted by `changed_at` ascending. |
| current_timestamp | timestamp | Timestamp to store and send as `last_sync_timestamp` on the next room sync. |
| total_count | int32 | Number of changes returned. |

Each change contains:

| Field | Type | Description |
|-------|------|-------------|
| room_id | uuid | Changed room id. |
| type | string | One of `joined`, `updated`, or `removed`. |
| room | object/null | Hydrated `SnChatRoom` for active changes. Null for removals when the room should be dropped locally. |
| member | object/null | Current user's `SnChatMember` when the change came from membership state. |
| changed_at | timestamp | Effective change timestamp from `created_at`, `updated_at`, or `deleted_at`. |

Example:

```json
{
  "changes": [
    {
      "room_id": "660e8400-e29b-41d4-a716-446655440001",
      "type": "updated",
      "room": {
        "id": "660e8400-e29b-41d4-a716-446655440001",
        "name": "General",
        "type": 0,
        "is_public": false,
        "encryption_mode": 0
      },
      "member": null,
      "changed_at": "2024-02-02T10:00:00Z"
    }
  ],
  "current_timestamp": "2024-02-02T10:00:00Z",
  "total_count": 1
}
```

## Client Behavior

1. Send `last_sync_timestamp` as `0` for the first room sync.
2. Apply `joined` and `updated` changes by upserting `room`.
3. Apply `removed` changes by removing `room_id` from the local joined-room list.
4. Save `current_timestamp` after a successful response.
5. Use the saved timestamp for the next room sync.

## Notes

- This endpoint is separate from `POST /messager/chat/sync`, which remains specialized for chat message synchronization.
- Changes are based on `created_at`, `updated_at`, and `deleted_at` from `SnChatMember` and `SnChatRoom`.
- The current implementation returns up to 500 changes per request.
- `room` is hydrated with realm data and direct-message member data for active rooms.
- Pending invitations with `joined_at = null`, left rooms with `leave_at != null`, and soft-deleted memberships are returned as `removed` for joined-room-list clients.
