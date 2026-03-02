# Chat MLS Migration Runbook

## Goal

Move encrypted chat writes from legacy (`E2eeDm`, `E2eeSenderKeyGroup`) to MLS (`E2eeMls`) for both DM and group rooms.

## Migration Strategy

1. Deploy schema + API support (Pass + Messager).
2. Keep legacy read compatibility.
3. Enable MLS room-by-room using `POST /api/chat/{id}/mls/enable`.
4. Enforce MLS-only encrypted writes.
5. Freeze legacy encrypted room creation.
6. Retire legacy encrypted write paths after stability SLO is met.

## Preconditions

- Clients support `chat-mls-v1` and include `X-Client-Ability` on write endpoints.
- Clients publish MLS key packages per device.
- Realtime path is healthy (`RemoteRingService`/Ring push).

## Operational Checklist

### Phase 1: Server rollout

- Deploy Pass with `/api/e2ee/mls/*` endpoints.
- Deploy Messager with `POST /api/chat/{id}/mls/enable` endpoint.
- Run DB migrations for Pass and Messager.

### Phase 2: Internal canary

- Enable MLS for internal DM + group rooms.
- Verify:
  - send/update/delete in MLS rooms succeed with ciphertext payload
  - sync/read return ciphertext untouched
  - system message `system.e2ee.enabled` is emitted with `mls_group_id`
  - membership changes emit `system.mls.epoch_changed`

### Phase 3: Device behavior checks

- Same account, two devices: both receive new MLS envelopes.
- New device login: client surfaces `missing_welcome`/`missing_state` until re-share.
- Device revoke: revoked device receives no new envelopes.

### Phase 4: Legacy freeze

- Block new room creation in legacy encrypted modes.
- Keep old encrypted history readable.
- Track legacy write traffic to zero.

### Phase 5: Retirement

- Disable legacy encrypted write APIs behind feature flag.
- Keep migration window for historical read support.
- Announce final retirement date to clients.

## Monitoring Metrics

- MLS envelope fanout failure rate
- Per-device pending envelope backlog
- Envelope ack latency
- Decrypt failure reason distribution (`missing_state`, `missing_welcome`, `invalid_ciphertext`)
- `system.mls.reshare_required` frequency
- Capability-gate rejects (`chat.e2ee_required`)

## Rollback Notes

- Do not disable MLS for rooms already migrated (one-way enable).
- In incident mode, pause new MLS room enables and keep rooms operational with existing ciphertext relay.
- If needed, stop legacy-path retirement feature flag changes first before any schema rollback attempts.
