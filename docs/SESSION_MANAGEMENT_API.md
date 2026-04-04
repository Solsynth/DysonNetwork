# Session Management API Improvements

## Overview

Improvements to session management endpoints in `AccountSecurityController` for better clarity, filtering, and pagination.

## Changes

### GET /api/sessions

**Improvement**: Returns full `SnAuthSession` entity directly (removed `SessionResponse` DTO wrapper), added filtering options.

**Query Parameters**:
- `take` (int, default: 20) - Number of sessions to return
- `offset` (int, default: 0) - Number of sessions to skip
- `type` (SessionType?, optional) - Filter by session type (Login, OAuth, Oidc)
- `clientId` (Guid?, optional) - Filter by client/device ID

**Response Headers**:
- `X-Auth-Session`: Current session ID
- `X-Total`: Total count of sessions matching filters

**Before**:
```json
[
  {
    "id": "...",
    "type": 0,
    "lastGrantedAt": "...",
    "expiredAt": null,
    "ipAddress": "...",
    "userAgent": "...",
    "clientId": "...",
    "isCurrent": true
  }
]
```

**After**:
```json
[
  {
    "id": "550e8400-e29b-41d4-a716-446655440000",
    "type": 0,
    "lastGrantedAt": "2024-01-15T10:30:00Z",
    "expiredAt": null,
    "audiences": ["padlock"],
    "scopes": ["read", "write"],
    "ipAddress": "192.168.1.1",
    "userAgent": "Mozilla/5.0...",
    "location": null,
    "accountId": "...",
    "clientId": "...",
    "parentSessionId": null,
    "challengeId": null,
    "appId": null,
    "createdAt": "2024-01-15T10:30:00Z",
    "updatedAt": "2024-01-15T10:30:00Z",
    "deletedAt": null
  }
]
```

**Response Fields** (`SnAuthSession`):
- `Id`: Session identifier
- `Type`: Session type (0=Login, 1=OAuth, 2=Oidc)
- `LastGrantedAt`: Last time session was refreshed
- `ExpiredAt`: Expiration timestamp (null if active)
- `Audiences`: List of audiences
- `Scopes`: List of scopes
- `IpAddress`: Client IP address
- `UserAgent`: Client user agent string
- `Location`: Geo location (if available)
- `AccountId`: Account ID
- `ClientId`: Associated device/client ID
- `ParentSessionId`: Parent session ID (for OAuth/Oidc)
- `ChallengeId`: Challenge ID
- `AppId`: App ID (for OIDC connections)
- `CreatedAt`: Creation timestamp
- `UpdatedAt`: Last update timestamp
- `DeletedAt`: Deletion timestamp (null if active)

**Example**:
```
GET /api/sessions?take=20&offset=0&type=0
GET /api/sessions?take=10&offset=10&clientId=550e8400-e29b-41d4-a716-446655440000
```

---

### GET /api/devices

**Improvement**: Added pagination support.

**Query Parameters**:
- `take` (int, default: 20) - Number of devices to return
- `offset` (int, default: 0) - Number of devices to skip

**Response Headers**:
- `X-Auth-Session`: Current session ID
- `X-Total`: Total count of devices

**Example**:
```
GET /api/devices?take=10&offset=0
```

---

### GET /api/devices (Previously)

No filters or pagination - returned all devices and their sessions.

**After**: Supports pagination and filtering via query parameters.

---

## Bug Fixes

### Device Deletion Not Removing Device from List

**Problem**: Calling `DELETE /api/devices/{deviceId}` would expire sessions for the device but leave the device itself in the database. The device would continue appearing in `GET /api/devices` responses.

**Fix**: `AccountService.DeleteDevice` now sets `DeletedAt` on the `SnAuthClient` record in addition to expiring sessions. `GetDevices` now filters out devices where `DeletedAt != null`.

### Session Logout Not Invalidating Token

**Problem**: Calling `DELETE /api/sessions/{id}` or `DELETE /api/sessions/current` would set `ExpiredAt` in the database but not invalidate the JWT token itself. Since the shared Redis cache is checked first during authentication, the token remained valid until natural JWT expiration.

**Fix**: `AccountService.DeleteSession` now adds the session ID (JTI) to the revoked tokens list in Redis (`auth:revoked:jti:{sessionId}`) with a 30-day TTL. The token is immediately rejected on subsequent requests.

**Shared Cache Key**: The revoked JTI prefix is now centralized in `DysonNetwork.Shared.Auth.AuthCacheKeys`:
- `AuthCacheKeys.RevokedJtiPrefix` = `"auth:revoked:jti:"`
- `AuthCacheKeys.RevokedJti(jti)` = `"{RevokedJtiPrefix}{jti}"`
- `AuthCacheKeys.RevokedJtiTtlDays` = `30`

This constant is used across:
- `DysonNetwork.Shared.Auth.AuthScheme`
- `DysonNetwork.Padlock.Auth.AuthService`
- `DysonNetwork.Padlock.Auth.OidcProvider.Services.OidcProviderService`
- `DysonNetwork.Padlock.Account.AccountService`

---

## Notes

- `X-Auth-Session` header included for identifying current session
- `X-Total` header included for pagination UI
- Session type enum: `0 = Login`, `1 = OAuth`, `2 = Oidc`
