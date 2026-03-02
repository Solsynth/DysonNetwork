# Chat System Messages

System messages are backend-generated `SnChatMessage` timeline events delivered as normal `messages.new` websocket packets.

## Core Types

| Type | Purpose |
|---|---|
| `system.member.joined` | Member joined room |
| `system.member.left` | Member left or removed |
| `system.chat.updated` | Room settings changed |
| `system.e2ee.enabled` | Encryption enabled for room |
| `system.e2ee.rotate_required` | Legacy sender-key rotate required |
| `system.mls.epoch_changed` | MLS epoch advanced/needs commit sync |
| `system.mls.reshare_required` | MLS state re-share required for a device |
| `system.call.member.joined` | Realtime call participant joined |
| `system.call.member.left` | Realtime call participant left/removed |

## Encryption-Related Events

### `system.e2ee.enabled`

Emitted by `POST /api/chat/{id}/mls/enable`.

Example meta:

```json
{
  "event": "e2ee_enabled",
  "room_id": "room-guid",
  "mode": "E2eeMls",
  "mls_group_id": "chat:room-guid"
}
```

### `system.e2ee.rotate_required` (Legacy only)

Only for legacy sender-key rooms (`E2eeSenderKeyGroup`).

Example meta:

```json
{
  "event": "e2ee_rotate_required",
  "room_id": "room-guid",
  "changed_member_id": "account-guid",
  "reason": "member_joined",
  "rotation_hint_epoch": 1740758400000
}
```

### `system.mls.epoch_changed`

Used for MLS rooms (`E2eeMls`) when membership/commit flow requires clients to advance epoch state.

Example meta:

```json
{
  "event": "mls_epoch_changed",
  "room_id": "room-guid",
  "mls_group_id": "chat:room-guid",
  "epoch": 1740758400000,
  "reason": "member_joined"
}
```

### `system.mls.reshare_required`

Used when a target device needs MLS state re-share.

Example meta:

```json
{
  "event": "mls_reshare_required",
  "room_id": "room-guid",
  "mls_group_id": "chat:room-guid",
  "target_account_id": "account-guid",
  "target_device_id": "device-id",
  "epoch": 1740758400000,
  "reason": "new_device"
}
```

## Delivery and Notification

- Delivered over normal room websocket message packets.
- System messages are informational and not user-authored.
- No extra push-notification side channel is required for these events.
