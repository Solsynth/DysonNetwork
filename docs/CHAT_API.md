# Chat API

This document describes the ChatController REST API in `DysonNetwork.Messager`, which covers the core message CRUD operations, subscriptions, sync, autocomplete, andMLS device management for chat.

When using through the gateway, `/api/chat` is exposed as `/messager/chat`.

## Base URL

```
/api/chat
```

## Authentication

All endpoints require a valid `Bearer` token except where noted (some read endpoints allow anonymous access to public rooms).

```
Authorization: Bearer <token>
```

---

## Overview of Endpoints

| Category | Endpoints |
|---|---|
| Summary & Unread | `GET /summary`, `GET /unread`, `POST /read-all` |
| Subscriptions | `GET /{roomId}/subscriptions`, `GET /accounts/me/subscriptions`, `GET /accounts/me/status` |
| Messages CRUD & Search | `GET /messages/search`, `GET /{roomId}/messages`, `GET /{roomId}/messages/{messageId}`, `POST /{roomId}/messages`, `PATCH /{roomId}/messages/{messageId}`, `DELETE /{roomId}/messages/{messageId}` |
| Voice Messages | `POST /{roomId}/messages/voice`, `GET /{roomId}/voice/{voiceId}` |
| Reactions | `POST /{roomId}/messages/{messageId}/reactions`, `DELETE /{roomId}/messages/{messageId}/reactions/{symbol}`, `GET /{roomId}/messages/{messageId}/reactions` |
| Placeholders | `POST /{roomId}/messages/placeholder` |
| Redirect | `POST /{roomId}/messages/redirect` |
| Sync | `POST /{roomId}/sync`, `POST /sync` |
| Autocomplete | `POST /{roomId}/autocomplete` |
| MLS Devices | `POST /{roomId}/devices/me/joined` |
| Pins | `POST /{roomId}/pins`, `DELETE /{roomId}/pins/{pinId}`, `GET /{roomId}/pins` |
| Bot Commands | `GET /{roomId}/bots/commands` |

---

## Summary & Unread

### Get Chat Summary

Returns unread message counts and the last message for each room the user is a member of.

```
GET /api/chat/summary
```

**Auth:** Required

**Response:** `Dictionary<Guid, ChatSummaryResponse>`

```json
{
  "room-id-uuid": {
    "unreadCount": 5,
    "lastMessage": { /* SnChatMessage */ }
  }
}
```

### Get Total Unread Count

Returns the sum of unread messages across all rooms.

```
GET /api/chat/unread
```

**Auth:** Required

**Response:** `int`

### Mark All as Read

Marks all rooms as read for the current user. See [CHAT_MARK_ALL_READ](./CHAT_MARK_ALL_READ.md) for details.

```
POST /api/chat/read-all
```

**Auth:** Required  
**Permission:** `chat.read.all`

---

## Subscriptions

### Get Room Subscriptions

Returns subscription entries for all members of a room.

```
GET /api/chat/{roomId}/subscriptions
```

**Auth:** Required — must be a room member.

**Response:** `List<RoomSubscriptionEntry>`

### Get My Account Subscriptions

Returns all room subscriptions for the current user.

```
GET /api/chat/accounts/me/subscriptions
```

**Auth:** Required

**Response:** `List<AccountSubscriptionEntry>`

### Get My Chat Status

Returns the current user's subscription and WebSocket connection status across all rooms and devices.

```
GET /api/chat/accounts/me/status
```

**Auth:** Required

**Response:** `ChatAccountStatusResponse`

```json
{
  "accountId": "uuid",
  "hasActiveSubscriptions": true,
  "hasAnyWebSocketConnection": true,
  "pushNotificationsMaySendForUnsubscribedRooms": true,
  "subscriptions": [
    {
      "roomId": "uuid",
      "memberId": "uuid",
      "room": { /* SnChatRoom */ },
      "isSubscribed": true,
      "pushNotificationsSuppressed": false,
      "devices": [
        {
          "deviceToken": "token",
          "expiresAt": "2024-01-01T00:00:00Z",
          "isWebSocketConnected": true
        }
      ]
    }
  ]
}
```

---

## Messages CRUD

### List Messages

Returns paginated messages in a room, ordered by `room_sequence` descending.

