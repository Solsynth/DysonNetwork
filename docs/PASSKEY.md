# Passkey Authentication Factor

This document describes the passkey authentication factor implementation in Padlock.

## Overview

Passkeys provide a secure, passwordless authentication method using WebAuthn (FIDO2). Users can register their device's platform authenticator (Touch ID, Face ID, Windows Hello, etc.) and use it to authenticate without needing codes or passwords.

**Key characteristics:**
- Trust level: `4` (highest)
- Server-side ECDSA P-256 signature verification
- Full credential data stored in Padlock
- Supports cross-device passkeys via hybrid authentication

## Data Model

### `account_auth_factors`

| Column | Type | Description |
|--------|------|-------------|
| `id` | `uuid` | Primary key |
| `account_id` | `uuid` | Owner account ID |
| `type` | `enum` | Factor type (`Passkey`) |
| `secret` | `jsonb` | Full passkey credential (credentialId, publicKeyX, publicKeyY) |
| `config` | `jsonb` | Additional config (`verified` flag, authenticator info) |
| `trustworthy` | `int` | Trust level (4) |
| `enabled_at` | `timestamptz` | When factor was enabled |
| `expired_at` | `timestamptz` | Not used for passkeys |
| `created_at` | `timestamptz` | Record creation time |

### Secret Format (stored as JSON)

```json
{
  "credentialId": "base64-encoded-credential-id",
  "publicKeyX": "base64-encoded-x-coordinate",
  "publicKeyY": "base64-encoded-y-coordinate",
  "counter": 12345
}
```

### Config Format

```json
{
  "verified": true,
  "deviceName": "MacBook Pro",
  "platform": "macOS"
}
```

## Auth Factor Types

Defined in `DysonNetwork.Shared/Models/Account.cs`:

```csharp
public enum AccountAuthFactorType
{
    Password,
    EmailCode,
    InAppCode,
    TimedCode,
    PinCode,
    RecoveryCode,
    NfcToken,
    Passkey,
}
```

## Trust Levels

| Factor | Trust Level |
|--------|-------------|
| RecoveryCode | 0 |
| Password | 1 |
| PinCode | 1 |
| NfcToken | 1 |
| EmailCode | 2 |
| InAppCode | 2 |
| TimedCode | 3 |
| **Passkey** | **4** |

Higher trust levels contribute more steps to the challenge flow. Passkey has the highest trust level, making it suitable for confirming dangerous operations.

## Registration Flow

The passkey registration uses a two-step ceremony following the WebAuthn specification:

### Step 1: Start Registration

Generate a registration challenge and options.

**Endpoint:** `POST /api/factors/passkey/start`

**Authentication:** Required (interactive session)

