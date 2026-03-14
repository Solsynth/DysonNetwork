# Chat Message Reactions API

## Overview

The Chat Message Reactions API allows users to add, remove, and sync message reactions in chat rooms. Reactions are synced to all room members in real-time via WebSocket.

Reaction totals are persisted on each chat message in `chat_messages.reactions_count` as a JSONB object whose keys are reaction symbols and whose values are counts.

When using with the gateway, the `/api/chat` will be `/messager/chat `as the base path.

## Reaction Types

The following reaction symbols are available by default:

| Symbol | Description |
|--------|-------------|
| `thumb_up` | Thumbs up |
| `thumb_down` | Thumbs down |
| `just_okay` | Just okay |
| `cry` | Crying face |
| `confuse` | Confused face |
| `clap` | Clapping hands |
| `laugh` | Laughing face |
| `angry` | Angry face |
| `party` | Party/celebration |
| `pray` | Praying hands |
| `heart` | Heart/like |

Custom reaction symbols require an active subscription.

## Endpoints

### Add or Toggle Reaction

```
POST /api/chat/{roomId}/messages/{messageId}/reactions
```

#### Authentication

Requires valid authentication token. Include the token in the Authorization header:

```
Authorization: Bearer <token>
```

#### Request Body

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| symbol | string | Yes | The reaction symbol (e.g., "heart", "thumb_up") |
| attitude | int | Yes | Reaction attitude: 0 = Positive, 1 = Neutral, 2 = Negative |

Example:
```json
{
  "symbol": "heart",
  "attitude": 0
}
```

#### Response

Returns the created `SnChatReaction` object:

```json
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "message_id": "660e8400-e29b-41d4-a716-446655440001",
  "sender_id": "770e8400-e29b-41d4-a716-446655440002",
  "symbol": "heart",
  "attitude": 0,
  "created_at": "2024-02-02T10:00:00Z",
  "updated_at": "2024-02-02T10:00:00Z"
}
```

#### Toggle Behavior

If the user has already added the same reaction symbol to the message, calling this endpoint will **remove** the existing reaction (toggle behavior). In this case, the response will be `204 No Content`.

#### Error Responses

| Status Code | Description |
|-------------|-------------|
| 401 Unauthorized | Invalid or missing authentication token |
| 403 Forbidden | User is not a member of the chat room |
| 404 Not Found | Message not found in the specified room |
| 400 Bad Request | Custom reaction symbol without subscription |

---

### Remove Reaction

```
DELETE /api/chat/{roomId}/messages/{messageId}/reactions/{symbol}
```

#### Authentication

Requires valid authentication token.

#### Response

Returns `204 No Content` on success.

#### Error Responses

| Status Code | Description |
|-------------|-------------|
| 401 Unauthorized | Invalid or missing authentication token |
| 403 Forbidden | User is not a member of the chat room |
| 404 Not Found | Message not found |

---

## Real-time Sync

When a reaction is added or removed, all room members receive a WebSocket notification with the following packet types:

### Reaction Added

```
Type: messages.reaction.added
```

Example payload:
```json
{
  "id": "880e8400-e29b-41d4-a716-446655440008",
  "type": "messages.reaction.added",
  "chat_room_id": "660e8400-e29b-41d4-a716-446655440001",
  "sender_id": "770e8400-e29b-41d4-a716-446655440002",
  "nonce": "abc123xyz",
  "meta": {
    "message_id": "660e8400-e29b-41d4-a716-446655440001",
    "symbol": "heart",
    "reaction": {
      "id": "550e8400-e29b-41d4-a716-446655440000",
      "symbol": "heart",
      "attitude": 0,
      "message_id": "660e8400-e29b-41d4-a716-446655440001",
      "sender_id": "770e8400-e29b-41d4-a716-446655440002"
    },
    "reactions_count": {
      "heart": 3,
      "thumb_up": 1
    }
  },
  "created_at": "2024-02-02T10:00:00Z",
  "updated_at": "2024-02-02T10:00:00Z"
}
```

### Reaction Removed

```
Type: messages.reaction.removed
```

Example payload:
```json
{
  "id": "880e8400-e29b-41d4-a716-446655440009",
  "type": "messages.reaction.removed",
  "chat_room_id": "660e8400-e29b-41d4-a716-446655440001",
  "sender_id": "770e8400-e29b-41d4-a716-446655440002",
  "nonce": "abc123xyz",
  "meta": {
    "message_id": "660e8400-e29b-41d4-a716-446655440001",
    "symbol": "heart",
    "reactions_count": {
      "thumb_up": 1
    }
  },
  "created_at": "2024-02-02T10:00:00Z",
  "updated_at": "2024-02-02T10:00:00Z"
}
```

## Persistence Model

- `chat_reactions` remains the source of truth for individual user reactions.
- `chat_messages.reactions_count` is a JSONB summary cache stored on the message row itself.
- Reaction add/remove operations update both the reaction row set and the message summary in the same write flow.
- Existing messages are backfilled by migration `20260314145001_AddChatMessageReactionsCount`.

## Message Reactions Data

Messages include reaction data in the following fields:

| Field | Type | Description |
|-------|------|-------------|
| reactions_count | dict | Dictionary mapping symbol to count (e.g., `{"heart": 3, "thumb_up": 1}`) |
| reactions_made | dict? | Dictionary of symbols the current user has reacted with (only when querying with user context) |

Example message with reactions:
```json
{
  "id": "660e8400-e29b-41d4-a716-446655440001",
  "content": "Hello world",
  "reactions_count": {
    "heart": 3,
    "thumb_up": 1
  },
  "reactions_made": {
    "heart": true
  },
  "sender_id": "770e8400-e29b-41d4-a716-446655440002",
  "chat_room_id": "660e8400-e29b-41d4-a716-446655440001"
}
```

`reactions_count` is read directly from the message record. `reactions_made` is still hydrated per requesting user from `chat_reactions`.

## Sync Integration

Reaction changes are also stored as sync messages in the chat room. When syncing messages using the `/api/chat/sync` global sync endpoint, reaction changes will be included as special message types:

- `messages.reaction.added` - Reaction was added
- `messages.reaction.removed` - Reaction was removed

The sync message metadata now already includes the updated `reactions_count` snapshot, so clients do not need a second enrichment pass to compute totals.

Recommended client behavior:

- Use the sync message `meta.message_id` to locate the target message.
- Replace the local `reactions_count` with `meta.reactions_count`.
- Apply user-specific toggles from the current user context or refreshed message payloads as needed.
