# Bot Chat Enhancement API

## Overview

This document describes the API changes for bot chat enhancements, including:
- Bot chat configuration (commands, webhooks, behavior flags)
- Auto-approved DMs for bots
- Bot message events via EventBus
- Developer member impersonation (`?identity=` parameter)
- Bot commands autocomplete

---

## Bot Chat Configuration

### Get Bot Chat Config

```
GET /api/developers/{pubName}/projects/{projectId}/bots/{botId}/chat
```

**Authorization:** Requires at least `Viewer` role in the developer publisher.

**Response:**
```json
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "commands": [
    {
      "name": "help",
      "description": "Show help information",
      "usage": "/help [topic]",
      "parameters": [
        {
          "name": "topic",
          "description": "Help topic to show",
          "required": false,
          "type": "string"
        }
      ]
    }
  ],
  "webhooks": [
    {
      "url": "https://example.com/webhook",
      "secret": "hmac-secret-key",
      "events": ["messages.new"],
      "is_active": true
    }
  ],
  "auto_approve_dm": true,
  "support_chat": true,
  "subscribed_events": ["messages.new", "member.joined"],
  "created_at": "2026-06-07T10:00:00Z",
  "updated_at": "2026-06-07T10:00:00Z"
}
```

### Update Bot Chat Config

```
PUT /api/developers/{pubName}/projects/{projectId}/bots/{botId}/chat
```

**Authorization:** Requires at least `Editor` role in the developer publisher.

**Request Body:**
```json
{
  "commands": [
    {
      "name": "help",
      "description": "Show help information",
      "usage": "/help [topic]",
      "parameters": []
    },
    {
      "name": "status",
      "description": "Check bot status",
      "usage": "/status",
      "parameters": []
    }
  ],
  "webhooks": [
    {
      "url": "https://example.com/webhook",
      "secret": "my-secret",
      "events": ["messages.new"],
      "is_active": true
    }
  ],
  "auto_approve_dm": true,
  "support_chat": true,
  "subscribed_events": ["messages.new"]
}
```

**Response:** Returns the updated `SnBotChatConfig` object.

### Update Bot Manifest (Full Replace)

```
POST /api/developers/{pubName}/projects/{projectId}/bots/{botId}/chat/manifest
```

**Authorization:** Requires at least `Editor` role in the developer publisher.

**Request Body:**
```json
{
  "commands": [
    {
      "name": "help",
      "description": "Show help information"
    }
  ],
  "webhooks": [],
  "auto_approve_dm": true,
  "support_chat": true,
  "subscribed_events": ["messages.new"]
}
```

**Response:** Returns the updated `SnBotChatConfig` object.

---

## Public Bot Endpoints

### Get Bot Transparent Info

```
GET /api/bots/public/{botId}
```

**Response:**
```json
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "slug": "my-bot",
  "is_active": true,
  "developer": {
    "id": "...",
    "publisher_id": "...",
    "publisher": {
      "name": "my-publisher",
      "nick": "My Publisher"
    }
  }
}
```

### Get Bot Developer

```
GET /api/bots/public/{botId}/developer
```

**Response:**
```json
{
  "id": "developer-id",
  "publisher_id": "publisher-id"
}
```

### Get Bot Chat Config (Public)

```
GET /api/bots/public/{botId}/chat
```

**Response:** Same as the authenticated endpoint but publicly accessible.

### Get Bot Commands (Public)

```
GET /api/bots/public/{botId}/commands
```

**Response:**
```json
[
  {
    "name": "help",
    "description": "Show help information",
    "usage": "/help [topic]",
    "parameters": [
      {
        "name": "topic",
        "required": false,
        "type": "string"
      }
    ]
  }
]
```

---

## Chat API Changes

### Create Direct Message (Auto-Approve)

```
POST /api/chat/direct
```

**Request Body:**
```json
{
  "related_user_id": "bot-account-id",
  "encryption_mode": "None"
}
```

**Behavior:**
- If the target account is a bot with `SupportChat = false`, returns `403`
- If the target account is a bot with `AutoApproveDm = true`, the bot member's `JoinedAt` is set immediately (no invite required)
- If `AutoApproveDm = false`, falls through to normal invite flow

**Response:** Returns the created `SnChatRoom` object.

### Send Message with Identity

```
POST /api/chat/{roomId}/messages?identity={botId}
```

**Query Parameters:**
| Parameter | Type | Description |
|-----------|------|-------------|
| `identity` | Guid (optional) | Bot account ID to send as |

**Authorization:**
- If `identity` is provided, the current user must have at least `Editor` role in the bot's publisher
- If `identity` is omitted, uses the current user's identity

**Request Body:**
```json
{
  "content": "Hello from the bot!",
  "nonce": "optional-client-nonce"
}
```

**Response:** Returns the created `SnChatMessage` object with the bot as the sender.

### Get Bot Commands in Room

```
GET /api/chat/{roomId}/bots/commands
```

**Authorization:** Requires read access to the room.

