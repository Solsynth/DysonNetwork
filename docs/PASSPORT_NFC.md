# NFC Tag API

This document describes the backend implementation of the NFC tag feature in Passport.

## Overview

NFC tags serve as physical identity cards on the Solar Network. A user taps an NFC tag with their phone, and the system resolves it to the tag owner's profile — enabling quick social interactions like viewing a profile or adding a friend.

The backend provides:

- NTAG424 Secure Unique NFC (SUN) verification
- MAC-based tag authentication
- Replay attack prevention via read counters
- Tag registration and lifecycle management

## Design Decisions

### Why NTAG424 SUN

NTAG424 tags support Secure Unique NFC (SUN) / Secure Dynamic Messaging (SDM). Each scan generates a unique URL with encrypted data, a counter, and a cryptographic MAC signature.

Compared to a plain URL like `https://solian.app/u/123`:

| | Plain URL | SUN URL |
|---|---|---|
| Copyable | Yes | No (changes each scan) |
| Forgeable | Yes | No (signed with tag key) |
| Replay detectable | No | Yes (counter-based) |

### Security model

This feature targets **social identification**, not authentication. The threat model is:

- Prevent copying a tag's URL and replaying it later
- Prevent forging a tag that impersonates another user
- Not required to resist sophisticated physical attacks on the tag itself

### Architecture

All security logic lives on the server. The client (app) simply:

1. Reads the NFC tag via platform NFC APIs
2. Extracts the URL from the tag
3. Calls `GET /api/nfc` with the URL parameters
4. Displays the returned user profile

The client never handles encryption, key storage, or MAC verification.

## Configuration

Configured in [appsettings.json](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/appsettings.json):

```json
"Nfc": {
    "ReplayWindow": 0
}
```

| Key | Default | Description |
|---|---|---|
| `ReplayWindow` | `0` | How many previous counter values to accept. `0` = strict (each counter used once). Higher values tolerate concurrent scan races. |

## Data Model

Implemented in [SnNfcTag.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Nfc/SnNfcTag.cs).

### `nfc_tags`

| Column | Type | Description |
|---|---|---|
| `id` | `uuid` | Primary key |
| `uid` | `text` | Chip UID from SUN decryption. Unique index. |
| `user_id` | `uuid` | Owner account ID. Indexed. |
| `sun_key` | `bytea` | Per-tag AES-128 SUN key (16 bytes) |
| `counter` | `int` | Last seen read counter for replay protection |
| `label` | `text` | Optional user label (e.g., "Work Card") |
| `is_active` | `bool` | Soft disable flag |
| `locked_at` | `timestamptz` | When the tag was locked against reprogramming |
| `last_seen_at` | `timestamptz` | Last successful scan timestamp |
| `created_at` | `timestamptz` | Record creation time |
| `updated_at` | `timestamptz` | Last update time |
| `deleted_at` | `timestamptz` | Soft delete timestamp |

## SUN Verification Flow

When a tag is scanned, NTAG424 generates a URL like:

```
https://solian.app/nfc?e=BASE64_ENC&c=COUNTER&mac=BASE64_MAC
```

The server performs these steps:

```
1. Decode parameters
   e   → encrypted PICCData bytes (AES-CBC, zero IV)
   c   → read counter (int)
   mac → AES-CMAC signature (16 bytes)

2. Load all active tags from database

3. For each candidate tag:
   a. Compute AES-CMAC(tag.sun_key, e || c_as_3bytes_le)
   b. Compare with received mac (constant-time)
   c. If match → AES-CBC decrypt e with tag.sun_key
   d. Extract UID from decrypted PICCData
   e. Verify UID matches tag.uid

4. Counter check (replay protection):
   counter must be > tag.counter - ReplayWindow

5. On success:
   - Update tag.counter and tag.last_seen_at
   - Fetch user profile via AccountService
   - Check relationship status if observer is authenticated
   - Return user info + available actions
```

### Crypto implementation details

| Operation | Algorithm | Notes |
|---|---|---|
| MAC verification | AES-128-CMAC (NIST SP 800-38B / RFC 4493) | Pure .NET, no external deps |
| PICCData decryption | AES-CBC with zero IV | No padding validation (NTAG424 uses PKCS7 to 16 bytes) |
| Replay detection | Counter comparison | Configurable window via `Nfc:ReplayWindow` |

### PICCData format

After decryption, the PICCData layout is:

```
Byte 0:     PICCDataTag (0x08 for UID)
Bytes 1-7:  UID (7 bytes)
Bytes 8-10: SDMReadCtr (3 bytes, little-endian)
Bytes 11-15: PKCS7 padding
```

