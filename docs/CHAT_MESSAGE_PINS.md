# Chat Message Pins API

## Overview

The Chat Message Pins API allows room administrators to pin and unpin messages in chat rooms. Multiple messages can be pinned at once. Pins can optionally have an expiry time.

Pin events are broadcast to all room members in real-time via WebSocket as system messages.

When using with the gateway, the `/api/chat` will be `/messager/chat` as the base path.

## Permissions

Pin and unpin operations require elevated permissions depending on the room type:

| Room Type | Required Permission |
|-----------|---------------------|
| Group (non-realm) | Room owner (`chatRoom.AccountId`) |
| Group (realm-linked) | Realm moderator or above (`RealmMemberRole >= 50`) |
| Direct Message | Any member of the DM |

Listing pins only requires read access to the room (same as listing messages).

## Endpoints

### Pin a Message

```
POST /api/chat/{roomId}/pins
```

#### Authentication

Requires valid authentication token and room membership with appropriate permissions.

```
Authorization: Bearer <token>
```

#### Request Body

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| message_id | guid | Yes | The ID of the message to pin |
| expires_at | instant? | No | Optional expiry timestamp (ISO 8601 / Unix ms). Omit or set to `null` for a non-expiring pin |

Example (non-expiring pin):
```json
{
  "message_id": "660e8400-e29b-41d4-a716-446655440001"
}
```

Example (with expiry):
```json
{
  "message_id": "660e8400-e29b-41d4-a716-446655440001",
  "expires_at": 1735689600000
}
```

#### Response

Returns the created `SnChatMessagePin` object:

```json
{
  "id": "aa0e8400-e29b-41d4-a716-446655440010",
  "message_id": "660e8400-e29b-41d4-a716-446655440001",
  "chat_room_id": "550e8400-e29b-41d4-a716-446655440000",
  "pinned_by_member_id": "770e8400-e29b-41d4-a716-446655440002",
  "expires_at": null,
  "created_at": "2024-02-02T10:00:00Z",
  "updated_at": "2024-02-02T10:00:00Z"
}
```

#### Error Responses

| Status Code | Description |
|-------------|-------------|
| 401 Unauthorized | Invalid or missing authentication token |
| 403 Forbidden | User lacks pin permission (not owner/moderator) or is not a room member |
| 404 Not Found | Room not found |
| 400 Bad Request | Message not found in the room, or message is already pinned |

---

### Unpin a Message

```
DELETE /api/chat/{roomId}/pins/{pinId}
```

#### Authentication

Requires valid authentication token and room membership with appropriate permissions.

```
Authorization: Bearer <token>
```

#### Response

Returns `200 OK` on success.

#### Error Responses

| Status Code | Description |
|-------------|-------------|
| 401 Unauthorized | Invalid or missing authentication token |
| 403 Forbidden | User lacks pin permission or is not a room member |
| 404 Not Found | Room not found |
| 400 Bad Request | Pin not found |

---

### List Pinned Messages

```
GET /api/chat/{roomId}/pins
```

#### Authentication

Authentication is optional for public unencrypted rooms.

Private rooms and encrypted rooms require a valid authentication token and active room membership.

#### Query Parameters

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| include_expired | bool | No | Include expired pins. Default: `false` |

#### Response

Returns a list of pin objects. Each pin includes its associated `message` (with hydrated `sender`) and `pinned_by` member (with hydrated account).

