# Chat Typing Over WebSocket

This document describes the `messages.typing` websocket packet handled by `DysonNetwork.Messager`.

## Overview

- Request packet type: `messages.typing`
- Broadcast packet type: `messages.typing`
- Audience: subscribed members of the room except the sender

This packet is intended for transient room activity indicators such as typing, speaking, or upload progress.

## Gateway Routing

Client must send packet through Ring WebSocket with:

- `endpoint = "DysonNetwork.Messager"`
- `type = "messages.typing"`

## Request Shape

Required fields:

- `chat_room_id`: target room id

Optional fields:

- `ts`: client timestamp as Unix milliseconds
- `type`: activity type
- `progress`: progress value from `0` to `1`

Allowed `type` values:

- `typing`
- `speaking`
- `uploading`

If `type` is omitted or null, Messager defaults it to `typing`.

Example request:

```json
{
  "type": "messages.typing",
  "endpoint": "DysonNetwork.Messager",
  "data": {
    "chat_room_id": "00000000-0000-0000-0000-000000000000",
    "ts": 1770000000000,
    "type": "uploading",
    "progress": 0.42
  }
}
```

Minimal request:

```json
{
  "type": "messages.typing",
  "endpoint": "DysonNetwork.Messager",
  "data": {
    "chat_room_id": "00000000-0000-0000-0000-000000000000"
  }
}
```

## Broadcast Behavior

Messager validates that the sender is a member of the room, then broadcasts the packet to currently subscribed room members except the sending account.

Subscriptions are the same cache-backed room subscriptions managed by:

- `messages.subscribe`
- `messages.unsubscribe`

Broadcast payload:

```json
{
  "room_id": "00000000-0000-0000-0000-000000000000",
  "sender_id": "11111111-1111-1111-1111-111111111111",
  "sender": {
    "id": "11111111-1111-1111-1111-111111111111"
  },
  "timestamp": "2026-05-26T10:15:30Z",
  "type": "uploading",
  "progress": 0.42
}
```

Notes:

- `timestamp` is an `Instant` in the websocket payload.
- If client provides `ts`, the server converts that Unix millisecond value into `timestamp`.
- If client omits `ts`, the server uses current server time.
- `progress` may be `null`.

## Validation and Errors

Messager rejects the request when:

- `chat_room_id` is missing or invalid
- sender is not a member of the room
- `type` is not one of `typing`, `speaking`, or `uploading`
- `progress` is less than `0` or greater than `1`
- `ts` is outside the valid Unix millisecond range accepted by `Instant.FromUnixTimeMilliseconds`

On validation failure, the sender device receives:

```json
{
  "type": "error",
  "error_message": "..."
}
```

## Client Integration Notes

- Use `typing` for normal text composition state.
- Use `speaking` for live voice capture or push-to-talk state.
- Use `uploading` with `progress` when sending attachments or other staged media.
- Treat this packet as ephemeral UI state rather than durable room history.
