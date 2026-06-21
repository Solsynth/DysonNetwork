# Call Invited WebSocket Packet

This document describes the realtime call invite packet sent by `DysonNetwork.Messager` when one user invites another user into a chat call.

## Packet Type

```text
call.invited
```

This is defined by `WebSocketPacketType.CallInvited`.

## When It Is Sent

The packet is sent by:

```text
POST /api/chat/realtime/{roomId}/invite/{targetAccountId}
```

When the invite succeeds, the server does two best-effort deliveries for the target account:

1. A Ring push notification with `PushType = "VoIP"`
2. A websocket packet with type `call.invited`

The endpoint still returns `204 No Content`. Clients must not assume both deliveries always arrive.

## Payload

The websocket packet `data` is a JSON object with the following fields:

```json
{
  "event": "call_invited",
  "room_id": "9f23b1dc-1a1d-4b87-a0b9-2d7fdab77d4c",
  "call_id": "f5f61848-4627-413f-8a67-3d4ca7db8cf0",
  "caller_id": "95d8353a-04d2-488f-b43d-bad2fd455e56",
  "caller_name": "Alice",
  "room_name": "General, Example Realm",
  "session_id": "Call_abc123"
}
```

## Field Meanings

| Field | Type | Meaning |
|---|---|---|
| `event` | string | Always `call_invited` |
| `room_id` | UUID string | Chat room ID that owns the call |
| `call_id` | UUID string | Server-side realtime call record ID |
| `caller_id` | UUID string | Account ID of the inviter |
| `caller_name` | string | Display name resolved from the inviter member/account |
| `room_name` | string | Human-readable room section shown to clients |
| `session_id` | string | Provider session identifier used for the current call |

## Delivery Semantics

- The packet is sent to the invited account, not only to a single device.
- The websocket delivery is fire-and-forget.
- The websocket packet can arrive without the VoIP push.
- The VoIP push can arrive without the websocket packet.
- The client should treat both as equivalent signals for the same incoming call.

Because of that, the client should deduplicate incoming call UI using `call_id` or the tuple `room_id + session_id`.

## Recommended Client Handling

When the client receives `call.invited`:

1. Parse the payload from the websocket packet `data`.
2. Ignore the packet if the user is already handling the same `call_id`.
3. Show incoming call UI immediately.
4. Use `caller_name` and `room_name` for the initial UI.
5. Fetch or refresh call state from `GET /api/chat/realtime/{roomId}` if the local state is stale.
6. When the user accepts, call `GET /api/chat/realtime/{roomId}/join`.
7. Use the returned provider token and endpoint to join LiveKit.
8. Poll `GET /api/chat/realtime/{roomId}/participants` after joining to keep participant state current.

## Recommended Fallback Rules

- If `room_name` is present, show it directly instead of rebuilding it client-side.
- If `caller_name` is empty for any reason, fall back to a generic incoming call label.
- If the client receives repeated `call.invited` packets for the same `call_id`, refresh the ringing timeout instead of stacking multiple prompts.
- If `join` fails with `403`, treat the invite as expired or no longer valid.

## Minimal Example

Example websocket packet envelope:

```json
{
  "type": "call.invited",
  "data": {
    "event": "call_invited",
    "room_id": "9f23b1dc-1a1d-4b87-a0b9-2d7fdab77d4c",
    "call_id": "f5f61848-4627-413f-8a67-3d4ca7db8cf0",
    "caller_id": "95d8353a-04d2-488f-b43d-bad2fd455e56",
    "caller_name": "Alice",
    "room_name": "General, Example Realm",
    "session_id": "Call_abc123"
  }
}
```

## Related Docs

- [REALTIME_CALL_API](./REALTIME_CALL_API.md)
- [NOTIFICATION_SUBSCRIPTIONS](./NOTIFICATION_SUBSCRIPTIONS.md)
- [SOP_PUSH_API](./SOP_PUSH_API.md)