```
GET /api/chat/{roomId}/messages?offset=0&take=20
```

**Auth:** Optional for public rooms. Required for private/E2EE rooms.

**Query Parameters:**

| Param | Type | Default | Description |
|---|---|---|---|
| `offset` | int | 0 | Pagination offset |
| `take` | int | 20 | Page size (max 1000) |

**Headers:**

| Header | Description |
|---|---|
| `X-Total` | Total number of messages in the room |

**Response:** `List<SnChatMessage>`

Each message includes the `Sender` (hydrated `SnChatMember` with account info), `ReactionsCount`, and `ReactionsMade` (if authenticated).

### Get Single Message

Returns a single message by ID.

```
GET /api/chat/{roomId}/messages/{messageId}
```

**Auth:** Optional for public rooms. Required for private/E2EE rooms.

**Response:** `SnChatMessage`

### Send Message

Creates and sends a new message in a room.

```
POST /api/chat/{roomId}/messages
```

**Auth:** Required  
**Permission:** `chat.messages.create`

**Query Parameters:**

| Param | Type | Description |
|---|---|---|
| `identity` | Guid? | Bot ID to send as (requires bot publisher membership) |

**Request Body:** `SendMessageRequest`

| Field | Type | Description |
|---|---|---|
| `content` | string? | Message text content (max 4096 chars) |
| `nonce` | string? | Client-generated nonce for idempotency (max 36 chars) |
| `clientMessageId` | string? | Client-side message ID for dedup (max 128 chars) |
| `fundId` | Guid? | Attach a fund embed |
| `surveyId` | Guid? | Attach a poll/survey embed |
| `meetId` | Guid? | Attach a meet embed |
| `notableDayId` | Guid? | Attach a notable day embed |
| `calendarEventId` | Guid? | Attach a calendar event embed |
| `locationName` | string? | Location name (max 256 chars) |
| `locationAddress` | string? | Location address (max 1024 chars) |
| `locationWkt` | string? | Location as WKT geometry |
| `attachmentsId` | List\<string>? | File attachment IDs |
| `embeds` | List\<Dictionary>? | Custom embeds (if provided, overrides individual embed fields) |
| `meta` | Dictionary? | Arbitrary metadata |
| `repliedMessageId` | Guid? | Message being replied to |
| `forwardedMessageId` | Guid? | Message being forwarded |
| `isEncrypted` | bool | Whether this is an E2EE message |
| `ciphertext` | byte[]? | Encrypted payload |
| `encryptionHeader` | byte[]? | E2EE header |
| `encryptionSignature` | byte[]? | E2EE signature |
| `encryptionScheme` | string? | E2EE scheme (e.g. `chat.mls.v2`) |
| `encryptionEpoch` | long? | MLS epoch number |
| `encryptionMessageType` | string? | MLS message type |

**Behavior:**

- In non-E2EE rooms, `content` and attachments are processed normally.
- In E2EE rooms, only `ciphertext`, `encryptionHeader`, `encryptionSignature`, `encryptionScheme`, and `encryptionEpoch` are accepted — all plaintext fields are rejected.
- For MLS rooms, the epoch must match the current MLS group epoch.
- If `identity` (botId) is provided, the caller must be at least a `Viewer` in the bot's publisher to read, or `Editor` to write.
- Mentions are auto-extracted from `content`.
- If `embeds` is provided directly, it overrides individual embed fields (fund, survey, meet, etc.).

**Errors:**

| Status | Condition |
|---|---|
| 400 | Empty message (non-E2EE), invalid location WKT, invalid reply/forward target |
| 403 | Not a member, timed out, or lacks realm permission |
| 409 | E2EE payload missing, MLS epoch mismatch, MLS group not ready, plaintext in E2EE room |

**Response:** `SnChatMessage` (the persisted message)

> **WebSocket alternative:** Messages can also be sent via WebSocket. See [CHAT_WEBSOCKET_SEND_MESSAGE](./CHAT_WEBSOCKET_SEND_MESSAGE.md).

### Update Message

Edits an existing message. Only the sender can update their own messages.

```
PATCH /api/chat/{roomId}/messages/{messageId}
```

**Auth:** Required  
**Permission:** `chat.messages.update`

**Request Body:** Same as `SendMessageRequest`.

**Behavior:**

