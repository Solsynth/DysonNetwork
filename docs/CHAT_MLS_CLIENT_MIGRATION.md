# Chat MLS Client Migration Guide

## Scope

For all chat clients (web/mobile/desktop) that send or process encrypted messages.

## Breaking changes

1. Encrypted chat is MLS-only.
2. Chat write payload scheme must be `chat.mls.v1` (legacy `pass.e2ee.mls.v1` is not accepted).
3. MLS write endpoints require `X-Client-Ability: chat-mls-v1`.
4. Legacy chat E2EE enable endpoint is removed; use `POST /api/chat/{id}/mls/enable`.
5. Legacy encrypted modes (`E2eeDm`, `E2eeSenderKeyGroup`) are retired for new writes.

## Required client behavior

1. Store MLS identity/state per device.
2. Publish MLS key packages through Pass MLS APIs.
3. Include `X-Client-Ability: chat-mls-v1` on:
   - chat write APIs (`send`, `update`, `delete`)
   - Pass MLS APIs (`/api/e2ee/mls/*`)
4. For MLS room user-content writes:
   - `is_encrypted = true`
   - `encryption_scheme = chat.mls.v1`
   - `encryption_epoch` required
   - ciphertext required
   - `encryption_message_type` should match chat semantics (`text`, `messages.update`, `messages.delete`)
5. Never send plaintext `content` for encrypted user messages.

## Endpoint updates

1. Enable MLS:
   - `POST /api/chat/{id}/mls/enable`
2. Legacy removed:
   - `POST /api/chat/{id}/e2ee/enable` -> `410`
3. Pass MLS control plane:
   - use `/api/e2ee/mls/*`
   - legacy `/api/e2ee/*` non-MLS routes -> `410`

## System events to handle

1. `system.e2ee.enabled`
2. `system.mls.epoch_changed`
3. `system.mls.reshare_required`

Client should treat these as operational hints and update local MLS state accordingly.

## Error handling checklist

1. `chat.e2ee_required`: missing/invalid ability header for MLS room writes.
2. `chat.mls_payload_required`: missing MLS-required encryption fields.
3. `e2ee.mls_ability_required`: missing ability for Pass MLS endpoints.
4. `e2ee.legacy_endpoint_removed`: old Pass E2EE routes used.

## Reactions and control events

1. Reactions/typing/read/system events remain plaintext control signals in v1.
2. User content, edit, delete payloads in MLS rooms must remain encrypted.

## Rollout checklist

1. Remove legacy scheme usage (`pass.e2ee.mls.v1`) from all clients.
2. Ensure every device sends ability header consistently.
3. Add decrypt-failure reason telemetry (`missing_state`, `missing_welcome`, `invalid_ciphertext`).
4. Add re-share retry flow when receiving `system.mls.reshare_required`.
