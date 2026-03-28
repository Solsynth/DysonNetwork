# Pass E2EE API (MLS for Chat)

## Overview

Pass provides encrypted transport/state APIs. Cryptography stays client-side.

For chat, MLS endpoints are authoritative:

- base: `/api/e2ee/mls/*`
- realtime delivery uses Ring websocket push via `RemoteRingService`
- legacy `/api/e2ee/*` non-MLS routes return `410 Gone` with `e2ee.legacy_endpoint_removed`

## Required Client Ability

All MLS endpoints require:

- header: `X-Client-Ability`
- token: `chat.mls.v2`

Missing ability is rejected with `409` (`e2ee.mls_ability_required`).

## MLS Endpoints

### Key packages

- `PUT /api/e2ee/mls/devices/me/key-packages`
- `GET /api/e2ee/mls/keys/{accountId}/devices`

Server guardrails:

- upload rate limit: max `10` key-packages per account per 24h
- auto purge: key-packages older than `30` days are deleted

### Group state

- `POST /api/e2ee/mls/groups/{roomId}/bootstrap`
- `POST /api/e2ee/mls/groups/{roomId}/commit`
- `POST /api/e2ee/mls/groups/{roomId}/welcome/fanout`
- `POST /api/e2ee/mls/groups/{roomId}/reshare-required`

### Envelope transport

- `POST /api/e2ee/mls/messages/fanout`
- `GET /api/e2ee/mls/envelopes/pending?device_id=...`
- `POST /api/e2ee/mls/envelopes/{id}/ack?device_id=...`

Server guardrails:

- fanout payload cap: max `1000` per request
- per-device completeness required for recipient active devices

### Device security

- `POST /api/e2ee/mls/devices/{deviceId}/revoke`

Revoke behavior:

- device marked revoked immediately
- all pending envelopes targeting revoked device are purged
- sibling active devices receive control envelope (`event=mls_device_revoked`) for reshare/recovery flows

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

- chat encryption scheme marker: `chat.mls.v2`
- default ciphersuite policy: `MLS_128_DHKEMX25519_AES128GCM_SHA256_Ed25519`
- scheme identifiers follow `<usecase>.<method>.<version>`

## Operational Notes

- `POST /mls/groups/{roomId}/reshare-required` is idempotent and can be retried by clients until state is recovered.
- Large rooms should use async/batched fanout workers; API enforces request caps but operators still need queue capacity planning.

## Threat Model

Protected:

- message confidentiality/integrity for MLS payloads end-to-end (client-side cryptography)
- forward secrecy and post-compromise recovery semantics provided by MLS protocol operations

Not protected:

- server-visible metadata (room membership, device ids, timing, envelope sizes, attachment metadata)
- push notification content secrecy beyond generic payload policy

Attack surface / assumptions:

- compromised device can read local keys/state until revoked; revoke + reshare limits future exposure
- websocket transport MITM is out of scope for server application logic; TLS and client verification remain required
- nonce/key misuse is client responsibility (server treats ciphertext as opaque bytes)
