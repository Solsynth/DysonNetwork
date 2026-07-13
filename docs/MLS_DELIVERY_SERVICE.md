# MLS Delivery Service (DysonNetwork Implementation)

## Overview

DysonNetwork implements an MLS (Messaging Layer Security) Delivery Service following [RFC 9420](https://www.rfc-editor.org/rfc/rfc9420.html) and the [MLS Architecture in RFC 9750](https://www.rfc-editor.org/rfc/rfc9750.html). The Delivery Service (DS) acts as an intermediary for MLS message delivery between clients.

## Purpose

The DS enables end-to-end encrypted chat messaging using MLS protocol:

- **Key Package Distribution** - Store and provide key packages to clients
- **Message Routing** - Queue and deliver MLS messages to group members
- **Welcome Message Handling** - Distribute welcome messages to new group members
- **Group State Management** - Bootstrap, commit, and reset MLS groups

## Design Decisions

- The DS tracks the current epoch and signed public `GroupInfo`, but never group secrets.
- Group Commit and Welcome delivery is device-scoped and ordered.
- DS stores key packages and delivers messages per device
- MLS endpoints rely on authentication, device identity, and group membership; no client-ability header is required.
- KeyPackages are single-use. Consuming reads are serialized so concurrent claims cannot return the same package twice.

## Authentication

All MLS endpoints require:
- Valid authentication token (via `[Authorize]` attribute)
- `X-Device-Id` for device-scoped delivery and group-state routes

Commit/Welcome fanout and GroupInfo read/write additionally require an active MLS device membership for the authenticated account. This is the application access-control layer required around MLS external joins.

Missing ability returns `409` with error code `e2ee.mls_ability_required`.

## API Endpoints

### Key Package Availability

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/mls/users/{accountId}/ready` | Check if a user has available key packages |
| POST | `/mls/users/ready/batch` | Batch check multiple users' key package availability |
| GET | `/mls/groups/{groupId}/devices/capable` | Get MLS capable devices for a group |

### Key Package Management

| Method | Endpoint | Description |
|--------|----------|-------------|
| PUT | `/mls/devices/me/kps` | Publish key package for current device |
| GET | `/mls/keys/{accountId}/devices` | Get key packages for a client (with consume option) |
| GET | `/mls/users/{accountId}/ready` | Check if user has available key packages |
| GET | `/mls/groups/{groupId}/devices/capable` | Get MLS capable devices for a group |

### Group State Management

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/mls/groups/{groupId}/bootstrap` | Bootstrap a new MLS group |
| POST | `/mls/groups/{groupId}/commit` | Commit group changes |
| POST | `/mls/groups/{groupId}/reset` | Reset group (delete and recreate) |
| PUT | `/mls/groups/{groupId}/groupinfo` | Publish GroupInfo for the current epoch |
| GET | `/mls/groups/{groupId}/groupinfo` | Read GroupInfo for an authorized device |

### Message Distribution

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/mls/groups/{groupId}/welcome/fanout` | Fanout Welcome messages to new members |
| POST | `/mls/groups/{groupId}/commit/fanout` | Fanout Commit messages to group members |
| POST | `/mls/messages/fanout` | Fanout MLS application messages |
| POST | `/mls/groups/{groupId}/reshare-required` | Mark reshare required for a device |

### Message Retrieval

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/mls/envelopes/pending` | Retrieve queued messages for a device |
| POST | `/mls/envelopes/{envelopeId}/ack` | Acknowledge a successfully applied message |

### Device Management

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/mls/devices/{deviceId}/revoke` | Revoke a device |

## Request/Response Types

### Publish Key Package

```json
PUT /mls/devices/me/kps
{
  "keyPackage": "base64-encoded-kp",
  "ciphersuite": "MLS_128_DHKEMX25519_AES128GCM_SHA256_Ed25519",
  "deviceId": "device-uuid",
  "deviceLabel": "optional-label"
}
```

### Fanout Message

```json
POST /mls/messages/fanout
{
  "recipientAccountId": "account-uuid",
  "groupId": "group-id",
  "includeSenderCopy": true,
  "payloads": [
    {
      "recipientDeviceId": "device-uuid",
      "clientMessageId": "optional-client-id",
      "ciphertext": "base64-encoded-message",
      "header": "base64-encoded-header",
      "signature": "base64-encoded-signature"
    }
  ]
}
```

### Get Pending Envelopes

```
GET /mls/envelopes/pending?deviceId=device-uuid&take=100
```

Envelopes are returned in device sequence order. Clients must only acknowledge a Welcome, Proposal, or Commit after OpenMLS has applied it successfully. Failed processing is intentionally redelivered.

### Publish GroupInfo

```json
PUT /mls/groups/{groupId}/groupinfo
{
  "epoch": 4,
  "group_info": "base64-encoded-group-info",
  "ratchet_tree": "base64-encoded-ratchet-tree"
}
```

The supplied epoch must equal the delivery service's current epoch. Stale clients receive `409 e2ee.mls_epoch_mismatch`; they cannot overwrite recovery state for newer members.

### Batch Check User Availability

```json
POST /mls/users/ready/batch
{
  "accountIds": ["uuid-1", "uuid-2", "uuid-3"]
}

Response:
{
  "users": [
    { "accountId": "uuid-1", "isReady": true, "availableKeyPackages": 5 },
    { "accountId": "uuid-2", "isReady": false, "availableKeyPackages": 0 },
    { "accountId": "uuid-3", "isReady": true, "availableKeyPackages": 3 }
  ]
}
```

## Envelope Types

The DS handles the following envelope types:

- `MlsCommit` - Group commit messages
- `MlsWelcome` - Welcome messages for new members
- `MlsApplication` - Application messages (chat content)
- `MlsProposal` - Proposal messages (add/remove members)

## Message Flow

### Registering a Device

1. Client creates MLS key packages
2. Client sends key package to `PUT /mls/devices/me/kps`
3. DS stores the key package for distribution

### Creating a Group

1. Client1 reserves bootstrap ownership and creates the MLS group.
2. Client1 registers its device membership through Messager.
3. Client1 consumes one KeyPackage per target device.
4. OpenMLS creates a pending Commit and Welcome.
5. Client1 durably fanouts the Commit at `current_epoch + 1` and the Welcome.
6. Only after both fanouts succeed, Client1 calls `mergePendingCommit`.
7. Client2 processes the Welcome, registers device membership, then acknowledges the envelope.

### Sending Group Messages

1. Client creates a message for the group
2. Client sends fanout request (`POST /mls/messages/fanout`) with payloads for each recipient device
3. DS queues the message for each recipient device
4. Recipients retrieve their messages via `GET /mls/envelopes/pending`

## Server Guardrails

- **Key Package Upload Rate Limit**: Max 10 key packages per account per 24 hours
- **Key Package Auto-Purge**: Key packages older than 30 days are deleted
- **Fanout Payload Cap**: Max 1000 payloads per request
- **Device Completeness**: All active target devices must have a payload
- **Epoch Consistency**: chat writes and GroupInfo uploads must match the current Padlock epoch
- **Bootstrap Idempotency**: replay never rolls an existing group back to epoch zero

## Device Revocation

When a device is revoked (`POST /mls/devices/{deviceId}/revoke`):

1. Device is marked as revoked immediately
2. All pending envelopes targeting the revoked device are purged
3. Sibling active devices receive control envelope for reshare/recovery

## Contract Markers

- Chat encryption scheme marker: `chat.mls.v2`
