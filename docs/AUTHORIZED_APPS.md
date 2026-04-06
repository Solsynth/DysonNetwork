# Authorized Apps API

## Overview

The Authorized Apps API allows users to view and manage third-party applications that have been granted access to their account. This is part of the account security features in the Padlock service.

## API Endpoints

### Get Authorized Apps

**GET** `/api/authorized-apps`

Returns a list of applications authorized by the current user.

**Query Parameters:**

| Parameter | Type | Description |
|------------|------|-------------|
| `type` | `AuthorizedAppType?` | Filter by app type |

**Response:** `200 OK`

```json
[
  {
    "id": "guid",
    "appId": "guid",
    "type": "Oidc" | "AppConnect",
    "appSlug": "string?",
    "appName": "string?",
    "lastAuthorizedAt": "2024-01-01T00:00:00Z",
    "lastUsedAt": "2024-01-02T00:00:00Z"
  }
]
```

**Authorization:** Requires authenticated user via `[Authorize]` and `[RequireInteractiveSession]` attributes.

---

### Deauthorize App

**DELETE** `/api/authorized-apps/{appId:guid}`

Revokes access for a specific authorized application.

**Path Parameters:**

| Parameter | Type | Description |
|------------|------|-------------|
| `appId` | `guid` | The app ID to revoke |

**Query Parameters:**

| Parameter | Type | Description |
|------------|------|-------------|
| `type` | `AuthorizedAppType?` | App type filter (optional) |

**Response:** `204 No Content` on success, `404 Not Found` if app not found.

---

## Data Models

### AuthorizedAppType

| Value | Description |
|-------|-------------|
| `Oidc` | OAuth 2.0 / OIDC based authorization |
| `AppConnect` | App Connect protocol authorization |

### SnAuthorizedApp

| Field | Type | Description |
|-------|------|-------------|
| `Id` | `guid` | Unique identifier |
| `AccountId` | `guid` | Owner's account ID |
| `AppId` | `guid` | Reference to custom app in Develop service |
| `AppSlug` | `string?` | App URL slug |
| `AppName` | `string?` | Display name |
| `LastAuthorizedAt` | `Instant` | When authorization was granted |
| `LastUsedAt` | `Instant?` | Last time the app accessed the account |

## Implementation Details

The controller (`AccountSecurityController`) enforces:
- User must be authenticated (`CurrentUser` in HttpContext)
- Only returns apps where `DeletedAt == null` (soft delete)
- Results ordered by most recently used first (`LastUsedAt ?? LastAuthorizedAt`)
- Revocation uses `AuthService.RevokeAuthorizedAppAccessAsync()` which handles type filtering

## Related Components

- **AccountService**: Business logic for account management
- **AuthService**: Handles OAuth/OIDC authorization flows
- **AppDatabase**: EF Core database context
