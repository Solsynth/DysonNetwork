# Pass E2EE API

## Overview

`DysonNetwork.Pass` now provides a reusable E2EE transport module for:

1. 1:1 encrypted messaging relay and offline queueing
2. key bundle upload/discovery for pairwise session bootstrap
3. optional sender-key distribution for group extension

Server responsibility is transport, queueing, and delivery state tracking.  
Server does **not** decrypt message ciphertext or hold private keys.

## Base URL

```
/api/e2ee
```

## Authentication

All endpoints require authenticated user context.

## Encoding Notes

- Binary fields (`byte[]`) are JSON Base64 strings in requests/responses.
- Envelope ordering is per-recipient `sequence` (monotonic increasing).
- Realtime push packet type is `e2ee.envelope` via Ring websocket.

## Enums

### Envelope Type (`SnE2eeEnvelopeType`)

| Value | Name | Meaning |
|-------|------|---------|
| `0` | `PairwiseMessage` | Normal 1:1 encrypted payload |
| `1` | `SenderKeyDistribution` | Pairwise-encrypted sender-key control payload |
| `2` | `SenderKeyMessage` | Sender-key encrypted group message payload |
| `3` | `Control` | Reserved generic control payload |

### Envelope Delivery Status (`SnE2eeEnvelopeStatus`)

| Value | Name | Meaning |
|-------|------|---------|
| `0` | `Pending` | Stored, not yet delivered |
| `1` | `Delivered` | Delivered via websocket or pending fetch |
| `2` | `Acknowledged` | Recipient acknowledged |
| `3` | `Failed` | Reserved failure state |

## Endpoints

### 1) Upload / Rotate Public Key Bundle

**Endpoint:** `POST /api/e2ee/keys/upload`

Uploads or updates the current user key bundle and appends new one-time prekeys.

**Request Body:**

```json
{
  "algorithm": "x25519",
  "identityKey": "BASE64",
  "signedPreKeyId": 1,
  "signedPreKey": "BASE64",
  "signedPreKeySignature": "BASE64",
  "signedPreKeyExpiresAt": "2026-03-07T00:00:00Z",
  "oneTimePreKeys": [
    { "keyId": 101, "publicKey": "BASE64" },
    { "keyId": 102, "publicKey": "BASE64" }
  ],
  "meta": {
    "client": "ios",
    "bundle_version": 1
  }
}
```

**Response:** `200 OK` with persisted bundle entity.

---

### 2) Get My Public Bundle

**Endpoint:** `GET /api/e2ee/keys/me`

Returns your currently published bundle (public view).

**Response:** `200 OK` or `404 Not Found`

---

### 3) Fetch Another User Public Bundle

**Endpoint:** `GET /api/e2ee/keys/{accountId}/bundle?consumeOneTimePreKey=true`

If `consumeOneTimePreKey=true`, server claims one available prekey atomically and returns it.

**Path Params:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `accountId` | uuid | Target account |

**Query Params:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `consumeOneTimePreKey` | bool | `true` | Claim and return one available OPK |

**Response Example:**

```json
{
  "accountId": "11111111-1111-1111-1111-111111111111",
  "algorithm": "x25519",
  "identityKey": "BASE64",
  "signedPreKeyId": 1,
  "signedPreKey": "BASE64",
  "signedPreKeySignature": "BASE64",
  "signedPreKeyExpiresAt": "2026-03-07T00:00:00+00:00",
  "oneTimePreKey": {
    "keyId": 101,
    "publicKey": "BASE64"
  },
  "meta": {
    "bundle_version": 1
  }
}
```

---

### 4) Ensure Pairwise Session Record

**Endpoint:** `POST /api/e2ee/sessions/{peerId}`

Creates a server-side metadata record for pairwise session lifecycle tracking (no secrets stored).

**Request Body:**

```json
{
  "hint": "x3dh-v1",
  "meta": {
    "device": "iphone-17"
  }
}
```

**Response:** `200 OK` with `SnE2eeSession`.

---

### 5) Send Encrypted Envelope

**Endpoint:** `POST /api/e2ee/messages`

