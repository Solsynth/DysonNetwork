# Chat E2EE Integration (MLS-Only Writes)

## Scope

This document defines how `DysonNetwork.Messager` integrates with `DysonNetwork.Pass` for encrypted chat after MLS migration.

- New encrypted writes are MLS-only.
- Legacy encrypted history remains readable during migration.
- Realtime delivery remains websocket through `RemoteRingService`-backed Ring push flows.

## Room Encryption Modes

`SnChatRoom.encryption_mode`:

| Value | Name | Status |
|---|---|---|
| `0` | `None` | Active |
| `1` | `E2eeDm` | Legacy (read compatibility only) |
| `2` | `E2eeSenderKeyGroup` | Legacy (read compatibility only) |
| `3` | `E2eeMls` | Active encrypted mode |

`SnChatRoom.mls_group_id` stores MLS group binding for encrypted rooms.

## Enable Flow

- Endpoint: `POST /api/chat/{id}/mls/enable`
- One-way transition: `None -> E2eeMls`
- Cannot disable back to `None`.
- Legacy `POST /api/chat/{id}/e2ee/enable` is retired and returns conflict.
- `PATCH /api/chat/{id}` cannot toggle encryption mode.

When enabled, server emits system message:

- type: `system.e2ee.enabled`
- content: `This chat now uses MLS.`
- meta includes:
  - `mode=E2eeMls`
  - `mls_group_id`

## Write Capability Gate

For MLS room write endpoints only (`send`, `update`, `delete`), client must include:

`X-Client-Ability: chat-mls-v1`

Missing capability returns:

```json
{
  "code": "chat.e2ee_required",
  "error": "This room requires capability 'chat-mls-v1'."
}
```

Read/sync endpoints do not require this header.

## MLS Message Contract

For user content messages in MLS rooms:

- `is_encrypted = true`
- `encryption_scheme = pass.e2ee.mls.v1`
- `encryption_epoch` required
- `ciphertext` required
- `encryption_message_type` required (`content.new`, `content.edit`, `content.delete`)

Plaintext user fields are rejected in encrypted rooms:

- `content`
- attachment IDs
- fund/poll embed inputs
- plaintext reply/forward inputs

Voice endpoint is not supported for encrypted rooms in v1:

- `chat.e2ee_voice_not_supported_v1`

## Algorithm Notes

Server is transport/state only. Encryption is client-side.

Current MLS profile in server defaults:

- `encryption_scheme`: `pass.e2ee.mls.v1`
- default ciphersuite: `MLS_128_DHKEMX25519_AES128GCM_SHA256_Ed25519`

Legacy bootstrap markers remain documented for backward compatibility:

- key bundle algorithm marker: `x25519`
- legacy session hint: `x3dh-v1`

## Membership Change Events

- Legacy sender-key rooms still use `system.e2ee.rotate_required`.
- MLS rooms emit `system.mls.epoch_changed` on membership-change hooks (`member_joined`, `member_left`, `member_removed`) so clients can commit/rekey and continue MLS traffic.
- Device-specific reshare workflows should emit/consume `system.mls.reshare_required`.

## Notification and Preview Policy

For encrypted user messages:

- Server does not run link preview extraction.
- Push body is generic: `Encrypted message`.

## Pass Integration Boundary

Messager does not bootstrap MLS keys/sessions directly. Clients must use Pass MLS endpoints (`/api/e2ee/mls/*`) for:

- key package publication/discovery
- commit/welcome fanout transport
- per-device pending envelope fetch + ack
- device revoke
