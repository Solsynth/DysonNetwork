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
| `uid` | `text` | Chip UID (hex string). Unique index. For encrypted tags, this is the UID from decrypted PICCData; for plain tags, it's the entry UUID. |
| `user_id` | `uuid` | Owner account ID. Indexed. |
| `is_encrypted` | `bool` | Whether this is an NTAG424 SUN encrypted tag. |
| `sun_key` | `bytea` | Per-tag SDMFileReadKey (16 bytes, AES-128). Only set for encrypted tags; null for plain tags. |
| `counter` | `int` | Last seen read counter for replay protection. Only set for encrypted tags; null for plain tags. |
| `label` | `text` | Optional user label (e.g., "Work Card") |
| `is_active` | `bool` | Soft disable flag |
| `locked_at` | `timestamptz` | When the tag was locked against reprogramming |
| `last_seen_at` | `timestamptz` | Last successful scan timestamp |
| `created_at` | `timestamptz` | Record creation time |
| `updated_at` | `timestamptz` | Last update time |
| `deleted_at` | `timestamptz` | Soft delete timestamp |

## SUN Verification Flow

When an encrypted NTAG424 tag is scanned, it generates a URL like:

```
https://solian.app/nfc?e=BASE64_ENC&c=COUNTER&mac=BASE64_MAC
```

The server performs these steps:

```
1. Parse parameters
   e   → encrypted PICCData bytes (16 bytes, AES-CBC encrypted)
   c   → read counter (int)
   mac → AES-CMAC signature (16 bytes)

2. Load all active encrypted tags from database (is_encrypted = true)

3. For each candidate tag:
   a. Decrypt e using the tag's SunKey:
      - Derive SUN_ENC_KEY via KDF(SunKey, 0xC7 || UID_placeholder || readCtr || 0x80)
      - AES-CBC decrypt e with SUN_ENC_KEY (zero IV)
      - Extract UID (bytes 1-7) and SDMReadCtr (bytes 8-10) from decrypted data
      - Verify SDMReadCtr == c

   b. Verify CMAC using the extracted UID:
      - Derive SUN_MAC_KEY via KDF(SunKey, 0x01 || UID || readCtr || 0x80)
      - Compute CMAC(SUN_MAC_KEY, e || readCtr_3bytes_LE)
      - Compare with received mac (constant-time comparison)
      - Verify the extracted UID matches tag.uid

   c. If both pass → found the matching tag

4. Counter check (replay protection):
   counter must be > tag.counter (strict: no replay allowed)

5. On success:
   - Update tag.counter and tag.last_seen_at
   - Fetch user profile via AccountService
   - Check relationship status if observer is authenticated
   - Return user info + available actions
```

### Key hierarchy

```
SDMFileReadKey (16 bytes, stored as sun_key in DB)
    │
    ├─→ KDF(0xC7 || UID_placeholder || ReadCtr || 0x80) → SUN_ENC_KEY
    │   (UID_placeholder is all zeros since UID is unknown before decryption)
    │
    └─→ KDF(0x01 || UID || ReadCtr || 0x80) → SUN_MAC_KEY
        (UID is extracted from decrypted PICCData)
```

### Crypto implementation

| Operation | Algorithm | Notes |
|---|---|---|
| Session key derivation | AES-CMAC based KDF (NXP NTAG424 spec) | Derives ENC_KEY and MAC_KEY from SDMFileReadKey |
| MAC verification | AES-128-CMAC (NIST SP 800-38B / RFC 4493) | Pure .NET implementation, constant-time comparison |
| PICCData decryption | AES-CBC with zero IV | 16-byte block, parsed for UID and counter |
| Replay detection | Counter comparison | Strict: counter must be strictly greater than last seen |

### PICCData format

After decryption, the PICCData layout is:

```
Byte 0:     PICCDataTag (0xC7 for AES-128, 0x08 for 3DES)
Bytes 1-7:  UID (7 bytes, ISO 14443-3A)
Bytes 8-10: SDMReadCtr (3 bytes, little-endian)
Bytes 11-15: Reserved/zeros
```

## NFC Login Flow

Encrypted NFC tags can be used as an authentication factor in the Padlock challenge flow.

### Flow

```
1. User creates auth challenge: POST /api/auth/challenge
2. Client scans NFC tag → extracts e, c, mac from SUN URL
3. Client submits NFC factor: PATCH /api/auth/challenge/{id}
   Body: { "factor_id": "...", "password": "e=...&c=...&mac=..." }
4. Padlock calls Passport via gRPC to validate the SUN token
5. If valid, the NfcToken factor is completed (Trustworthy: 1)
6. When all required steps are done, client exchanges for tokens: POST /api/auth/token
```

