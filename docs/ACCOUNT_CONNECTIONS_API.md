# Account Connections API

Endpoint and OAuth scope for reading a user's connected platform accounts.

## Overview

Users can connect external platforms (Steam, Discord, etc.) to their account.
Third-party apps can request access to this data via the `account.connections` OAuth scope.

## OAuth Scope

| Scope | Access |
|-------|--------|
| `account.connections` | Read connected platforms and their metadata |
| `account.*` | Includes `account.connections` (wildcard) |
| `*` | Full access (superuser / internal) |

### Requesting the scope

Add `account.connections` to the `scope` parameter in the authorization request:

```
GET /api/auth/open/authorize?
    client_id={slug}&
    response_type=code&
    redirect_uri={uri}&
    scope=openid profile account.connections&
    state={state}
```

The user will see the connection-read permission on the consent screen.

## API

### List current user's connections

```
GET /api/accounts/me/connections
Authorization: Bearer {access_token}
```

**Query parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `provider` | string | No | Filter by platform name (e.g. `steam`, `discord`) |

**Response:** `200 OK`

```json
[
  {
    "id": "a1b2c3d4-...",
    "provider": "steam",
    "provided_identifier": "76561198012345678",
    "meta": {
      "avatar": "https://avatars.steamstatic.com/abc123.jpg",
      "profile_url": "https://steamcommunity.com/id/example"
    },
    "last_used_at": "2025-06-20T12:00:00Z",
    "account_id": "f47ac10b-58cc-...",
    "created_at": "2025-01-15T08:30:00Z",
    "updated_at": "2025-06-20T12:00:00Z"
  }
]
```

**Notes:**

- `access_token` and `refresh_token` are never included in the response.
- `meta` is a free-form JSON object whose keys vary by provider.
- An empty array `[]` is returned when the user has no connections (or none matching the `provider` filter).

### Error responses

| Status | Code | Description |
|--------|------|-------------|
| `401` | — | Missing or invalid token |
| `403` | — | Token lacks `account.connections` scope |

## Controller

`DysonNetwork.Passport/Account/AccountCurrentController.cs`

## Service

`DysonNetwork.Shared/Registry/RemoteAccountConnectionService.cs` — delegates to Padlock's `ListConnections` gRPC endpoint.
