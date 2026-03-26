# Nearby API

This document describes the current backend implementation of the `nearby` feature in Passport.

## Overview

`nearby` is the anonymous presence-discovery layer that sits in front of meet/invite flows.

The backend currently provides:

- rolling presence token issuance
- token resolution into visible nearby peers

The current implementation does not yet include:

- heartbeat
- nearby invites
- invite accept / reject
- observation log persistence

## Design Summary

The BLE payload should contain short-lived anonymous rolling tokens rather than user profile data.

Server responsibilities:

- issue rolling tokens for future time slots
- store only token hashes
- resolve scanned tokens back into visible users
- enforce discoverability and friend-only visibility
- filter blocked users

## Configuration

Configured in [appsettings.json](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/appsettings.json):

- `Nearby:ServiceUuid`
- `Nearby:SlotDurationSec`
- `Nearby:PresenceTokenSecret`

Defaults in this implementation:

- `service_uuid = FFF0`
- `slot_duration_sec = 30`
- `prefetch_slots = 10`

## Data Model

Implemented in [Nearby.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Shared/Models/Nearby.cs).

### `nearby_devices`

Fields:

- `id`
- `user_id`
- `device_id`
- `discoverable`
- `friend_only`
- `capabilities`
- `status`
- `last_heartbeat_at`
- `last_token_issued_at`
- `created_at`
- `updated_at`

### `nearby_presence_tokens`

Fields:

- `id`
- `device_id`
- `slot`
- `token_hash`
- `valid_from`
- `valid_to`
- `discoverable`
- `friend_only`
- `capabilities`
- `created_at`
- `updated_at`

Only the token hash is stored.

## Token Strategy

Tokens are deterministic rolling tokens generated from:

- user ID
- device ID
- slot
- discoverable flag
- friend-only flag
- capabilities
- server secret

Implementation reference:

- [NearbyService.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Nearby/NearbyService.cs)

The current token format returned to clients is a 16-byte value encoded as uppercase hex.

## Authentication

All nearby endpoints require authentication.

The current user is read from `HttpContext.Items["CurrentUser"]`.

## Endpoints

### Issue presence tokens

`POST /api/nearby/presence-tokens`

Issues rolling tokens for future slots for a specific client device.

Request body:

```json
{
  "device_id": "device_xxx",
  "discoverable": true,
  "friend_only": true,
  "capabilities": 0,
  "prefetch_slots": 10
}
```

Response:

```json
{
  "service_uuid": "FFF0",
  "slot_duration_sec": 30,
  "tokens": [
    {
      "slot": 58765432,
      "token": "A1B2C3D4E5F60718293A4B5C6D7E8F90",
      "valid_from": "2026-03-21T12:00:00Z",
      "valid_to": "2026-03-21T12:00:30Z"
    }
  ]
}
```

Behavior:

- creates or updates the nearby device record
- marks the device active
- pre-generates future slot tokens
- stores only token hashes in the database
- reissues future tokens if visibility/capability settings changed

### Resolve scanned observations

`POST /api/nearby/resolve`

Resolves scanned rolling tokens into visible nearby peers.

Request body:

```json
{
  "observations": [
    {
      "token": "A1B2C3D4E5F60718293A4B5C6D7E8F90",
      "slot": 58765432,
      "avg_rssi": -67,
      "seen_count": 4,
      "duration_ms": 5200,
      "first_seen_at": "2026-03-21T12:00:03Z",
      "last_seen_at": "2026-03-21T12:00:08Z"
    }
  ]
}
```

Response:

```json
{
  "peers": [
    {
      "user_id": "11111111-2222-3333-4444-555555555555",
      "display_name": "Alice",
      "avatar": null,
      "is_friend": true,
      "can_invite": true,
      "visibility": "friend_only",
      "last_seen_at": "2026-03-21T12:00:08Z"
    }
  ]
}
```

Behavior:

- hashes the observed token
- finds matching valid token rows by hash and slot
- enforces time-window validity
- skips the current user
- requires the device to still be discoverable and active
- enforces `friend_only`
- filters blocked relationships in both directions
- returns a deduplicated peer list

## Visibility Rules

The resolver only returns a peer when all of these checks pass:

- token exists
- slot matches
- token is still in the allowed time window
- device is active
- device is discoverable
- observer is allowed to see the target
- neither side has blocked the other