### How Padlock validates NFC tokens

Padlock's `AccountService.VerifyFactorCode` handles the `NfcToken` factor type:
1. Parses `e`, `c`, `mac` from the URL-encoded password string
2. Calls Passport's `DyNfcService.ValidateNfcToken` via gRPC
3. Passport performs full SUN verification (CMAC, decryption, counter check)
4. Returns `{ is_valid, account_id, tag_id, error_code }`

### Setting up NfcToken as an auth factor

Users can enable NFC as an auth factor after registering an encrypted NFC tag:
```
POST /api/auth/factors { "type": "NfcToken", "secret": "<tag_id>" }
```

The factor's `Config` dictionary stores the associated tag ID. Trustworthy level: 1 (single-step factor).

## Authentication

| Endpoint | Auth | Description |
|---|---|---|
| `GET /api/nfc?uid=` | Optional | Public scan resolution by UID (unencrypted tags). If JWT present, includes relationship info. |
| `GET /api/nfc?e=&c=&mac=` | Optional | Public scan resolution via SUN URL (encrypted tags). Includes claim logic. |
| `GET /api/nfc/lookup?uid=` | Optional | Look up tag by UID (admin/debug only, no MAC verification) |
| `GET /api/nfc/tags/{id}` | Optional | Look up tag by entry ID (for unencrypted/plain tags) |
| `GET /api/nfc/tags` | Required | List user's tags |
| `POST /api/nfc/tags` | Required | Register an unencrypted tag |
| `PATCH /api/nfc/tags/{id}` | Required | Update tag metadata |
| `POST /api/nfc/tags/{id}/lock` | Required | Lock a tag |
| `DELETE /api/nfc/tags/{id}` | Required | Unregister a tag |
| `POST /api/admin/nfc/tags` | `nfc.admin` | Create encrypted tag with SUN key (factory) |
| `GET /api/admin/nfc/tags` | `nfc.admin` | List all encrypted tags |

### gRPC endpoints (internal, Padlock ↔ Passport)

| gRPC Method | Direction | Description |
|---|---|---|
| `DyNfcService.ValidateNfcToken` | Padlock → Passport | Validate SUN token for login. Returns `{ is_valid, account_id, tag_id, error_code }` |
| `DyNfcService.ResolveNfcTag` | Any → Passport | Resolve SUN token to full user profile. Returns `{ is_valid, account, profile, is_friend, actions }` |

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

### Lookup tag by entry ID

`GET /api/nfc/tags/{id}`

No authentication required. If JWT present, relationship info is included.

For unencrypted/plain NFC tags — the tag entry ID is stored on the physical tag and read directly without SUN verification.

Parameters:

| Param | Type | Required | Description |
|---|---|---|---|
| `id` | UUID | Yes | Database entry ID of the tag |

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

### Register tag (unencrypted only)

`POST /api/nfc/tags`

Requires JWT. Only for unencrypted/plain tags. Encrypted tags are registered via the admin API.

Request body:

```json
{
  "uid": "04A1B2C3D4E5F6",
  "label": "Work Card"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `uid` | string | Yes | Chip UID (hex, e.g., "04A1B2C3D4E5F6") |
| `label` | string | No | User-facing label (max 64 chars) |

Response (200):

```json
{
  "id": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
  "uid": "04A1B2C3D4E5F6",
  "label": "Work Card",
  "is_active": true,
  "is_locked": false,
  "is_encrypted": false,
  "last_seen_at": null,
  "created_at": "2026-03-30T12:00:00Z"
}
```

For encrypted tags, use the admin endpoint: `POST /api/admin/nfc/tags`.

Error responses:

| Status | Code | When |
|---|---|---|
| 400 | `VALIDATION_ERROR` | Invalid UID format |
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

### Factory flow (encrypted tags)

Encrypted NTAG424 SUN tags are produced by a factory. End users cannot write to NTAG424 authentication slots, so the factory handles key generation and tag programming.

#### Step 1: Factory prepares the tag

1. Factory generates a unique 16-byte AES-128 SUN key
2. Factory configures NTAG424 SDM settings on the tag:
   - Enable SUN (Secure Unique NFC)
   - Set SDM MAC input: PICCData + ReadCtr
   - Set URL template: `https://solian.app/nfc?e={ENC}&c={CNT}&mac={MAC}`
   - Set SUN MAC offset: after encrypted data
   - Write the SUN key to the tag's SDM keys (FileRead key)
