# Pass E2EE API (MLS for Chat)

## Overview

Pass provides encrypted transport/state APIs. Cryptography stays client-side.

For chat, MLS endpoints are authoritative:

- base: `/api/e2ee/mls/*`
- realtime delivery uses Ring websocket push via `RemoteRingService`
- legacy `/api/e2ee/*` non-MLS routes return `410 Gone` with `e2ee.legacy_endpoint_removed`

## MLS Endpoints

### Key packages

- `PUT /api/e2ee/mls/devices/me/key-packages`
- `GET /api/e2ee/mls/keys/{accountId}/devices`

### Group state

- `POST /api/e2ee/mls/groups/{roomId}/bootstrap`
- `POST /api/e2ee/mls/groups/{roomId}/commit`
- `POST /api/e2ee/mls/groups/{roomId}/welcome/fanout`
- `POST /api/e2ee/mls/groups/{roomId}/reshare-required`

### Envelope transport

- `POST /api/e2ee/mls/messages/fanout`
- `GET /api/e2ee/mls/envelopes/pending?device_id=...`
- `POST /api/e2ee/mls/envelopes/{id}/ack?device_id=...`

### Device security

- `POST /api/e2ee/mls/devices/{deviceId}/revoke`

## Envelope Types Used By MLS

- `MlsCommit`
- `MlsWelcome`
- `MlsApplication`
- `MlsProposal`

## Device Fanout Rules

On MLS fanout send, server:

1. resolves recipient active devices
2. requires one payload per active target device
3. rejects missing/unknown/revoked device payloads
4. stores one envelope per recipient device

## Contract Markers

- chat encryption scheme marker: `chat.mls.v1`
- default ciphersuite policy: `MLS_128_DHKEMX25519_AES128GCM_SHA256_Ed25519`
- scheme identifiers follow `<usecase>.<method>.<version>`