Stores encrypted envelope, attempts realtime websocket push, otherwise remains queued.
Idempotency key is `clientMessageId` scoped to `(recipientId, senderId)`.

**Request Body:**

```json
{
  "recipientId": "22222222-2222-2222-2222-222222222222",
  "sessionId": "33333333-3333-3333-3333-333333333333",
  "type": 0,
  "groupId": null,
  "clientMessageId": "msg-01JFXQWQ3K",
  "ciphertext": "BASE64",
  "header": "BASE64",
  "signature": "BASE64",
  "expiresAt": "2026-03-01T00:00:00Z",
  "meta": {
    "ratchet_step": 128
  }
}
```

**Response:** `200 OK` with `SnE2eeEnvelope`.

---

### 6) Fetch Pending Envelopes

**Endpoint:** `GET /api/e2ee/messages/pending?take=100`

Returns non-acked envelopes for current user in sequence order.
Pending envelopes become `Delivered` when returned.

**Query Params:**

| Parameter | Type | Default | Min | Max |
|-----------|------|---------|-----|-----|
| `take` | int | `100` | `1` | `500` |

**Response:** `200 OK` with `SnE2eeEnvelope[]`.

---

### 7) Acknowledge Envelope

**Endpoint:** `POST /api/e2ee/messages/{envelopeId}/ack`

Marks envelope as `Acknowledged`.

**Path Params:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `envelopeId` | uuid | Envelope id |

**Response:** `200 OK` or `404 Not Found`

---

### 8) Sender Key Distribution (Group Extension)

**Endpoint:** `POST /api/e2ee/groups/sender-key/distribute`

Distributes sender key payloads to multiple recipients.  
Each item is still delivered as pairwise envelope (`type = SenderKeyDistribution`).

**Request Body:**

```json
{
  "groupId": "room:engineering",
  "expiresAt": "2026-03-01T00:00:00Z",
  "items": [
    {
      "recipientId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      "ciphertext": "BASE64",
      "header": "BASE64",
      "signature": "BASE64",
      "clientMessageId": "skd-user-a-v1",
      "meta": { "epoch": 1 }
    },
    {
      "recipientId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
      "ciphertext": "BASE64",
      "header": "BASE64",
      "signature": "BASE64",
      "clientMessageId": "skd-user-b-v1",
      "meta": { "epoch": 1 }
    }
  ]
}
```

**Response:**

```json
{
  "sent": 2
}
```

## Realtime Packet

When recipient is connected, Pass pushes a websocket packet through Ring:

- `type`: `e2ee.envelope`
- `data`: serialized envelope payload (`id`, `senderId`, `recipientId`, `sessionId`, `type`, `groupId`, `clientMessageId`, `sequence`, `ciphertext`, `header`, `signature`, `meta`, `createdAt`)

Clients should still call `GET /api/e2ee/messages/pending` on reconnect to close delivery gaps.

## Recommended Client Flow (1:1)

1. Publish key bundle: `POST /api/e2ee/keys/upload`
2. Fetch peer bundle: `GET /api/e2ee/keys/{peerId}/bundle`
3. Run X3DH (or equivalent) client-side and initialize double ratchet client-side
4. Optionally create metadata session: `POST /api/e2ee/sessions/{peerId}`
5. Send ciphertext envelope: `POST /api/e2ee/messages`
6. Receive via websocket `e2ee.envelope` and/or `GET /api/e2ee/messages/pending`
7. Decrypt client-side and ack processed envelopes

## Recommended Group Extension Flow (Sender Key)

1. Keep pairwise stack as bootstrap channel
2. Sender generates sender key material locally
3. Distribute to each member via `POST /api/e2ee/groups/sender-key/distribute`
4. Group messages use `type=SenderKeyMessage` envelopes
5. On membership changes, rotate sender keys and redistribute

## Security Notes

- Do not upload private keys/session secrets to server.
- Validate signature/header/ciphertext formats on client before decrypt.
- Use `clientMessageId` for at-least-once retry safety.
- Use envelope `expiresAt` for stale message control.
