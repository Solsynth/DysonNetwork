# Chat E2EE Integration (Messager + Pass)

## Overview

This document describes how chat E2EE works after integrating `DysonNetwork.Messager` with Pass E2EE key/bootstrap APIs.

- `Messager` is the encrypted message transport timeline (storage, websocket fan-out, sync).
- `Pass` is key/session/control bootstrap (`/api/e2ee/*`).
- Encryption/decryption remains client-side.

## Room Encryption Modes

`SnChatRoom.encryption_mode`:

| Value | Name | Applies To |
|-------|------|------------|
| `0` | `None` | Plaintext room |
| `1` | `E2eeDm` | Direct message rooms |
| `2` | `E2eeSenderKeyGroup` | Group rooms (sender key) |

Validation:
- DM room cannot use `E2eeSenderKeyGroup`.
- Group room cannot use `E2eeDm`.

## Capability Requirement

For E2EE rooms, client must include:

`X-Dyson-Client-Capabilities: chat-e2ee-v1`

If missing, endpoints return:

```json
{
  "code": "chat.e2ee_required",
  "error": "This room requires E2EE-capable clients."
}
```

## Encrypted Message Fields

`SnChatMessage` now supports:

- `is_encrypted` (bool)
- `ciphertext` (base64 bytes)
- `encryption_header` (base64 bytes, optional)
- `encryption_signature` (base64 bytes, optional)
- `encryption_scheme` (string)
- `encryption_epoch` (long, optional)
- `encryption_message_type` (`content.new` / `content.edit` / `content.delete`)
- `client_message_id` (idempotency/retry)

## Endpoint Behavior

### Send / Update / Delete in E2EE rooms

- Require encrypted payload.
- Reject plaintext fields (`content`, server-side fund/poll embeds, plaintext references).
- Voice endpoint returns:

```json
{
  "code": "chat.e2ee_voice_not_supported_v1",
  "error": "Voice endpoint is not supported for E2EE rooms in v1."
}
```

### Read / Sync in E2EE rooms

- Requires E2EE capability header.
- Returns encrypted message fields unchanged.

## Notifications and Link Previews

For encrypted user messages:
- Link preview scraping is skipped.
- Push body is generic (`Encrypted message`).

## Group Sender-Key Rotation Hook

On group membership changes, Messager emits plaintext system control message:

- `type`: `system.e2ee.rotate_required`
- `meta`:
  - `room_id`
  - `changed_member_id`
  - `reason` (`member_joined` / `member_left` / `member_removed`)
  - `rotation_hint_epoch`

Clients must rotate/distribute sender keys via Pass E2EE endpoints after receiving this event.
