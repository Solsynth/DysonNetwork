# Chat System Messages

System messages are backend-generated `SnChatMessage` events delivered as normal `messages.new` websocket packets.

## Types

| Type | Purpose |
|---|---|
| `system.member.joined` | Member joined room |
| `system.member.left` | Member left/removed |
| `system.chat.updated` | Room settings changed |
| `system.e2ee.enabled` | Room switched to MLS encryption |
| `system.mls.epoch_changed` | MLS epoch advanced/rekey required |
| `system.mls.reshare_required` | MLS state re-share required for device |
| `system.call.member.joined` | Call participant joined |
| `system.call.member.left` | Call participant left/removed |

`system.e2ee.rotate_required` is retired in hard-cut MLS mode.

## Encryption Events

### `system.e2ee.enabled`

```json
{
  "event": "e2ee_enabled",
  "room_id": "room-guid",
  "mode": "E2eeMls",
  "mls_group_id": "chat:room-guid"
}
```

### `system.mls.epoch_changed`

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

## Threat Model

Protected:

- system message sequencing and delivery are authenticated by normal account/session auth

Not protected:

- these events are plaintext control data and reveal operational metadata (membership/device/epoch changes)

Assumptions:

- clients treat these events as control hints and validate actual MLS decryptability locally