**Request:**
```json
{
  "deviceId": "device-identifier",
  "deviceName": "MacBook Pro",
  "rpId": "example.com",
  "rpName": "DysonNetwork"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `deviceId` | `string` | Yes | Unique device identifier |
| `deviceName` | `string` | No | Human-readable device name |
| `rpId` | `string` | Yes | Relying Party ID (domain) |
| `rpName` | `string` | Yes | Relying Party name |

**Response (200):**
```json
{
  "challenge": "dG9rZW4gdGhhdCBpcyBhdCBsZWFzdDI1NiBiaXRz",
  "rpId": "example.com",
  "rpName": "DysonNetwork",
  "userId": "11111111-2222-3333-4444-555555555555",
  "userName": "johndoe",
  "displayName": "John Doe",
  "pubKeyCredParams": [{"type": "public-key", "alg": -7}],
  "timeout": 60000,
  "authenticatorSelection": {
    "authenticatorAttachment": "platform",
    "residentKey": "preferred",
    "userVerification": "preferred"
  }
}
```

### Step 2: Complete Registration

After the client creates the credential using the WebAuthn API, send the attestation to complete registration.

**Endpoint:** `POST /api/factors/passkey/complete`

**Authentication:** Required (interactive session)

**Request:**
```json
{
  "deviceId": "device-identifier",
  "clientDataJson": "{\"type\":\"webauthn.create\",\"challenge\":\"dG9rZW4...\",\"origin\":\"https://example.com\"}",
  "attestationObject": "base64-encoded-attestation-object"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `deviceId` | `string` | Yes | Must match the deviceId from step 1 |
| `clientDataJson` | `string` | Yes | JSON string of client data |
| `attestationObject` | `string` | Yes | Base64-encoded attestation object |

**Response (200):**
```json
{
  "id": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
  "accountId": "11111111-2222-3333-4444-555555555555",
  "type": "Passkey",
  "trustworthy": 4,
  "enabledAt": "2026-04-05T10:00:00Z",
  "expiredAt": null
}
```

### Client-Side Registration Example

```javascript
const credential = await navigator.credentials.create({
  publicKey: {
    rp: {
      id: response.rpId,
      name: response.rpName
    },
    user: {
      id: Uint8Array.from(atob(response.userId), c => c.charCodeAt(0)),
      name: response.userName,
      displayName: response.displayName
    },
    challenge: Uint8Array.from(atob(response.challenge), c => c.charCodeAt(0)),
    pubKeyCredParams: [{ type: "public-key", alg: -7 }],
    timeout: 60000,
    authenticatorSelection: {
      authenticatorAttachment: "platform",
      residentKey: "preferred",
      userVerification: "preferred"
    }
  }
});

// Send to /factors/passkey/complete:
// - attestationObject: btoa(String.fromCharCode(...new Uint8Array(credential.response.attestationObject)))
// - clientDataJson: new TextDecoder().decode(credential.response.clientDataJSON)
```

## Verification Flow

### Client-Side Assertion Generation

When authenticating, the client uses the WebAuthn API to generate an assertion:

```javascript
const credential = await navigator.credentials.get({
  publicKey: {
    challenge: serverChallenge,
    rpId: "example.com",
    userVerification: "preferred"
  }
});

// Assertion contains:
// - credential.rawId (the credential ID)
// - response.clientDataJSON (JSON string of client data)
// - response.authenticatorData (bytes)
// - response.signature (bytes)
```

### Verification Request

**Endpoint:** Challenge verification during login flow

The assertion data is passed as JSON to the verification endpoint:

```json
{
  "credentialId": "ZLhsEWvakECWPNZBNkOtHGVEEBkAAAAA",
  "clientDataJson": "{\"type\":\"webauthn.get\",\"challenge\":\"dG9rZW4...\",\"origin\":\"https://example.com\"}",
  "authenticatorData": "base64-encoded-authenticator-data",
  "signature": "base64-encoded-ecdsa-signature"
}
```

### Server-Side Verification

The `VerifyPasskey` method in `AccountService.cs` performs:

1. **Credential ID matching** - Ensures the presented credential matches stored public key
2. **User presence check** - Verifies `UP` flag is set in authenticator data
3. **Signature verification** - ECDSA P-256 verification using stored public key and constructed signed data

**Signed data construction:**
```
32 bytes | RpIdHash
1 byte  | Flags (user present, etc.)
4 bytes | Counter (big-endian)
32 bytes | SHA256(clientDataJson)
```

### AuthenticatorData Structure

| Offset | Length | Field |
|--------|--------|-------|
| 0 | 32 | RP ID Hash (SHA-256) |
| 32 | 1 | Flags byte |
| 33 | 4 | Sign Counter (big-endian uint32) |
| 37+ | Variable | Attested Credential Data (optional) |

### Flags Byte

| Bit | Name | Description |
|-----|------|-------------|
| 0 | UP | User Present |
| 1 | UV | User Verified |
| 2 | BE | Backup Eligibility |
| 3 | BS | Backup State |
| 4 | AT | Attested Credential Data Present |
| 5 | ED | Extension Data Present |
| 6 | N/A | Reserved |
| 7 | N/A | Reserved |

## Endpoints

### Start Passkey Registration

**Endpoint:** `POST /api/factors/passkey/start`

### Complete Passkey Registration

**Endpoint:** `POST /api/factors/passkey/complete`

### List Auth Factors

**Endpoint:** `GET /api/factors`

**Authentication:** Required (interactive session)

**Response (200):**
```json
[
  {
    "id": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
    "accountId": "11111111-2222-3333-4444-555555555555",
    "type": "Passkey",
    "trustworthy": 4,
    "enabledAt": "2026-04-05T10:00:00Z",
    "expiredAt": null,
    "config": {
      "verified": false
    }
  }
]
```

Note: The `secret` field is not returned in list responses for security.

### Other Factor Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/factors` | GET | List all factors |
| `/api/factors/{id}` | DELETE | Delete a factor |
| `/api/factors/{id}/enable` | POST | Enable a factor |
| `/api/factors/{id}/disable` | POST | Disable a factor |

## Implementation References

- Model: `DysonNetwork.Shared/Models/Account.cs`
- Service: `DysonNetwork.Padlock/Account/AccountService.cs`
- Controller: `DysonNetwork.Padlock/Account/AccountSecurityController.cs`
- gRPC Mapping: `DysonNetwork.Padlock/Account/AccountServiceGrpc.cs`
- Proto: `DysonNetwork.Shared/Proto/Account.cs`

## Security Considerations

### Signature Verification

Passkey verification uses ECDSA with P-256 curve (secp256r1). The signature is verified against:
- The stored public key (X and Y coordinates)
- The authenticator data
- The client data JSON

### User Presence

The verification requires the `UserPresent` flag to be set, ensuring the user physically interacted with the authenticator.

### Credential ID Matching

Before signature verification, the presented credential ID is matched against the stored credential ID to prevent using a different credential with the same public key.

### Replay Protection

The authenticator counter is stored and can be used for replay detection. If a counter value lower than the stored value is presented, the verification should fail.

## Future Extensions

### Counter Rollback Detection

Currently, counter values are stored but not enforced. Future versions could reject assertions with counters lower than stored values.

### Credential ID Rotation

Support for credential ID updates when users re-register the same authenticator.

### Hybrid Transport Support

For cross-device passkeys (e.g., phone as authenticator for laptop sign-in), implement hybrid authentication with:
- `PRF` extension for key derivation
- Large blob storage for credential state

### Authenticator Metadata

Store and verify authenticator metadata:
- AAGUID to identify authenticator type
- Device name/platform
- Backup eligibility status
