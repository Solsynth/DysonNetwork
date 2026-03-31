# Account Recovery Code

This document describes the account recovery code feature in Padlock.

## Overview

The recovery code is a fallback authentication factor that allows users to regain access to their account if they lose access to their primary factors (e.g., TOTP authenticator, email code, in-app code).

**Key characteristics:**
- Must be **enabled first** before any other auth factors can be added
- Trust level: `0` (lowest priority)
- Auto-generated UUID as the secret
- One-time use — after use, the code is disabled and must be re-enabled to get a new code

## Design Decisions

### Why recovery code must be enabled first

By requiring the recovery code to be enabled before other factors, users are guaranteed to always have a fallback method when they need it. This prevents situations where a user:
- Enables TOTP, loses their phone, and has no way to recover
- Sets up email code, then loses access to email

After a recovery code is used, it becomes disabled. The user must re-enable it to get a new code before adding other auth factors again.

### Security model

When a recovery code is used:
1. The recovery code factor is **disabled** (a new code must be generated)
2. All auth factors (except Password) are **disabled**
3. All existing sessions are **revoked**
4. A new session is created for the recovery device

This ensures that if an attacker obtained the recovery code, they cannot continue to use the victim's authenticated sessions, and the compromised code is no longer valid.

### Trust levels

| Factor | Trust Level |
|--------|-------------|
| RecoveryCode | 0 |
| Password | 1 |
| PinCode | 1 |
| EmailCode | 2 |
| InAppCode | 2 |
| TimedCode | 3 |

Higher trust levels contribute more steps to the challenge flow.

## Data Model

### `account_auth_factors`