## Authentication

| Endpoint | Auth | Description |
|---|---|---|
| `GET /api/nfc/lookup` | Optional | Look up tag by UID (admin/debug only, no MAC verification) |
| `GET /api/nfc` | Optional | Public scan resolution. If JWT present, includes relationship info. |
| `GET /api/nfc/tags` | Required | List user's tags |
| `POST /api/nfc/tags` | Required | Register a tag |
| `PATCH /api/nfc/tags/{id}` | Required | Update tag metadata |
| `POST /api/nfc/tags/{id}/lock` | Required | Lock a tag |
| `DELETE /api/nfc/tags/{id}` | Required | Unregister a tag |

The current user is read from `HttpContext.Items["CurrentUser"]`.

## Endpoints

### Lookup tag by UID

`GET /api/nfc/lookup?uid={uid}`

No authentication required. If JWT present, relationship info is included.

**Warning**: This endpoint bypasses SUN MAC verification and replay protection. Intended for admin/debug/testing only.

Parameters:

| Param | Type | Required | Description |
|---|---|---|---|
| `uid` | string | Yes | Chip UID (hex string) |

Response (200):

```json
{
  "user": {
    "id": "11111111-2222-3333-4444-555555555555",
    "name": "alice",
    "nick": "Alice",
    "picture": null,
    "bio": "Hello world"
  },
  "is_friend": false,
  "actions": ["view_profile", "add_friend"]
}
```

Error responses:

| Status | Code | When |
|---|---|---|
| 400 | `VALIDATION_ERROR` | Missing uid parameter |
| 404 | `NOT_FOUND` | Tag not found |

### Resolve NFC tag

`GET /api/nfc?e={enc}&c={counter}&mac={mac}`

No authentication required. If the caller is authenticated, relationship status is included.

Parameters:

| Param | Type | Required | Description |
|---|---|---|---|
| `e` | string | Yes | Base64-encoded encrypted PICCData |
| `c` | string | Yes | Read counter (integer) |
| `mac` | string | Yes | Base64-encoded AES-CMAC signature |

Response (200):

```json
{
  "user": {
    "id": "11111111-2222-3333-4444-555555555555",
    "name": "alice",
    "nick": "Alice",
    "picture": null,
    "bio": "Hello world"
  },
  "is_friend": false,
  "actions": ["view_profile", "add_friend"]
}
```

Error responses:

| Status | Code | When |
|---|---|---|
| 400 | `VALIDATION_ERROR` | Missing or malformed parameters |
| 404 | `NOT_FOUND` | Tag not found or MAC verification failed |
| 410 | (future) | Tag is disabled |

Behavior:

- verifies AES-CMAC across all active tags
- decrypts PICCData to extract UID
- checks counter against last known value
- skips blocked users in both directions
- updates tag counter and last-seen timestamp
- returns available actions based on auth state

### List tags

`GET /api/nfc/tags`

Requires JWT.

Response (200):

```json
[
  {
    "id": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
    "label": "Work Card",
    "is_active": true,
    "is_locked": false,
    "last_seen_at": "2026-03-29T08:00:00Z",
    "created_at": "2026-03-29T06:00:00Z"
  }
]
```

### Register tag

`POST /api/nfc/tags`

Requires JWT.

Request body:

```json
{
  "uid": "04A1B2C3D4E5F6",
  "sun_key": "AAAAAAAAAAAAAAAAAAAAAA==",
  "label": "Work Card"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `uid` | string | Yes | Chip UID (hex, up to 32 chars) |
| `sun_key` | string | Yes | Base64-encoded 16-byte AES-128 key |
| `label` | string | No | User-facing label (max 64 chars) |

Response (200): Tag DTO

Error responses:

| Status | Code | When |
|---|---|---|
| 400 | `VALIDATION_ERROR` | Invalid key format or length |
| 409 | `NFC_TAG_EXISTS` | UID already registered |

### Update tag

`PATCH /api/nfc/tags/{tagId}`

Requires JWT. Owns the tag.

Request body:

```json
{
  "label": "Personal Card",
  "is_active": true
}
```

Response (200): Updated tag DTO

### Lock tag

`POST /api/nfc/tags/{tagId}/lock`

Requires JWT. Owns the tag.

Sets `locked_at` to current time. This indicates the physical tag has been locked against reprogramming.

Response (200): Updated tag DTO with `is_locked: true`

### Unregister tag

`DELETE /api/nfc/tags/{tagId}`

Requires JWT. Owns the tag.

Soft-deletes the tag (sets `deleted_at`, marks `is_active = false`).

Response: 204 No Content

## Tag Registration Flow

The physical tag programming and server registration are separate operations:

### Server-side (what the API handles)

1. User generates a unique AES-128 key
2. User calls `POST /api/nfc/tags` with the chip UID and key
3. Server stores the mapping: UID → user_id + sun_key

### Physical tag (done with NFC writer tool)

1. Configure NTAG424 SDM settings:
   - Enable SUN (Secure Unique NFC)
   - Set SDM MAC input: PICCData + ReadCtr
   - Set URL template: `https://solian.app/nfc?e={ENC}&c={CNT}&mac={MAC}`
