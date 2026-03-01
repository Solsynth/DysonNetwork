# Chat Send Message Over WebSocket

This document describes how to send chat messages through the Ring WebSocket gateway to Messager, alongside the existing HTTP endpoint.

## Overview

- Existing HTTP send endpoint is still supported: `POST /api/chat/{roomId}/messages`
- New WebSocket send packet is now supported by `DysonNetwork.Messager`:
  - Request packet type: `messages.send`
  - Success ack packet type (to sender device): `messages.delivered`
  - Room broadcast packet type (to room members): `messages.new`

`messages.delivered` includes the full persisted `SnChatMessage` so client-side pending messages can be marked as delivered using server-generated fields (`id`, timestamps, etc.).

## Gateway Routing

Client must send packet through Ring WebSocket with:

- `endpoint = "DysonNetwork.Messager"`
- `type = "messages.send"`

## Request Shape

`messages.send` data follows `SendMessageRequest` fields, plus `chat_room_id`.

```json
{
  "type": "messages.send",
  "endpoint": "DysonNetwork.Messager",
  "data": {
    "chat_room_id": "00000000-0000-0000-0000-000000000000",
    "content": "Hello from websocket",
    "nonce": "optional-client-nonce",
    "client_message_id": "optional-client-message-id",
    "attachments_id": [],
    "meta": {},
    "replied_message_id": null,
    "forwarded_message_id": null,
    "is_encrypted": false,
    "ciphertext": null,
    "encryption_header": null,
    "encryption_signature": null,
    "encryption_scheme": null,
    "encryption_epoch": null,
    "encryption_message_type": null
  }
}
```

## Success Flow

After persistence succeeds:

1. Sender device receives `messages.delivered` with full message payload.
2. Room members receive `messages.new` via normal chat delivery flow.

Example ack:

```json
{
  "type": "messages.delivered",
  "data": {
    "id": "server-message-id",
    "chat_room_id": "room-id",
    "sender_id": "member-id",
    "client_message_id": "optional-client-message-id",
    "content": "Hello from websocket",
    "...": "other SnChatMessage fields"
  }
}
```

## Validation and Errors

`messages.send` applies the same send rules as HTTP for:

- room membership check
- timeout check
- empty message rejection (non-E2EE)
- E2EE payload/plaintext validation
- fund and poll validation
- attachment resolution
- reply/forward target validation
- mention extraction

On validation or processing failure, sender device receives:

```json
{
  "type": "error",
  "error_message": "..."
}
```

## Client Integration Notes

- Use `client_message_id` for retry/idempotency logic on client side.
- Mark local pending message as delivered when `messages.delivered` is received.
- Keep existing `messages.new` handling for room timeline sync.