- The sender must be the original message author.
- In E2EE rooms, only encrypted payload fields are updated.
- In non-E2EE rooms, embeds are replaced (old embed types are removed before adding new ones).
- Mentions are re-extracted from updated content.

**Response:** `SnChatMessage` (the updated message)

### Delete Message

Deletes a message. Only the sender can delete their own messages.

```
DELETE /api/chat/{roomId}/messages/{messageId}
```

**Auth:** Required  
**Permission:** `chat.messages.delete`

**Request Body:** `DeleteMessageRequest` (optional — for E2EE deletion metadata)

| Field | Type | Description |
|---|---|---|
| `clientMessageId` | string? | Client message ID |
| `ciphertext` | byte[]? | Encrypted deletion confirmation |
| `encryptionHeader` | byte[]? | E2EE header |
| `encryptionSignature` | byte[]? | E2EE signature |
| `encryptionScheme` | string? | E2EE scheme |
| `encryptionEpoch` | long? | MLS epoch number |
| `encryptionMessageType` | string? | MLS message type |

**Errors:**

| Status | Condition |
|---|---|
| 403 | Not the message sender |
| 409 | E2EE/MLS validation failure |

---

## Voice Messages

See [CHAT_VOICE_MESSAGES](./CHAT_VOICE_MESSAGES.md) for full details.

### Send Voice Message

Uploads a voice clip and creates a voice-type message.

```
POST /api/chat/{roomId}/messages/voice
```

**Auth:** Required  
**Permission:** `chat.messages.create`  
**Content-Type:** `multipart/form-data`

**Form Fields:**

| Field | Type | Required | Description |
|---|---|---|---|
| `file` | file | Yes | Audio file |
| `nonce` | string | No | Client nonce |
| `durationMs` | int | No | Duration in milliseconds |
| `repliedMessageId` | Guid | No | Reply target |
| `forwardedMessageId` | Guid | No | Forward target |

**Errors:**

| Status | Condition |
|---|---|
| 403 | E2EE rooms not supported for voice v1 |
| 400 | Invalid audio file |

### Get Voice Clip

Streams the binary audio content for a voice clip.

```
GET /api/chat/{roomId}/voice/{voiceId}
```

**Auth:** Optional for public rooms.

**Response:** Binary audio stream with appropriate `Content-Type`.

---

## Reactions

See [CHAT_MESSAGE_REACTIONS](./CHAT_MESSAGE_REACTIONS.md) for full details.

### Add or Toggle Reaction

Toggles a reaction on a message. If the same reaction already exists from the user, it is removed.

```
POST /api/chat/{roomId}/messages/{messageId}/reactions
```

**Auth:** Required  
**Permission:** `chat.messages.react`

**Request Body:** `MessageReactionRequest`

| Field | Type | Description |
|---|---|---|
| `symbol` | string | Reaction symbol (e.g. `thumb_up`, `heart`) |
| `attitude` | MessageReactionAttitude | `Positive`, `Neutral`, or `Negative` |

**Response:** `SnChatReaction` (200) if added, `204 No Content` if removed.

Custom reaction symbols require an active subscription.

### Remove Reaction

```
DELETE /api/chat/{roomId}/messages/{messageId}/reactions/{symbol}
```

**Auth:** Required  
**Permission:** `chat.messages.react`

**Response:** `204 No Content`

### List Reactions

```
GET /api/chat/{roomId}/messages/{messageId}/reactions?symbol=&offset=0&take=20&order=
```

**Auth:** Optional for public rooms.

**Query Parameters:**

| Param | Type | Description |
|---|---|---|
| `symbol` | string | Filter by symbol |
| `offset` | int | Pagination offset |
| `take` | int | Page size (default 20) |
| `order` | string | `created` for recency, default is by symbol |

**Headers:**

| Header | Description |
|---|---|
| `X-Total` | Total matching reactions |

---

## Placeholder Messages

See [CHAT_PLACEHOLDER_MESSAGES](./CHAT_PLACEHOLDER_MESSAGES.md) for full details.

### Send Placeholder

Creates a placeholder message for streaming output or upload progress.

```
POST /api/chat/{roomId}/messages/placeholder
```

**Auth:** Required  
**Permission:** `chat.messages.create`

**Request Body:** `SendPlaceholderMessageRequest`