**Response:**
```json
{
  "550e8400-e29b-41d4-a716-446655440000": [
    {
      "name": "help",
      "description": "Show help information",
      "usage": "/help [topic]",
      "parameters": []
    }
  ],
  "660e8400-e29b-41d4-a716-446655440001": [
    {
      "name": "status",
      "description": "Check status",
      "usage": "/status",
      "parameters": []
    }
  ]
}
```

The response is a dictionary where keys are bot account IDs and values are arrays of commands. Results are cached for 5 minutes.

---

## EventBus Events

### BotChatMessageEvent

Published when a new message is sent to a room containing bot accounts.

**Subject:** `bot.chat.message`
**Stream:** `bot_chat_events`

**Payload:**
```json
{
  "event_id": "uuid",
  "timestamp": "2026-06-07T10:00:00Z",
  "event_type": "bot.chat.message",
  "stream_name": "bot_chat_events",
  "bot_account_id": "uuid",
  "room_id": "uuid",
  "message_id": "uuid",
  "sender_account_id": "uuid",
  "content": "Hello bot!",
  "message_type": "text",
  "meta": {},
  "created_at": "2026-06-07T10:00:00Z"
}
```

### BotChatConfigUpdatedEvent

Published when a bot's chat configuration is updated.

**Subject:** `bot.chat.config.updated`
**Stream:** `bot_chat_events`

**Payload:**
```json
{
  "event_id": "uuid",
  "timestamp": "2026-06-07T10:00:00Z",
  "event_type": "bot.chat.config.updated",
  "stream_name": "bot_chat_events",
  "bot_account_id": "uuid",
  "updated_at": "2026-06-07T10:00:00Z"
}
```

---

## Webhook Delivery

When a `BotChatMessageEvent` is received, the system delivers to configured webhooks:

**HTTP POST to webhook URL**

**Headers:**
```
Content-Type: application/json
X-Bot-Signature: sha256=<hmac-hex-digest>
```

**Body:**
```json
{
  "bot_id": "uuid",
  "room_id": "uuid",
  "message_id": "uuid",
  "sender_account_id": "uuid",
  "content": "Hello bot!",
  "message_type": "text",
  "meta": {},
  "created_at": 1717754400000
}
```

**Signature Verification:**
```csharp
var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(requestBody));
var expectedSignature = $"sha256={Convert.ToHexString(hash).ToLowerInvariant()}";
// Compare with X-Bot-Signature header
```

---

## Data Models

### SnBotChatConfig

| Field | Type | Description |
|-------|------|-------------|
| `id` | Guid | Same as BotAccount.Id (1:1) |
| `commands` | List\<SnBotCommand\> | Bot's slash commands |
| `webhooks` | List\<SnBotWebhook\> | Webhook endpoints |
| `auto_approve_dm` | bool | Auto-approve DMs (default: true) |
| `support_chat` | bool | Bot supports chat (default: true) |
| `subscribed_events` | List\<string\> | Event types to receive |
| `created_at` | Instant | Creation timestamp |
| `updated_at` | Instant | Last update timestamp |

### SnBotCommand

| Field | Type | Description |
|-------|------|-------------|
| `name` | string | Command name (e.g., "help") |
| `description` | string | Command description |
| `usage` | string? | Usage pattern (e.g., "/help [topic]") |
| `parameters` | List\<SnBotCommandParameter\>? | Command parameters |

### SnBotCommandParameter

| Field | Type | Description |
|-------|------|-------------|
| `name` | string | Parameter name |
| `description` | string? | Parameter description |
| `required` | bool | Is required |
| `type` | string? | Type ("string", "int", "user") |

### SnBotWebhook

| Field | Type | Description |
|-------|------|-------------|
| `url` | string | Webhook URL |
| `secret` | string? | HMAC signing secret |
| `events` | List\<string\> | Event types to deliver |
| `is_active` | bool | Is webhook active |

---

## Cache Behavior

### Bot Commands Cache

- **Cache Key:** `chat:room:bot_commands:{roomId}`
- **TTL:** 5 minutes
- **Invalidation Triggers:**
  - Bot joins a room
  - Bot leaves a room
  - Bot is removed from a room
  - Bot's manifest/config is updated

### Cache Invalidation Flow

1. Developer updates bot manifest via API
2. `BotChatConfigUpdatedEvent` published to NATS
3. Messager service receives event
4. Invalidates cache for all rooms where the bot is a member
5. Next request to `GET /api/chat/{roomId}/bots/commands` rebuilds cache

---

## Authorization Matrix

| Action | Required Role |
|--------|---------------|
| View bot chat config | Publisher Viewer |
| Update bot chat config | Publisher Editor |
| Update bot manifest | Publisher Editor |
| Send message as bot | Publisher Editor |
| List messages (with identity) | Publisher Viewer |
| View bot commands in room | Chat Room Member |

---

## Error Responses

### 403 Bot Does Not Support Chat

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.4",
  "title": "Forbidden",
  "status": 403,
  "detail": "This bot does not support chat."
}
```

### 403 Insufficient Publisher Role

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.4",
  "title": "Forbidden",
  "status": 403,
  "detail": "You must be an editor of the bot's publisher to perform this action."
}
```

### 403 Bot Not in Room

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.4",
  "title": "Forbidden",
  "status": 403,
  "detail": "The bot is not a member of this chat room."
}
```