```json
[
  {
    "id": "aa0e8400-e29b-41d4-a716-446655440010",
    "message_id": "660e8400-e29b-41d4-a716-446655440001",
    "chat_room_id": "550e8400-e29b-41d4-a716-446655440000",
    "pinned_by_member_id": "770e8400-e29b-41d4-a716-446655440002",
    "expires_at": null,
    "message": {
      "id": "660e8400-e29b-41d4-a716-446655440001",
      "type": "text",
      "content": "Important announcement!",
      "sender_id": "770e8400-e29b-41d4-a716-446655440002",
      "sender": {
        "id": "770e8400-e29b-41d4-a716-446655440002",
        "account_id": "880e8400-e29b-41d4-a716-446655440003",
        "account": {
          "id": "880e8400-e29b-41d4-a716-446655440003",
          "nick": "Alice"
        }
      },
      "chat_room_id": "550e8400-e29b-41d4-a716-446655440000",
      "created_at": "2024-02-02T09:00:00Z"
    },
    "pinned_by": {
      "id": "770e8400-e29b-41d4-a716-446655440002",
      "account_id": "880e8400-e29b-41d4-a716-446655440003",
      "account": {
        "id": "880e8400-e29b-41d4-a716-446655440003",
        "nick": "Alice"
      }
    },
    "created_at": "2024-02-02T10:00:00Z",
    "updated_at": "2024-02-02T10:00:00Z"
  }
]
```

Results are ordered by `created_at` descending (newest pins first).

#### Error Responses

| Status Code | Description |
|-------------|-------------|
| 401 Unauthorized | Missing authentication for a private or encrypted room |
| 403 Forbidden | User is not a member of a private or encrypted room |
| 404 Not Found | Room not found |

---

## Real-time Sync

When a message is pinned or unpinned, all room members receive a WebSocket notification as a system message.

### Message Pinned

```
Type: messages.pinned
```

Example payload:
```json
{
  "id": "990e8400-e29b-41d4-a716-446655440020",
  "type": "messages.pinned",
  "chat_room_id": "550e8400-e29b-41d4-a716-446655440000",
  "sender_id": "770e8400-e29b-41d4-a716-446655440002",
  "nonce": "abc123xyz",
  "meta": {
    "pin_id": "aa0e8400-e29b-41d4-a716-446655440010",
    "message_id": "660e8400-e29b-41d4-a716-446655440001",
    "pinned_by_member_id": "770e8400-e29b-41d4-a716-446655440002",
    "expires_at": 1735689600000
  },
  "created_at": "2024-02-02T10:00:00Z",
  "updated_at": "2024-02-02T10:00:00Z"
}
```

When the pin has no expiry, the `expires_at` field is omitted from the meta.

### Message Unpinned

```
Type: messages.unpinned
```

Example payload:
```json
{
  "id": "990e8400-e29b-41d4-a716-446655440021",
  "type": "messages.unpinned",
  "chat_room_id": "550e8400-e29b-41d4-a716-446655440000",
  "sender_id": "770e8400-e29b-41d4-a716-446655440002",
  "nonce": "abc123xyz",
  "meta": {
    "pin_id": "aa0e8400-e29b-41d4-a716-446655440010",
    "message_id": "660e8400-e29b-41d4-a716-446655440001"
  },
  "created_at": "2024-02-02T10:05:00Z",
  "updated_at": "2024-02-02T10:05:00Z"
}
```

## Sync Integration

Pin changes are stored as system messages in the chat room. When syncing messages using the `/api/chat/sync` global sync endpoint, pin changes will be included as special message types:

- `messages.pinned` - A message was pinned
- `messages.unpinned` - A message was unpinned

Recommended client behavior:

- Use the sync message `meta.message_id` to locate the target message in the local store.
- On `messages.pinned`, fetch the full pin via `GET /api/chat/{roomId}/pins` or insert it locally.
- On `messages.unpinned`, remove the pin from the local pin list using `meta.pin_id`.

## Persistence Model

- `chat_message_pins` is the source of truth for pinned messages.
- Each pin references a `message_id`, `chat_room_id`, and `pinned_by_member_id`.
- A unique constraint on `(chat_room_id, message_id)` prevents duplicate pins.
- Pins use soft delete (`deleted_at`) -- unpinned records are marked as deleted, not removed.
- Pins can be non-expiring (`expires_at = null`) or have an optional expiry timestamp.
- Expired pins are still returned by the API unless the client filters them. Use the `include_expired=true` query parameter to explicitly include expired pins.