3. Factory optionally locks the tag

#### Step 2: Admin registers the tag on the server

Factory (or admin) calls the admin API to register the tag:

```
POST /api/admin/nfc/tags
{
  "uid": "04A1B2C3D4E5F6",
  "sun_key": "base64-encoded-16-byte-key==",
  "assigned_user_id": null  // or a specific user ID for pre-assigned tags
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `uid` | string | Yes | Physical chip UID |
| `sun_key` | string | Yes | Base64-encoded 16-byte SUN key |
| `assigned_user_id` | string? | No | Pre-assign to a specific user (null = unassigned) |

Requires `nfc.admin` permission.

#### Step 3: User receives and scans the tag

1. User receives the pre-programmed tag
2. User signs in to the app
3. User scans the tag → `GET /api/nfc?e=...&c=...&mac=...`
4. Server validates the SUN, finds the unclaimed tag
5. Server automatically claims the tag for the authenticated user
6. Response includes `is_claimed: true`

#### Pre-assigned vs unassigned tags

| Mode | `assigned_user_id` | Behavior on scan |
|---|---|---|
| Unassigned | `null` | First authenticated scanner claims the tag |
| Pre-assigned | User ID | Only the assigned user can claim the tag; others get `TAG_PRE_ASSIGNED` error |

### User flow (unencrypted tags)

Unencrypted tags are self-registered by users:

1. User calls `POST /api/nfc/tags` with `{ "uid": "..." }`
2. User writes the tag entry ID (the UUID returned by the server) to the physical tag
3. No key configuration needed

### Recommended NTAG424 SDM configuration (for factory)

| Setting | Value | Notes |
|---|---|---|
| SDMReadCtr | Enabled | Counter increments on each read |
| SDMENCFileData | Enabled | Encrypts PICCData |
| SDMMACInput | PICCData + ReadCtr | MAC covers both encrypted data and counter |
| SDMMAC | Enabled | Generates AES-CMAC signature |
| UIDMirroring | Disabled | UID is inside encrypted PICCData, not in plaintext |
| SDMFileReadKey | Factory-generated | 16-byte AES-128 key, also stored on server via admin API |

## Client Implementation Guide

### NFC scan flow

**For encrypted tags (SUN):**

```
1. User taps phone to NFC tag
2. Platform NFC API reads NDEF record → URL string
3. Parse URL: extract e, c, mac parameters
4. Call GET /api/nfc?e={e}&c={c}&mac={mac}
5. Display returned user profile
6. Offer actions: view profile, add friend
```

**For unencrypted tags:**

```
1. User taps phone to NFC tag
2. Platform NFC API reads NDEF record → URL string
3. Parse URL: extract uid parameter (or tag entry ID)
4. Call GET /api/nfc?uid={uid} or GET /api/nfc/tags/{id}
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

Encrypted tags emit URLs in this format:

```
https://solian.app/nfc?e={BASE64_ENC}&c={COUNTER}&mac={BASE64_MAC}
```

Unencrypted tags emit a simple URL with the UID or entry ID.

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

The NFC feature supports:

- **Social identification** (scan-only): Both encrypted (SUN) and unencrypted tags can resolve to user profiles.
- **Login via NFC**: Encrypted SUN tags can be used as an `NfcToken` auth factor in the Padlock challenge flow. Padlock calls Passport via gRPC to validate SUN tokens.

Possible future enhancements:

- **Multi-factor NFC**: Require NFC + biometric/passkey for high-security scenarios
- **NFC-based device binding**: Tie sessions to specific NFC tags for continuous authentication
- **SUN key rotation**: Allow users to regenerate SUN keys for existing tags

## Migration

Schema migrations:

- [20260330171220_AddNfcTags.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Migrations/20260330171220_AddNfcTags.cs) - Initial nfc_tags table
- [20260331000000_AddNfcTagsEncrypted.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Migrations/20260331000000_AddNfcTagsEncrypted.cs) - Add is_encrypted column

## Implementation References

- Controller: [NfcController.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Nfc/NfcController.cs)
- Service: [NfcService.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Nfc/NfcService.cs)
- gRPC Server: [NfcServiceGrpc.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Nfc/NfcServiceGrpc.cs)
- Crypto: [NfcCrypto.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Nfc/NfcCrypto.cs)
- Model: [SnNfcTag.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Nfc/SnNfcTag.cs)
- Database wiring: [AppDatabase.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/AppDatabase.cs)
- Service registration: [ServiceCollectionExtensions.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Startup/ServiceCollectionExtensions.cs)
- Proto: nfc.proto (external proto repo)
