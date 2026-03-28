# Chat MLS Hard-Cut Runbook

## What hard-cut means

- All encrypted chat rooms use `E2eeMls`.
- Legacy chat encryption modes are converted and no longer used.
- Old `/api/chat/{id}/e2ee/enable` endpoint is removed (`410`).

## Migration SQL behavior

Messager migration performs:

1. `encryption_mode IN (1,2) -> 3`
2. backfill `mls_group_id = 'chat:' || id` where null

## Rollout Steps

1. Deploy Pass MLS endpoints (`/api/e2ee/mls/*`).
2. Deploy Messager hard-cut build.
3. Run DB migrations.
4. Verify rooms previously in mode 1/2 are now mode 3.
5. Verify write requests require `X-Client-Ability: chat-mls.v2` in MLS rooms.

## Validation Checklist

- MLS send/update/delete succeed with ciphertext payloads.
- Plaintext writes in encrypted rooms are rejected.
- Membership changes emit `system.mls.epoch_changed`.
- `system.e2ee.rotate_required` no longer appears.
- Offline device resync test passes (`pending` + `ack` + resumed decrypt).

## Operational Metrics

- MLS fanout failure rate
- pending envelope backlog per device
- envelope ack latency
- decrypt failure reasons (`missing_state`, `missing_welcome`, `invalid_ciphertext`)
- MLS bootstrap success rate
- MLS reshare-required failure/retry rate
- MLS group size distribution (watch large-room tail)
- revoked-device pending envelope purge count

## Threat Model

Protected:

- new encrypted chat writes are MLS-only with end-to-end ciphertext persistence
- revoke blocks future device fanout targets and clears pending delivery queue for revoked device

Not protected:

- historical ciphertext already delivered to a compromised device before revoke
- server metadata visibility (room size/activity/device identifiers)

Assumptions:

- clients perform MLS state backup/import if they need historical decrypt on new devices
- operators enforce room-size and queue capacity policies for large fanout workloads
