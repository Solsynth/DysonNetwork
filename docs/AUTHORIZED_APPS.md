# Authorized Apps API

The Authorized Apps API lets a user inspect, extend, and revoke app access.

This is used by OIDC app login, app notifications, and app contact sharing.

Related docs:
- `docs/APP_PRODUCTS.md`
- `docs/CUSTOM_APP_NOTIFICATIONS.md`

---

## Authorization model

An authorized app record stores:
- `app_id`
- auth `type`
- granted `scopes`
- authorization timestamps

Supported types:
- `Oidc`
- `ApiKey`

Scope names should match the existing permission-node naming style, for example:
- `contacts.read`
- `notifications.send`
- `accounts.profile.board.manage`

---

## Get authorized apps

```http
GET /api/authorized-apps
Authorization: Bearer <user_token>
```

Optional query:

```http
GET /api/authorized-apps?type=Oidc
```

Response shape:

```json
[
  {
    "id": "guid",
    "app_id": "guid",
    "type": "Oidc",
    "app_slug": "shop-app",
    "app_name": "Shop App",
    "app_description": "Merchant storefront",
    "picture": null,
    "background": null,
    "scopes": ["contacts.read", "notifications.send"],
    "last_authorized_at": "2026-07-05T00:00:00Z",
    "last_used_at": "2026-07-05T01:00:00Z"
  }
]
```

---

## Grant extra scopes to an app

```http
POST /api/authorized-apps/{appId}/scopes
Authorization: Bearer <user_token>
Content-Type: application/json
```

```json
{
  "scopes": ["contacts.read"]
}
```

Behavior:
- the app must exist
- each requested scope must already be allowed by the app's OAuth config
- scopes are merged with the user's existing scopes for that app
- authorization is stored in `SnAuthorizedApp`

This is the endpoint a client should call when asking the user to let a merchant app read saved contacts.

---

## Revoke app access

```http
DELETE /api/authorized-apps/{id}?type=Oidc
Authorization: Bearer <user_token>
```

Notes:
- path id is the authorized-app record id, not the app id
- revocation soft-deletes authorization records
- active sessions / related records are also revoked through `AuthService`

---

## How other APIs use this

### Notifications

Padlock notification delivery only targets users where:
- `app_id` matches
- auth record is active
- auth type is `Oidc`
- scopes include `notifications.send`

### Contact sharing

App contact reading only works when:
- app API key is valid
- auth record is active
- scopes include `contacts.read`

Endpoint:

```http
GET /api/private/apps/{appId}/accounts/{accountId}/contacts
X-Api-Key: <custom_app_api_key>
```

### Account board access

Self board management through Passport only works when:

- auth record is active
- scopes include `accounts.profile.board.manage`

Endpoints:

```http
GET /api/accounts/me/board
PUT /api/accounts/me/board
```

---

## Notes

- Product/custom-app management routes are not authorized through this API.
- Those routes still require developer Bearer tokens.
- Authorized-app scope storage is the shared enforcement point for app-user capabilities.