| Column | Type | Description |
|--------|------|-------------|
| `id` | `uuid` | Primary key |
| `account_id` | `uuid` | Owner account ID |
| `type` | `enum` | Factor type (see `AccountAuthFactorType`) |
| `secret` | `text` | Hashed secret (plain for RecoveryCode) |
| `trustworthy` | `int` | Trust level (0-3) |
| `enabled_at` | `timestamptz` | When factor was enabled |
| `expired_at` | `timestamptz` | Expiration (if applicable) |
| `created_at` | `timestamptz` | Record creation time |

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
}
```

## Endpoints

### Create Recovery Code

Creates the recovery code factor for the authenticated user.

**Endpoint:** `POST /api/factors`

**Authentication:** Required (interactive session)

**Request:**
```json
{
  "type": "RecoveryCode",
  "secret": null
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `type` | `string` | Yes | Must be `"RecoveryCode"` |
| `secret` | `string` | No | Optional; auto-generated if omitted |

**Response (200):**
```json
{
  "id": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
  "accountId": "11111111-2222-3333-4444-555555555555",
  "type": "RecoveryCode",
  "trustworthy": 0,
  "enabledAt": "2026-03-29T08:00:00Z",
  "expiredAt": null,
  "createdResponse": {
    "recovery_code": "a1b2c3d4e5f678901234567890123456"
  }
}
```

**Important:** Store the `recovery_code` from `createdResponse` securely — it cannot be retrieved again.

**Error responses:**

| Status | Code | When |
|--------|------|------|
| 400 | `VALIDATION_ERROR` | Auth factor already exists |

### List Auth Factors

**Endpoint:** `GET /api/factors`

**Authentication:** Required (interactive session)

**Response (200):**
```json
[
  {
    "id": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
    "accountId": "11111111-2222-3333-4444-555555555555",
    "type": "RecoveryCode",
    "trustworthy": 0,
    "enabledAt": "2026-03-29T08:00:00Z",
    "expiredAt": null
  }
]
```

Note: The `secret` field is not returned in list responses for security.

### Recover Account

Recovery endpoint that validates the recovery code, disables other factors, revokes sessions, and creates a new session.

**Endpoint:** `POST /api/auth/recover`

**Authentication:** None (public endpoint)

**Request:**
```json
{
  "account": "username or email@example.com",
  "recoveryCode": "a1b2c3d4e5f678901234567890123456",
  "captchaToken": "0.xxxxxxxx...",
  "deviceId": "unique-device-identifier",
  "deviceName": "iPhone 15 Pro",
  "platform": 1
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `account` | `string` | Yes | Account name or email |
| `recoveryCode` | `string` | Yes | The recovery code secret |
| `captchaToken` | `string` | Yes | Captcha verification token |
| `deviceId` | `string` | Yes | Unique device identifier |
| `deviceName` | `string` | No | Human-readable device name |
| `platform` | `int` | No | Client platform (default: 0) |

**Response (200):**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expiresIn": 3600,
  "refreshExpiresIn": 2592000
}
```

Sets authentication cookies on the response.

**Error responses:**

| Status | Code | Message |
|--------|------|---------|
| 400 | `VALIDATION_ERROR` | Invalid captcha token |
| 400 | `NOT_FOUND` | Account not found |
| 400 | `RECOVERY_FAILED` | Invalid recovery code |
| 400 | `RECOVERY_FAILED` | Recovery code factor not found or disabled |

### Re-enable Recovery Code

After a recovery code is used, it becomes disabled. To get a new recovery code:

**Endpoint:** `POST /api/factors/{id}/enable`

**Authentication:** Required (interactive session)

**Request:** Empty body (no code required for RecoveryCode)

**Response (200):**
```json
{
  "id": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
  "accountId": "11111111-2222-3333-4444-555555555555",
  "type": "RecoveryCode",
  "trustworthy": 0,
  "enabledAt": "2026-03-30T10:00:00Z",
  "expiredAt": null,
  "createdResponse": {
    "recovery_code": "newcode123456789012345678901234567"
  }
}
```

**Important:** The new `recovery_code` in `createdResponse` must be stored securely.

### Other Factor Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/factors` | GET | List all factors |
| `/api/factors/{id}` | DELETE | Delete a factor |
| `/api/factors/{id}/enable` | POST | Enable a factor (with verification code) |
| `/api/factors/{id}/disable` | POST | Disable a factor |

## Recovery Flow

```
1. User initiates recovery (from login page or dedicated recovery flow)
2. User enters account identifier (username or email)
3. Client validates captcha
4. Client sends recovery request:
   POST /api/auth/recover
   {
     "account": "...",
     "recoveryCode": "...",
     "captchaToken": "...",
     "deviceId": "...",
     "deviceName": "...",
     "platform": 1
   }
5. Server validates:
   a. Captcha token
   b. Account exists
   c. Recovery code matches stored secret
6. On success:
   a. Disable the recovery code factor (it can no longer be used)
   b. Disable all factors except Password
   c. Revoke all existing sessions
   d. Create new session for recovery device
   e. Return tokens and set cookies
7. User is now logged in with fresh session
8. User must re-enable recovery code to get a new code, then re-configure other factors
```

## Security Considerations

### Rate Limiting

The captcha requirement helps prevent brute-force attacks on the recovery code. Additionally:
- Failed captcha attempts are rejected at the captcha provider level
- Recovery code comparison is timing-safe

### Session Revocation

All sessions are revoked on successful recovery to prevent session hijacking. This includes:
- Web sessions
- Mobile sessions
- API sessions
- OAuth sessions

### Audit Logging

Recovery attempts are logged with:
- Account ID
- Factors disabled
- Sessions revoked
- IP address and user agent
- Session ID of new session

Action log type: `accounts.recovery`

## Client Implementation Guide

### Recovery Flow

```dart
Future<void> recoverAccount({
  required String account,
  required String recoveryCode,
  required String captchaToken,
  required String deviceId,
  String? deviceName,
}) async {
  final response = await dio.post('/api/auth/recover', data: {
    'account': account,
    'recoveryCode': recoveryCode,
    'captchaToken': captchaToken,
    'deviceId': deviceId,
    'deviceName': deviceName,
    'platform': Platform.isIOS ? 2 : 1, // 0=Unidentified, 1=Android, 2=iOS
  });

  // Tokens are set in cookies by the server
  // Redirect to home or setup flow
  await _refreshTokenPair();
}
```

### Captcha Integration

The captcha token should be obtained before the recovery request:

```dart
// Using reCAPTCHA or Turnstile
final captchaToken = await CaptchaService.verify();
```

### Post-Recovery

After successful recovery:

1. **Re-enable recovery code** — Call `POST /api/factors/{id}/enable` to generate a new recovery code
2. Store the new code securely
3. Re-configure other auth factors:
   - Set up new TOTP authenticator
   - Verify email if email code was disabled
   - Re-enable any other factors they need

The recovery code must be enabled before other auth factors can be added again.

## Implementation References

- Controller: `DysonNetwork.Padlock/Auth/AuthController.cs`
- Service: `DysonNetwork.Padlock/Auth/AuthService.cs`
- Account Service: `DysonNetwork.Padlock/Account/AccountService.cs`
- Model: `DysonNetwork.Shared/Models/Account.cs`
- Action Log Types: `DysonNetwork.Shared/Models/ActionLog.cs`

## Future Extensions

### Multiple Recovery Codes

Current implementation supports one-time use with regeneration. Future versions could support:
- Multiple one-time codes in a set
- Recovery code download as backup (e.g., encrypted file)
- Manual code rotation by re-enabling

### Recovery Code Hashing

Currently, recovery codes are stored in plain text. Future versions could:
- Hash codes like passwords (BCrypt)
- Allow user to set custom codes
- Implement code rotation

### Gradual Factor Disabling

Instead of immediately disabling all factors, future versions could:
- Keep certain factors based on trust level
- Offer selective re-enrollment
- Support recovery without full factor reset