| Field | Type | Description |
|---|---|---|
| `kind` | string | `"streaming"` or `"uploading"` |

**Errors:**

| Status | Condition |
|---|---|
| 403 | Not supported in E2EE rooms |
| 400 | Invalid kind value |

---

## Redirect Messages

See [CHAT_REDIRECT_MESSAGES](./CHAT_REDIRECT_MESSAGES.md) for full details.

### Redirect Messages

Copies a section of chat history from one room into another.

```
POST /api/chat/{roomId}/messages/redirect
```

**Auth:** Required  
**Permission:** `chat.messages.create`

**Request Body:** `RedirectMessagesRequest`

| Field | Type | Description |
|---|---|---|
| `messageIds` | List\<Guid> | Messages to redirect (max 100, all from same source room) |

**Constraints:**

- Up to 100 messages per request.
- All messages must be from a single source room.
- Caller must be a member of both source and destination rooms.
- Only non-E2EE text messages are supported.

---

## Sync

### Per-Room Sync

Fetches messages in a specific room since a given timestamp, with support for gap-filling via sequence numbers.

```
POST /api/chat/{roomId}/sync
```

**Auth:** Required

**Request Body:** `SyncRequest`

| Field | Type | Description |
|---|---|---|
| `lastSyncTimestamp` | long | Unix milliseconds. Use `0` for initial sync. |
| `missingSequences` | List\<long>? | Specific missing `room_sequence` values |
| `missingSequenceRanges` | List\<Range>? | Ranges of missing sequences |

`SyncSequenceRangeRequest`:

| Field | Type | Description |
|---|---|---|
| `startSequence` | long | Range start (inclusive) |
| `endSequence` | long | Range end (inclusive) |

**Response:** `SyncResponse`

| Field | Type | Description |
|---|---|---|
| `messages` | List\<SnChatMessage> | Messages since `lastSyncTimestamp` and/or matching missing sequences |
| `currentTimestamp` | Instant | Server timestamp for next sync cursor |
| `totalCount` | int | Total messages returned |

**Headers:**

| Header | Description |
|---|---|
| `X-Total` | Total count |

> See [CHAT_ROOM_SEQUENCE_SYNC](./CHAT_ROOM_SEQUENCE_SYNC.md) for details on `room_sequence` gap recovery.

### Global Sync

Fetches messages across all rooms the user is a member of since a given timestamp.

```
POST /api/chat/sync
```

**Auth:** Required  
**Permission:** `chat.sync`

**Request Body:** `SyncRequest` (uses `lastSyncTimestamp` only)

**Response:** `GlobalSyncResponse`

| Field | Type | Description |
|---|---|---|
| `messages` | List\<SnChatMessage> | Messages from all rooms, sorted by `createdAt` ascending (max 500) |
| `currentTimestamp` | Instant | Server timestamp for next sync cursor |
| `totalCount` | int | Total messages returned |

> See [CHAT_GLOBAL_SYNC](./CHAT_GLOBAL_SYNC.md) for details.

---

## Autocomplete

Returns autocomplete suggestions for the current chat input. Supports `@mention` completion for room members.

```
POST /api/chat/{roomId}/autocomplete
```

**Auth:** Required — must be a room member.

**Request Body:** `AutocompletionRequest`

| Field | Type | Description |
|---|---|---|
| `content` | string | Current input text |

**Behavior:**

- If input starts with `@`, searches room members by username.
- Supports `@username` and `@u/username` formats.
- For non-mention input (stickers, etc.), delegates to the autocompletion gRPC service.

**Response:** `List<Autocompletion>`

```json
[
  {
    "type": "user",
    "keyword": "@alice",
    "data": { /* SnAccount */ }
  }
]
```

---

## MLS Device Management

### Report MLS Device Joined

Registers an MLS device membership with the MLS group when a new device joins.

```
POST /api/chat/{roomId}/devices/me/joined
```

**Auth:** Required — must be a room member.

**Request Body:** `DeviceJoinedRequest`

| Field | Type | Description |
|---|---|---|
| `mlsDeviceId` | string | The MLS device identifier |
| `epoch` | long | The MLS epoch at join time |

**Errors:**

| Status | Condition |
|---|---|
| 400 | Room is not an MLS room or has no MLS group configured |