2. Write the SUN key to the tag's authentication keys
3. Lock the tag (optional but recommended)

### Recommended NTAG424 SDM configuration

| Setting | Value | Notes |
|---|---|---|
| SDMReadCtr | Enabled | Counter increments on each read |
| SDMENCFileData | Enabled | Encrypts PICCData |
| SDMMACInput | PICCData + ReadCtr | MAC covers both encrypted data and counter |
| SDMMAC | Enabled | Generates AES-CMAC signature |
| UIDMirroring | Disabled | UID is inside encrypted PICCData, not in plaintext |

## Client Implementation Guide

### NFC scan flow

```
1. User taps phone to NFC tag
2. Platform NFC API reads NDEF record → URL string
3. Parse URL: extract e, c, mac parameters
4. Call GET /api/nfc?e={e}&c={c}&mac={mac}
5. Display returned user profile
6. Offer actions: view profile, add friend
```

The client does **not** need to:

- Handle encryption or decryption
- Store or manage SUN keys
- Parse NFC low-level protocols
- Validate MAC signatures

All crypto verification is server-side.

### URL format

The tag emits URLs in this format:

```
https://solian.app/nfc?e={BASE64_ENC}&c={COUNTER}&mac={BASE64_MAC}
```

Parameters are URL-encoded Base64 (standard Base64 with `+` → `%2B`, `/` → `%2F`, `=` → `%3D`).

### Error handling

| HTTP Status | Client behavior |
|---|---|
| 200 | Show user profile and actions |
| 404 | Show "Tag not recognized" message |
| 400 | Show "Invalid tag data" message |
| 410 | Show "This tag has been deactivated" |
| 5xx | Retry with exponential backoff |

### Platform requirements

| Platform | Requirement |
|---|---|
| Android | NFC permission, `android.nfc.action.NDEF_DISCOVERED` intent filter |
| iOS | NFC reading entitlement, `NFCNDEFReaderSession` or Core NFC |
| Flutter | `nfc_manager` or `nfc_read_ndef` package |

### Example Flutter integration

```dart
import 'package:nfc_manager/nfc_manager.dart';

Future<void> startNfcScan() async {
  final isAvailable = await NfcManager.instance.isAvailable();
  if (!isAvailable) return;

  NfcManager.instance.startSession(
    onDiscovered: (NfcTag tag) async {
      final ndef = Ndef.from(tag);
      if (ndef == null) return;

      final message = ndef.cachedMessage;
      if (message == null || message.records.isEmpty) return;

      final uri = String.fromCharCodes(message.records.first.payload);
      // uri = https://solian.app/nfc?e=...&c=...&mac=...

      final uriObj = Uri.parse(uri);
      final e = uriObj.queryParameters['e'];
      final c = uriObj.queryParameters['c'];
      final mac = uriObj.queryParameters['mac'];

      if (e == null || c == null || mac == null) return;

      final response = await dio.get('/api/nfc', queryParameters: {
        'e': e,
        'c': c,
        'mac': mac,
      });

      // Show user profile from response.data
    },
  );
}
```

## Cleanup

Soft-deleted NFC tags follow the standard Passport recycling job cleanup (7 days after soft delete).

## Future Extensions

The current design is forward-compatible with an NFC-based login flow:

```
NFC scan → identify user (current implementation)
    ↓
Passkey / biometric → authenticate user (future)
```

No schema changes would be needed — the same tag, same endpoint, with an additional authentication step on the client.

## Migration

Schema migration:

- [20260329082033_AddNfcTags.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Migrations/20260329082033_AddNfcTags.cs)

## Implementation References

- Controller: [NfcController.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Nfc/NfcController.cs)
- Service: [NfcService.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Nfc/NfcService.cs)
- Model: [SnNfcTag.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Nfc/SnNfcTag.cs)
- Database wiring: [AppDatabase.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/AppDatabase.cs)
- Service registration: [ServiceCollectionExtensions.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Startup/ServiceCollectionExtensions.cs)
