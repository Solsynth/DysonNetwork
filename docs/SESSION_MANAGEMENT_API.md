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

## Notes

- Pagination for sessions remains unchanged (`take`, `offset` query parameters)
- `X-Auth-Session` header still included for backwards compatibility
- `X-Total` header still included for pagination UI
