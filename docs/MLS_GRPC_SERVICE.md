# Chat Service API

## Overview

Chat Service provides real-time messaging with end-to-end encryption support via MLS (Messaging Layer Security).

## Base URL

```
/api/chat
```

## Authentication

All endpoints require JWT authentication unless noted.

## Encryption Modes

| Mode | Value | Description |
|------|-------|-------------|
| `None` | 0 | No encryption |
| `E2eeMls` | 3 | MLS end-to-end encryption |

Legacy encryption modes (`E2eeDm = 1`, `E2eeSenderKeyGroup = 2`) are retired.

## Required Headers

For encrypted rooms (MLS), clients must include:

```
X-Client-Ability: chat.mls.v2
```

## Chat Rooms

### Get Chat Room

```
GET /api/chat/{id}
```

Returns room details including encryption mode.

### List Joined Rooms

```
GET /api/chat
```

Returns all rooms the authenticated user is a member of.

### Create Chat Room

```
POST /api/chat
```

**Request Body:**
```json
{
  "name": "Room Name",
  "description": "Optional description",
  "pictureId": "optional-file-id",
  "backgroundId": "optional-file-id",
  "isPublic": false,
  "isCommunity": false,
  "realmId": "optional-realm-guid",
  "encryptionMode": 0  // 0 = None, 3 = MLS
}
```

### Create Direct Message

```
POST /api/chat/direct
```

**Request Body:**
```json
{
  "relatedUserId": "guid",
  "encryptionMode": 0  // or 3 for MLS
}
```

### Enable MLS Encryption

```
POST /api/chat/{id}/mls/enable
```

One-way transition from unencrypted to MLS. Cannot be disabled.

**Request Body:**
```json
{
  "mlsGroupId": "optional-custom-group-id",
  "e2eePolicy": {
    "ciphersuite": "MLS_128_DHKEMX25519_AES128GCM_SHA256_Ed25519"
  }
}
```

## Room Membership

### List Members

```
GET /api/chat/{roomId}/members?take=20&offset=0&withStatus=false
```

### Invite Member

```
POST /api/chat/invites/{roomId}
```

**Request Body:**
```json
{
  "relatedUserId": "guid",
  "role": 0
}
```

### Accept Invite

```
POST /api/chat/invites/{roomId}/accept
```

### Decline Invite

```
POST /api/chat/invites/{roomId}/decline
```

### Leave Room

```
DELETE /api/chat/{roomId}/members/me
```

### Remove Member

```
DELETE /api/chat/{roomId}/members/{memberId}
```

## Messages

### List Messages

```
GET /api/chat/{roomId}/messages?offset=0&take=20
```

Returns paginated messages. **Note:** Encrypted message content will be opaque ciphertext.

### Send Message

```
POST /api/chat/{roomId}/messages
```

**Plaintext Request (unencrypted rooms):**
```json
{
  "content": "Hello world",
  "nonce": "optional-client-nonce",
  "clientMessageId": "optional-id",
  "attachmentsId": ["file-id-1"],
  "repliedMessageId": "optional-message-guid",
  "forwardedMessageId": "optional-message-guid",
  "meta": {}
}
```

**Encrypted Request (MLS rooms):**
```json
{
  "isEncrypted": true,
  "ciphertext": "<binary>",
  "encryptionHeader": "<binary>",
  "encryptionSignature": "<binary>",
  "encryptionScheme": "chat.mls.v2",
  "encryptionEpoch": 1,
  "encryptionMessageType": "text"
}
```

For encrypted rooms:
- `content` must be null/empty
- `attachmentsId` is stored as metadata reference only
- `fundId` and `pollId` are forbidden
- Client must include `X-Client-Ability: chat.mls.v2` header

### Edit Message

```
PATCH /api/chat/{roomId}/messages/{messageId}
```

Same format as send, with additional fields.

### Delete Message

```
DELETE /api/chat/{roomId}/messages/{messageId}
```

**Request Body (for encrypted rooms):**
```json
{
  "ciphertext": "<binary>",
  "encryptionHeader": "<binary>",
  "encryptionSignature": "<binary>",
  "encryptionScheme": "chat.mls.v2",
  "encryptionEpoch": 1,
  "encryptionMessageType": "messages.delete"
}
```

## Sync

### Sync Room Messages

```
POST /api/chat/{roomId}/sync
```

**Request Body:**
```json
{
  "lastSyncTimestamp": 1699999999999
}
```

**Response:**
```json
{
  "messages": [...],
  "currentTimestamp": 1700000000000,
  "totalCount": 42
}
```

### Global Sync

```
POST /api/chat/sync
```

Syncs all rooms at once.

## Reactions

### Add/Remove Reaction

```
POST /api/chat/{roomId}/messages/{messageId}/reactions
```

**Request Body:**
```json
{
  "symbol": "heart",
  "attitude": 0
}
```

### Remove Reaction

```
DELETE /api/chat/{roomId}/messages/{messageId}/reactions/{symbol}
```

## System Events

MLS rooms emit system events for encryption state changes:

| Event | Trigger |
|-------|---------|
| `system.e2ee.enabled` | MLS encryption enabled |
| `system.mls.epoch_changed` | Membership change committed |
| `system.mls.reshare_required` | Device needs re-share |

## Error Codes

| Code | Description |
|------|-------------|
| `chat.e2ee_required` | Room requires MLS capability |
| `chat.e2ee_payload_required` | Missing encrypted payload |
| `chat.mls_payload_required` | MLS rooms require `chat.mls.v2` scheme and epoch |
| `chat.e2ee_ciphertext_invalid` | Ciphertext appears to be plaintext JSON |
| `chat.e2ee_plaintext_forbidden` | Plaintext fields not allowed in encrypted rooms |
| `chat.e2ee_legacy_mode_forbidden` | Legacy encryption modes disabled |
| `chat.e2ee_dm_member_limit` | MLS DMs limited to 2 members |

