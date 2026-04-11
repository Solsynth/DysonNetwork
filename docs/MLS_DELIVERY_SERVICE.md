# MLS Delivery Service (DysonNetwork Implementation)

## Overview

DysonNetwork implements an MLS (Messaging Layer Security) Delivery Service as defined in [The MLS Architecture](https://messaginglayersecurity.rocks/mls-architecture/draft-ietf-mls-architecture.html). The Delivery Service (DS) acts as an intermediary for MLS message delivery between clients.

## Purpose

The DS enables end-to-end encrypted chat messaging using MLS protocol:

- **Key Package Distribution** - Store and provide key packages to clients
- **Message Routing** - Queue and deliver MLS messages to group members
- **Welcome Message Handling** - Distribute welcome messages to new group members
- **Group State Management** - Bootstrap, commit, and reset MLS groups

## Design Decisions

- The DS does not know about MLS group internal state (epochs, proposals, etc.)
- Clients must send recipient list (group members) with each message
- DS stores key packages and delivers messages per device
- All MLS endpoints require the `X-Client-Ability: chat.mls.v2` header

## Authentication

All MLS endpoints require:
- Valid authentication token (via `[Authorize]` attribute)
- `X-Client-Ability` header with token `chat.mls.v2`

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
| POST | `/mls/envelopes/{envelopeId}/ack` | Acknowledge and delete a message |

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

1. Client1 creates an MLS group and adds Client2
2. Client1 gets key packages for Client2 (`GET /mls/keys/{accountId}/devices`)
3. Client1 creates a Welcome message and fanouts to DS (`POST /mls/groups/{groupId}/welcome/fanout`)
4. DS stores the Welcome message for Client2's devices
5. Client2 retrieves the Welcome message (`GET /mls/envelopes/pending`)
6. Client2 acknowledges receipt (`POST /mls/envelopes/{id}/ack`)

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

## Device Revocation

When a device is revoked (`POST /mls/devices/{deviceId}/revoke`):

1. Device is marked as revoked immediately
2. All pending envelopes targeting the revoked device are purged
3. Sibling active devices receive control envelope for reshare/recovery

## Contract Markers

- Chat encryption scheme marker: `chat.mls.v2`
- Ability header: `X-Client-Ability`
