# Pass E2EE API

## Overview

`DysonNetwork.Pass` provides transport/state APIs for encrypted messaging.

- Crypto operations are client-side only.
- Server stores opaque ciphertext and metadata.
- Realtime delivery uses Ring websocket push through `RemoteRingService`.

Base path: `/api/e2ee`

## Versioning

- Legacy endpoints (`/api/e2ee/*`) remain for compatibility.
- MLS v2 endpoints are under `/api/e2ee/mls/*` and are used by new encrypted chat writes.

## Envelope Types

`SnE2eeEnvelopeType`:

- `0`: `PairwiseMessage`
- `1`: `SenderKeyDistribution` (legacy)
- `2`: `SenderKeyMessage` (legacy)
- `3`: `Control`
- `4`: `MlsCommit`
- `5`: `MlsWelcome`
- `6`: `MlsApplication`
- `7`: `MlsProposal`

## MLS Endpoints

### Device key package lifecycle

- `PUT /api/e2ee/mls/devices/me/key-packages`
- `GET /api/e2ee/mls/keys/{accountId}/devices`

### Group/session control

- `POST /api/e2ee/mls/groups/{roomId}/bootstrap`
- `POST /api/e2ee/mls/groups/{roomId}/commit`
- `POST /api/e2ee/mls/groups/{roomId}/welcome/fanout`
- `POST /api/e2ee/mls/groups/{roomId}/reshare-required`

### Envelope transport (device-scoped)

- `POST /api/e2ee/mls/messages/fanout`
  - Stored as `MlsApplication` fanout envelopes.
  - Requires ciphertext for each active recipient device.
- `GET /api/e2ee/mls/envelopes/pending?device_id=...`
- `POST /api/e2ee/mls/envelopes/{id}/ack?device_id=...`

### Device security operation

- `POST /api/e2ee/mls/devices/{deviceId}/revoke`
  - Revoked device is excluded from fanout target resolution.

## Device-Scoped Fanout Rules

On fanout send:

1. Resolve recipient active devices.
2. Require exactly one payload per active device.
3. Reject missing payloads.
4. Reject payloads for unknown/revoked devices.
5. Persist one envelope per recipient device.

Optional sender-copy can be enabled for local consistency.

## Pending/Ack Semantics

- Pending query is scoped by `(recipient_account_id, recipient_device_id)`.
- `Pending -> Delivered` transition occurs on fetch/realtime delivery.
- Ack is scoped per device envelope.

## MLS Metadata Conventions

Envelope/group metadata may include:

- `mls_group_id`
- `epoch`
- `content_type`
- `sender_leaf_index`

## Algorithm Notes

Pass does not enforce cryptographic internals, but current platform defaults use:

- scheme marker: `pass.e2ee.mls.v1`
- default ciphersuite string: `MLS_128_DHKEMX25519_AES128GCM_SHA256_Ed25519`

Legacy markers retained for migration period:

- `x25519` (bundle algorithm)
- `x3dh-v1` (legacy session bootstrap hint)