## E2EE MLS Endpoints

Clients interact with E2EE Service directly for MLS operations.

**Base URL:** `/api/e2ee/mls`

**Required Headers:**
- `X-Client-Ability: chat.mls.v2`
- `X-Device-Id: <device-id>` (for device-specific endpoints)

### Group Info

```
PUT /api/e2ee/mls/groups/{groupId}/groupinfo
GET /api/e2ee/mls/groups/{groupId}/groupinfo
```

**PUT Request Body:**
```json
{
  "groupInfo": "<binary>",
  "ratchetTree": "<binary>"
}
```

**PUT Response:**
```json
{
  "success": true,
  "groupId": "room:abc123",
  "epoch": 1
}
```

**GET Response:**
```json
{
  "groupId": "room:abc123",
  "epoch": 1,
  "groupInfo": "<binary>",
  "ratchetTree": "<binary>"
}
```

### Reshare Required

```
GET /api/e2ee/mls/devices/me/reshare-required
POST /api/e2ee/mls/devices/me/reshare-required/{groupId}/complete
POST /api/e2ee/mls/groups/{groupId}/reshare-required
```

**GET /api/e2ee/mls/devices/me/reshare-required:** Check if current device needs re-share for any groups.

**POST /api/e2ee/mls/devices/me/reshare-required/{groupId}/complete:** Mark re-share as completed for a group.

**Required Header:** `X-Device-Id: <device-id>`

**GET Response:**
```json
[
  {
    "id": "guid",
    "mlsGroupId": "room:abc123",
    "accountId": "guid",
    "deviceId": "device-123",
    "joinedEpoch": 1,
    "lastSeenEpoch": 3,
    "lastReshareRequiredAt": "2024-01-01T00:00:00Z",
    "lastReshareCompletedAt": null
  }
]
```

**POST Mark Request Body:**
```json
{
  "targetAccountId": "guid",
  "targetDeviceId": "device-123",
  "epoch": 1,
  "reason": "device_added"
}
```

### Other E2EE MLS Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| PUT | `/api/e2ee/mls/devices/me/kps` | Publish MLS KeyPackage |
| GET | `/api/e2ee/mls/kp/status` | Get KeyPackage status |
| GET | `/api/e2ee/mls/keys/{accountId}/devices` | List KeyPackages by device |
| POST | `/api/e2ee/mls/users/ready/batch` | Batch check MLS readiness |
| GET | `/api/e2ee/mls/users/{accountId}/ready` | Check if user is MLS ready |
| GET | `/api/e2ee/mls/groups/{groupId}/devices/capable` | Get capable devices |
| POST | `/api/e2ee/mls/groups/{groupId}/bootstrap` | Bootstrap MLS group |
| POST | `/api/e2ee/mls/groups/{groupId}/commit` | Commit MLS group changes |
| POST | `/api/e2ee/mls/groups/{groupId}/welcome/fanout` | Fanout welcome messages |
| GET | `/api/e2ee/mls/devices/me/reshare-required` | Get my device re-share status |
| POST | `/api/e2ee/mls/devices/me/reshare-required/{groupId}/complete` | Complete re-share |
| POST | `/api/e2ee/mls/groups/{groupId}/reshare-required` | Mark re-share required |
| POST | `/api/e2ee/mls/messages/fanout` | Send MLS message fanout |
| POST | `/api/e2ee/mls/groups/{groupId}/commit/fanout` | Fanout commit |
| GET | `/api/e2ee/mls/envelopes/pending` | Get pending envelopes |
| POST | `/api/e2ee/mls/envelopes/{envelopeId}/ack` | Acknowledge envelope |
| POST | `/api/e2ee/mls/devices/{deviceId}/revoke` | Revoke MLS device |
| POST | `/api/e2ee/mls/groups/{groupId}/reset` | Reset MLS group |

## MLS Integration (Internal)

Chat Service integrates with E2EE Service (Padlock) via gRPC for MLS operations:

```
┌─────────────────────────────────┐     gRPC      ┌─────────────────────┐
│         Chat Service            │◄──────────────│   E2EE Service      │
│        (Messager)               │               │     (Padlock)       │
│                                 │               │                     │
│  - User-level membership        │               │  - Device membership│
│  - Authorizes group operations  │               │  - KeyPackage storage│
│  - Room metadata                │               │  - MLS message fanout│
└─────────────────────────────────┘               └─────────────────────┘
```

### gRPC Methods

| Method | Purpose |
|--------|---------|
| `SendMlsMessage` | Fan out MLS ciphertext to group members |
| `GetGroupInfo` | Retrieve GroupInfo/RatchetTree for external join |
| `UploadGroupInfo` | Store GroupInfo/RatchetTree after external join |
| `JoinGroupExternal` | Initiate external join |
| `CommitGroupChanges` | Commit membership changes |
| `PublishWelcome` | Send welcome to new member devices |
| `GetKeyPackages` | Retrieve KeyPackages for group members |
| `MarkReshareRequired` | Request device re-share |
| `GetGroupState` | Get current group epoch |
| `DeleteGroup` | Remove group state |

### External Join Flow

1. Client obtains `GroupInfo` from existing member
2. Client uploads via `PUT /api/e2ee/mls/groups/{groupId}/groupinfo`
3. E2EE Service stores `GroupInfo`/`RatchetTree` in `SnMlsGroupState`
4. Client retrieves via `GET /api/e2ee/mls/groups/{groupId}/groupinfo` to construct external commit
