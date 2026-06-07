# Chat Placeholder Messages

This document describes the placeholder message system in `DysonNetwork.Messager`, which supports **bot streaming output** and **user file upload progress**.

When using with the gateway, `/api/chat` is exposed as `/messager/chat`.

## Overview

A placeholder message is a persisted chat message with `type = "placeholder"`. It serves as a live indicator in the chat while an operation is in progress:

- **Streaming** (`kind = "streaming"`) — a bot or user accumulates text chunks into a visible message as content is generated.
- **Uploading** (`kind = "uploading"`) — a user uploads a file; the placeholder shows upload progress, then converts to a real file message.

### Key Properties

| Constraint | Value |
|---|---|
| Max active placeholders | 1 per member per room |
| Default TTL | 5 minutes |
| Persistence | Yes (sync-compatible) |
| Push notifications | No (WebSocket delivery only) |
| Allowed in E2EE rooms | No |

### Lifecycle

```
CREATE ──→ UPDATE (N times) ──→ FINALIZE ──→ (becomes "text" message)
   │                                       
   └──→ EXPIRE (TTL exceeded, auto-deleted)
```

---

## REST API

### Create Placeholder

**Endpoint:** `POST /api/chat/{roomId:guid}/messages/placeholder`  
**Auth:** required (`Bearer`)  
**Permission:** `chat.messages.create`  
**Content-Type:** `application/json`

#### Request Body

| Field | Type | Required | Description |
|---|---|---|---|
| `kind` | string | Yes | `"streaming"` or `"uploading"` |

#### Example

```bash
curl -X POST "https://api.example.com/api/chat/{roomId}/messages/placeholder" \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"kind": "streaming"}'
```

```ts
const res = await fetch(`/api/chat/${roomId}/messages/placeholder`, {
  method: "POST",
  headers: {
    Authorization: `Bearer ${token}`,
    "Content-Type": "application/json",
  },
  body: JSON.stringify({ kind: "streaming" }),
});
const placeholder = await res.json(); // SnChatMessage
```

#### Response

Returns the created `SnChatMessage` with:

- `type: "placeholder"`
- `meta.placeholder_kind`: `"streaming"` or `"uploading"`
- `meta.placeholder_expires_at`: Unix ms timestamp of TTL
- `meta.placeholder_content`: `""` (streaming kind only)
- `meta.placeholder_progress`: `0.0` (uploading kind only)

#### Behavior

- If the member already has an active placeholder in the room:
  - If it has accumulated content, the old placeholder is **finalized** (converted to a `"text"` message).
  - If it has no content, the old placeholder is **expired** (soft-deleted).
- The new placeholder is then created and delivered via WebSocket.
- The placeholder message is broadcast to all room members as a `messages.new` packet.

#### Error Cases

- `400` invalid `kind` (must be `"streaming"` or `"uploading"`)
- `401` unauthenticated
- `403` not a chat member / timed out
- `409` E2EE rooms do not support placeholders

---

## WebSocket Packets

All WebSocket packets are sent through Ring WebSocket with `endpoint = "DysonNetwork.Messager"`.

### Update Placeholder

**Packet type:** `messages.placeholder.update`  
**Direction:** Client → Server

Updates the content or progress of an active placeholder.

#### Request Shape

| Field | Type | Required | Description |
|---|---|---|---|
| `message_id` | guid | Yes | The placeholder message ID |
| `content_chunk` | string | No | Text to append (streaming kind) |
| `progress` | double | No | Upload progress 0.0–1.0 (uploading kind) |

For `kind = "streaming"`: send `content_chunk` to append text. The server accumulates chunks into `meta.placeholder_content`.

For `kind = "uploading"`: send `progress` to update the upload percentage.

You may send `content_chunk` and `progress` together if the placeholder kind supports both (though typically only one is relevant).

#### Example — Streaming Text Chunks

```json
{
  "type": "messages.placeholder.update",
  "endpoint": "DysonNetwork.Messager",
  "data": {
    "message_id": "placeholder-guid",
    "content_chunk": "Hello, I can help "
  }
}
```

```json
{
  "type": "messages.placeholder.update",
  "endpoint": "DysonNetwork.Messager",
  "data": {
    "message_id": "placeholder-guid",
    "content_chunk": "you with that question."
  }
}
```

After these two updates, `meta.placeholder_content` = `"Hello, I can help you with that question."`.

#### Example — Upload Progress

```json
{
  "type": "messages.placeholder.update",
  "endpoint": "DysonNetwork.Messager",
  "data": {
    "message_id": "placeholder-guid",
    "progress": 0.42
  }
}
```

#### Broadcast

The update is broadcast to all room members as a `messages.placeholder.update` packet containing the full updated `SnChatMessage`.

#### Error Responses

Sent to the sender device as an `error` packet:

- `"messages.placeholder.update requires request payload."`
- `"messages.placeholder.update requires a valid message_id."`
- `"Placeholder message not found."`
- `"You can only update your own placeholders."`
- `"Cannot update an expired or finalized placeholder."`

---

### Finalize Placeholder

**Packet type:** `messages.placeholder.finalize`  
**Direction:** Client → Server

Converts the placeholder into a real `"text"` message.

#### Request Shape

| Field | Type | Required | Description |
|---|---|---|---|
| `message_id` | guid | Yes | The placeholder message ID |
| `content` | string | No | Final content (defaults to accumulated `placeholder_content`) |
| `attachments_id` | list\<string\> | No | File IDs to attach to the final message |

#### Example — Streaming Finalization

```json
{
  "type": "messages.placeholder.finalize",
  "endpoint": "DysonNetwork.Messager",
  "data": {
    "message_id": "placeholder-guid"
  }
}
```

This uses the accumulated `meta.placeholder_content` as the final message content.

#### Example — Upload Finalization

```json
{
  "type": "messages.placeholder.finalize",
  "endpoint": "DysonNetwork.Messager",
  "data": {
    "message_id": "placeholder-guid",
    "content": "Here is the document I mentioned.",
    "attachments_id": ["file-guid-1", "file-guid-2"]
  }
}
```

#### Behavior

1. The placeholder's `type` changes from `"placeholder"` to `"text"`.
2. `content` is set to the provided value, or falls back to accumulated `placeholder_content`.
3. Attachments are resolved and attached.
4. All `placeholder_*` keys are removed from `meta`.
5. A `messages.update` sync message is created and delivered to all room members.
6. A confirmation `messages.placeholder.finalize` packet is sent to the sender device.

#### Broadcast

- **To all room members:** a `messages.update` sync message containing the finalized message.
- **To sender device:** a `messages.placeholder.finalize` packet containing the updated `SnChatMessage`.

#### Error Responses

- `"messages.placeholder.finalize requires request payload."`
- `"messages.placeholder.finalize requires a valid message_id."`
- `"Placeholder message not found."`
- `"You can only finalize your own placeholders."`
- `"Cannot finalize an expired or already-finalized placeholder."`

---

### Placeholder Expired (Server → Client)

**Packet type:** `messages.placeholder.expired`  
**Direction:** Server → Client

Sent to all room members when a placeholder's TTL expires and it is cleaned up by the background job.

#### Payload

```json
{
  "message_id": "placeholder-guid",
  "chat_room_id": "room-guid"
}
```

Clients should remove the placeholder from their local message list upon receiving this packet.

---

## Meta Fields

All placeholder-specific data is stored in the message's `meta` JSONB column:

| Key | Type | Kind | Description |
|---|---|---|---|
| `placeholder_kind` | string | both | `"streaming"` or `"uploading"` |
| `placeholder_content` | string | streaming | Accumulated text chunks |
| `placeholder_progress` | double | uploading | Upload progress (0.0–1.0) |
| `placeholder_expires_at` | long | both | Unix ms timestamp when the placeholder auto-expires |

These keys are automatically removed when the placeholder is finalized.

---

## Database Constraints

A partial unique index ensures at most one active placeholder per member per room:

```sql
CREATE UNIQUE INDEX ix_chat_messages_chat_room_id_sender_id
ON chat_messages (chat_room_id, sender_id)
WHERE type = 'placeholder' AND deleted_at IS NULL;
```

---

## Background Cleanup

A Quartz job (`PlaceholderExpirationJob`) runs every minute and soft-deletes any placeholder whose `placeholder_expires_at` has passed. The default TTL is 5 minutes from creation or last update.

Each `messages.placeholder.update` call refreshes the TTL.

---

## Client Integration Guide

### Bot Streaming Flow

```
1. POST /api/chat/{roomId}/messages/placeholder  { kind: "streaming" }
   → receives placeholder message with message_id

2. For each text chunk from the bot:
   WS: messages.placeholder.update  { message_id, content_chunk: "..." }

3. When streaming is complete:
   WS: messages.placeholder.finalize  { message_id }
   → placeholder becomes a real "text" message
```

### File Upload Flow

```
1. POST /api/chat/{roomId}/messages/placeholder  { kind: "uploading" }
   → receives placeholder message with message_id

2. Upload file to drive service, sending progress updates:
   WS: messages.placeholder.update  { message_id, progress: 0.5 }

3. When upload completes:
   WS: messages.placeholder.finalize  { message_id, content: "caption", attachments_id: ["..."] }
   → placeholder becomes a real "text" message with attachments
```

### Error Recovery

- If the WebSocket connection drops during streaming, the placeholder persists on the server. Reconnect and resume sending updates.
- If the client never finalizes, the placeholder expires after 5 minutes of inactivity and is cleaned up.
- If the client sends an update after the placeholder has expired, the server returns an error. Create a new placeholder and retry.
