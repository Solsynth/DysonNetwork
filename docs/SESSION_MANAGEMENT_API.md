# Session Management API Improvements

## Overview

Improvements to session management endpoints in `AccountSecurityController` for better clarity and client-side session identification.

## Changes

### GET /api/sessions

**Improvement**: Added `IsCurrent` flag to session responses.

Previously, clients had to rely on the `X-Auth-Session` response header to identify the current session. Now, each session object in the list explicitly indicates whether it represents the active session.

**Before**:
```json
{
  "id": "...",
  "type": 0,
  "lastGrantedAt": "...",
  "expiredAt": null,
  "ipAddress": "...",
  "userAgent": "...",
  "clientId": "..."
}
```

**After**:
```json
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
```

**Response Record** (`SessionResponse`):
- `Id`: Session identifier
- `Type`: Session type (Login, OAuth, Oidc)
- `LastGrantedAt`: Last time session was refreshed
- `ExpiredAt`: Expiration timestamp (null if active)
- `IpAddress`: Client IP address
- `UserAgent`: Client user agent string
- `ClientId`: Associated device/client ID
- `IsCurrent`: True if this session matches the current request's session

### GET /api/devices

**Improvement**: Renamed internal variable from `challenge` to `sessionsByClientId` for clarity.

The variable name now accurately reflects its purpose - a dictionary mapping client IDs to their associated sessions.

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

## Notes

- Pagination for sessions remains unchanged (`take`, `offset` query parameters)
- `X-Auth-Session` header still included for backwards compatibility
- `X-Total` header still included for pagination UI