---

## Message Pins

See [CHAT_MESSAGE_PINS](./CHAT_MESSAGE_PINS.md) for full details.

### Pin Message

```
POST /api/chat/{roomId}/pins
```

**Auth:** Required  
**Permission:** `chat.pins.manage`

**Request Body:** `PinMessageRequest`

| Field | Type | Required | Description |
|---|---|---|---|
| `messageId` | Guid | Yes | Message to pin |
| `expiresAt` | Instant? | No | Optional expiry time |

### Unpin Message

```
DELETE /api/chat/{roomId}/pins/{pinId}
```

**Auth:** Required  
**Permission:** `chat.pins.manage`

### List Pins

```
GET /api/chat/{roomId}/pins?includeExpired=false
```

**Auth:** Optional for public rooms.

**Query Parameters:**

| Param | Type | Default | Description |
|---|---|---|---|
| `includeExpired` | bool | false | Include expired pins |

---

## Bot Commands

### Get Bot Commands

Returns all bot commands available in a room, keyed by bot member ID.

```
GET /api/chat/{roomId}/bots/commands
```

**Auth:** Optional for public rooms.

**Response:** `Dictionary<Guid, List<SnBotCommand>>`

---

## Data Models

### ChatSummaryResponse

```csharp
public class ChatSummaryResponse
{
    public int UnreadCount { get; set; }
    public SnChatMessage? LastMessage { get; set; }
}
```

### ChatAccountStatusResponse

```csharp
public class ChatAccountStatusResponse
{
    public Guid AccountId { get; set; }
    public bool HasActiveSubscriptions { get; set; }
    public bool HasAnyWebSocketConnection { get; set; }
    public bool PushNotificationsMaySendForUnsubscribedRooms { get; set; }
    public List<ChatSubscriptionRoomStatusResponse> Subscriptions { get; set; }
}
```

### ChatSubscriptionRoomStatusResponse

```csharp
public class ChatSubscriptionRoomStatusResponse
{
    public Guid RoomId { get; set; }
    public Guid MemberId { get; set; }
    public SnChatRoom Room { get; set; }
    public bool IsSubscribed { get; set; }
    public bool PushNotificationsSuppressed { get; set; }
    public List<ChatSubscriptionDeviceStatusResponse> Devices { get; set; }
}
```

### ChatSubscriptionDeviceStatusResponse

```csharp
public class ChatSubscriptionDeviceStatusResponse
{
    public string DeviceToken { get; set; }
    public Instant ExpiresAt { get; set; }
    public bool IsWebSocketConnected { get; set; }
}
```

---

## Related Documentation

- [Chat Documentation Index](./CHAT_INDEX.md) — full list of chat docs
- [Chat WebSocket Send Message](./CHAT_WEBSOCKET_SEND_MESSAGE.md)
- [Chat WebSocket Typing](./CHAT_WEBSOCKET_TYPING.md)
- [Chat WebSocket Infrastructure](./CHAT_WEBSOCKET_INFRASTRUCTURE.md)
- [Chat Room Controller](./CHAT_ROOM_CONTROLLER.md)
- [Chat Room Sync](./CHAT_ROOM_SYNC.md)
- [Chat Global Sync](./CHAT_GLOBAL_SYNC.md)
- [Chat Room Sequence Sync](./CHAT_ROOM_SEQUENCE_SYNC.md)
- [Chat System Messages](./CHAT_SYSTEM_MESSAGES.md)
- [Chat Message Pins](./CHAT_MESSAGE_PINS.md)
- [Chat Message Reactions](./CHAT_MESSAGE_REACTIONS.md)
- [Chat Voice Messages](./CHAT_VOICE_MESSAGES.md)
- [Chat Placeholder Messages](./CHAT_PLACEHOLDER_MESSAGES.md)
- [Chat Redirect Messages](./CHAT_REDIRECT_MESSAGES.md)
- [Chat Mark All Read](./CHAT_MARK_ALL_READ.md)
- [Chat E2EE Integration](./CHAT_E2EE_INTEGRATION.md)
- [Bot Chat API](./BOT_CHAT_API.md)
- [Realtime Call API](./REALTIME_CALL_API.md)
- [Call Invited WebSocket](./CALL_INVITED_WEBSOCKET.md)
