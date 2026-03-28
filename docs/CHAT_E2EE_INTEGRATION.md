# Chat E2EE Integration (Hard-Cut MLS)

Client implementation guide: [CHAT_MLS_CLIENT_MIGRATION.md](/Users/littlesheep/Documents/Projects/DysonNetwork/docs/CHAT_MLS_CLIENT_MIGRATION.md)

## Policy

Encrypted chat is now MLS-only.

- Supported room encryption modes:
  - `None = 0`
  - `E2eeMls = 3`
- Legacy chat modes (`E2eeDm = 1`, `E2eeSenderKeyGroup = 2`) are retired.
- Realtime transport remains websocket via Ring/`RemoteRingService`.

## Enable Endpoint

- `POST /api/chat/{id}/mls/enable`
- One-way only: `None -> E2eeMls`
- Cannot disable encryption after enable.
- Legacy `POST /api/chat/{id}/e2ee/enable` returns `410 Gone`.

## Write Capability

For MLS room write endpoints (`send`, `update`, `delete`), client must include:

`X-Client-Ability: chat-mls.v2`

Pass MLS control-plane endpoints (`/api/e2ee/mls/*`) also require the same ability token.

## MLS Message Contract

In MLS rooms, user content writes must include:

- `is_encrypted = true`
- `encryption_scheme = chat.mls.v2`
- `encryption_epoch` (required)
- `ciphertext` (required)
- `encryption_message_type` mirrors chat `type` semantics (`text`, `messages.update`, `messages.delete`)

Plaintext content fields are rejected for encrypted rooms.

Attachment policy:
- unencrypted file attachments are allowed in encrypted messages.
- attachment references are carried via API `attachments_id` list and stored as plaintext metadata (`meta.attachments_id`).

## Server-Side Behavior

- Server stores/transports opaque ciphertext only.
- Link preview extraction is disabled for encrypted messages.
- Push notification body is generic: `Encrypted message`.
- Control events (`typing/read/reaction/system`) remain plaintext.

## System Events

- `system.e2ee.enabled` emitted when MLS is enabled.
- `system.mls.epoch_changed` emitted on membership-change operational hooks.
- `system.mls.reshare_required` used for device re-share workflows.

## Algorithm Notes

Crypto remains client-side. Server contract markers:

- scheme: `chat.mls.v2`
- default ciphersuite policy: `MLS_128_DHKEMX25519_AES128GCM_SHA256_Ed25519`

Compatibility note:
- `pass.e2ee.mls.v2` is no longer accepted in chat write paths.

## Threat Model

Protected:

- encrypted message bodies and encrypted edit/delete payloads remain opaque to server storage and transport
- MLS forward secrecy and epoch-based post-compromise recovery are preserved as client responsibilities

Not protected:

- room/member metadata, attachment ids, reactions/read receipts/typing/system events (plaintext controls in v1)
- notification metadata and generic notification signaling

Assumptions:

- clients correctly manage MLS state and key storage per device
- compromised devices require revoke + reshare to restore safety for future traffic