For `friend_only = true`, only friends can resolve the peer.

## Notes For Clients

Recommended client behavior:

- fetch tokens ahead of time
- rotate broadcast token every slot
- aggregate observations locally before calling `resolve`
- treat nearby peers as recently seen, not perfectly real-time

Recommended initial thresholds from the product design:

- `slot_duration_sec = 30`
- `resolve_min_seen_count = 2`
- `resolve_min_duration_ms = 3000`
- `resolve_min_avg_rssi = -75`

## Client Implementation Guide

This section describes how the client should integrate with the current backend.

### Current Client Scope

With the current backend, the client should implement:

- token fetch
- BLE advertising with rolling token payload
- BLE scanning
- local observation aggregation
- resolve request submission
- nearby peer list rendering

The client does not yet need to implement heartbeat or nearby invite requests because those backend endpoints are not shipped yet.

### Suggested Client Modules

Recommended separation:

- `NearbyPresenceService`
- `NearbyScannerService`
- `NearbyResolveService`
- `NearbyController`

Suggested responsibilities:

- `NearbyPresenceService`
  Rotates current token, manages advertising, refreshes token batches before expiry.
- `NearbyScannerService`
  Scans for matching `service_uuid`, parses payload, and aggregates local observations.
- `NearbyResolveService`
  Sends ready observations to `/api/nearby/resolve` and merges returned peers.
- `NearbyController`
  Owns UI state, permissions, Bluetooth state, and orchestration.

### Suggested Client State

Example state ideas:

```dart
enum NearbyMode {
  off,
  starting,
  running,
  permissionDenied,
  bluetoothOff,
  unsupported,
  error,
}
```

```dart
class PresenceToken {
  final int slot;
  final String token;
  final DateTime validFrom;
  final DateTime validTo;
}
```

```dart
class NearbyPeer {
  final String userId;
  final String displayName;
  final Map<String, dynamic>? avatar;
  final bool isFriend;
  final bool canInvite;
  final String visibility;
  final DateTime lastSeenAt;
}
```

### Presence Token Flow

Recommended flow:

1. User enables nearby.
2. Client calls `POST /api/nearby/presence-tokens`.
3. Backend returns `service_uuid`, `slot_duration_sec`, and future rolling tokens.
4. Client starts BLE advertising using the token for the current slot.
5. Client rotates the advertising payload whenever the slot changes.
6. Before the last few tokens expire, client fetches a fresh token batch again.

### BLE Payload Format

Recommended payload layout:

- `magic`: 1 byte
- `version`: 1 byte
- `flags`: 1 byte
- `slot`: 4 bytes
- `rolling_presence_token`: 16 bytes
- `capabilities`: 1 byte optional

Suggested total payload size:

- 23 bytes without `capabilities`
- 24 bytes with `capabilities`

This only works reliably when the nearby protocol uses a 16-bit service UUID and keeps the payload in service data only. A 128-bit service UUID adds too much advertising overhead on Android, and a separate advertised UUID block can also push the packet over the limit.

Recommended flag meanings:

- bit 0: discoverable
- bit 1: accepts invite
- bit 2: do not disturb
- bit 3: friend-only

Recommended magic marker:

- `0xD9` at byte `0`

This lets the scanner cheaply reject unrelated service data before parsing the rest of the packet.

### Token Encoding

The backend currently returns `token` as uppercase hex text.

Before placing the token into BLE payload bytes, the client should decode the hex string into raw 16 bytes.

### Example Payload Builder

```dart
Uint8List buildPresencePayload({
  required int magic,
  required int version,
  required int flags,
  required int slot,
  required Uint8List token,
  required int capabilities,
}) {
  final builder = BytesBuilder();
  builder.addByte(magic);
  builder.addByte(version);
  builder.addByte(flags);
  builder.add(_u32be(slot));
  builder.add(token);
  builder.addByte(capabilities);
  return builder.toBytes();
}
```

### Example Payload Parser

```dart
class PresencePayload {
  final int magic;
  final int version;
  final int flags;
  final int slot;
  final Uint8List token;
  final int capabilities;

  PresencePayload({
    required this.magic,
    required this.version,
    required this.flags,
    required this.slot,
    required this.token,
    required this.capabilities,
  });
}

PresencePayload parsePresencePayload(Uint8List data) {
  return PresencePayload(
    magic: data[0],
    version: data[1],
    flags: data[2],
    slot: _readU32be(data, 3),
    token: data.sublist(7, 23),
    capabilities: data.length > 23 ? data[23] : 0,
  );
}
```

### Advertising Flow

Recommended client flow:

1. Decode current token hex into 16 bytes.
2. Build payload with current slot.
3. Start advertising under the returned `service_uuid`.
4. Stop and restart advertising when the slot changes.

Android-specific guidance:

- use a 16-bit service UUID such as `FFF0`
- keep the nearby payload in `serviceData`
- do not add `localName`
- avoid extra service UUIDs
- add a defensive client-side size check before advertising

Example defensive check:

```dart
if (payload.length > 24) {
  throw Exception('BLE payload too large');
}
```

Recommended advertising shape:

```dart
AdvertiseData(
  serviceData: {
    Uuid.parse('FFF0'): payload,
  },
)
```

### Manufacturer Data Presentation

If the client also wants a manufacturer-data copy for easier low-level inspection on some platforms, keep it secondary and compact.

Suggested layout:

- manufacturer company id: your assigned company identifier in production
- byte 0: same magic marker
- byte 1: version
- byte 2: flags
- byte 3-6: slot
- byte 7-22: rolling token
- byte 23: capabilities optional

Recommendations:

- keep `serviceData` as the source of truth for the nearby protocol
- only mirror the same payload into manufacturer data when you have a platform-specific reason
- use a real Bluetooth company identifier in production rather than a placeholder
- if both are present, scanners should prefer `serviceData` and treat manufacturer data as advisory

Pseudo-example:

```dart
await nearbyApi.issuePresenceTokens(...);
await peripheralManager.startAdvertising(...);
```

### Scanning Flow

Recommended client flow:

1. Scan for the fixed `service_uuid`.
2. Extract service data.
3. Parse payload.
4. Aggregate observations by `slot + token`.
5. Submit only stable observations to `/api/nearby/resolve`.

### Observation Aggregation

Do not resolve on a single scan frame.

Recommended local aggregation fields:

- `seenCount`
- `rssiSum`
- `firstSeenAt`
- `lastSeenAt`

Recommended initial resolve thresholds:

- `seenCount >= 2`
- `durationMs >= 3000`
- `avgRssi >= -75`

### Resolve Request Flow

Recommended flow:

1. Convert raw token bytes back to uppercase hex for API submission.
2. Send grouped observations to `POST /api/nearby/resolve`.
3. Merge returned peers into UI state.
4. Remove stale peers after a short TTL.

Suggested UI TTL behavior:

- mark stale after `15` seconds without refresh
- remove after `30` seconds without refresh

### Suggested Client Request Sequence

Startup:

1. Request tokens.
2. Start advertising.
3. Start scanning.

Runtime loop:

1. Rotate token at slot boundary.
2. Aggregate scan observations.
3. Periodically resolve ready observations.
4. Refresh token batch before local supply runs out.

### Privacy Expectations

The client should not place these in BLE payload:

- user ID
- display name
- avatar URL
- profile text
- exact location
- fixed device identifier

BLE payload should only carry the anonymous rolling token plus lightweight protocol flags.

### Platform Notes

The Flutter side should still account for platform restrictions:

- Android 12+ requires Bluetooth scan/advertise/connect permissions.
- iOS background BLE behavior is limited and best-effort rather than guaranteed real-time operation.
- Nearby UI should be phrased as recently seen or recently nearby, not as exact real-time radar.

### Suggested Flutter Plugin Direction

Based on the current product direction:

- BLE: `bluetooth_low_energy`
- permissions: `permission_handler`

If Android needs stronger background persistence later, use a native foreground service path instead of forcing everything into pure Flutter logic.

## Cleanup

Expired nearby presence tokens are cleaned by the Passport recycling job.

Current cleanup behavior:

- deletes tokens older than `valid_to + 5 minutes`

## Migration

Schema migration:

- [20260321115431_AddNearbyPresenceTokens.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Migrations/20260321115431_AddNearbyPresenceTokens.cs)

## Implementation References

- Controller: [NearbyController.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Nearby/NearbyController.cs)
- Service: [NearbyService.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Nearby/NearbyService.cs)
- Models: [Nearby.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Shared/Models/Nearby.cs)
- Database wiring: [AppDatabase.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/AppDatabase.cs)
