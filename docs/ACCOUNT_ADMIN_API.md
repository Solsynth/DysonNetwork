# Account Admin API

This document describes the account admin endpoints split across:

- `DysonNetwork.Padlock` for auth, contacts, punishments, and session control
- `DysonNetwork.Passport` for profile, status, badges, and presence activity

All routes below are local development routes. In production, the gateway rewrites them to:

- `/padlock/admin/accounts/...`
- `/passport/admin/accounts/...`

All JSON fields are serialized as `snake_case`.

## Overview

There is no single monolithic admin controller for the whole account domain.

- Use Padlock admin routes when the action affects account access or security state.
- Use Passport admin routes when the action affects hydrated profile-facing account data.

## Permissions

| Permission | Purpose |
| --- | --- |
| `accounts.view` | View admin account lists and details |
| `accounts.manage` | Perform account management actions such as revoking sessions |
| `accounts.delete` | Delete an account |
| `punishments.view` | View created punishments |
| `punishments.create` | Create punishments and suspensions |
| `punishments.update` | Update punishments |
| `punishments.delete` | Remove punishments |
| `accounts.statuses.update` | Run admin presence scan operations |
| `credits.validate.perform` | Invalidate social credit cache |
| `notifications.send` | Send admin push notifications |
| `emails.send` | Send admin HTML emails |

## Padlock Admin Routes

Base route:

```text
/api/admin/accounts
```

These endpoints live in `DysonNetwork.Padlock/Account/AccountAdminController.cs`.

### GET /api/admin/accounts

Lists accounts for admin tooling with auth-side metadata.

Query parameters:

- `query` optional account name or nick filter
- `take` default `50`, max `200`
- `offset` default `0`
- `order_by` one of `name`, `name_desc`, `created_at_desc`

Required permission:

- `accounts.view`

Response headers:

- `X-Total` total matching account count

Response shape:

```json
[
  {
    "account": {
      "id": "550e8400-e29b-41d4-a716-446655440000",
      "name": "alice",
      "nick": "Alice"
    },
    "primary_email": "alice@example.com",
    "contact_count": 2,
    "auth_factor_count": 3,
    "has_password": true,
    "active_session_count": 4,
    "active_device_count": 2,
    "active_punishment": null
  }
]
```

Notes:

- `account` is hydrated through Profile gRPC, so it includes the shared account shape.
- `active_punishment` is the most severe active punishment, if any.

### GET /api/admin/accounts/{name}

Returns auth-side detail for a single account.

`{name}` accepts either:

- account name
- account id as a GUID

Required permission:

- `accounts.view`

Response fields:

- `account`
- `contacts`
- `auth_factors`
- `active_session_count`
- `active_device_count`
- `active_punishment`
- `active_punishments`

The `auth_factors` list is intentionally summarized and does not expose raw secrets.

Example response:

```json
{
  "account": {
    "id": "550e8400-e29b-41d4-a716-446655440000",
    "name": "alice",
    "nick": "Alice"
  },
  "contacts": [
    {
      "type": 0,
      "content": "alice@example.com",
      "is_primary": true
    }
  ],
  "auth_factors": [
    {
      "id": "0b6d9f6b-1ca1-4e11-a4cb-7f23d52784d4",
      "type": 0,
      "trustworthy": 1,
      "has_secret": true,
      "config": null,
      "enabled_at": "2026-07-06T10:00:00Z",
      "expired_at": null
    }
  ],
  "active_session_count": 4,
  "active_device_count": 2,
  "active_punishment": null,
  "active_punishments": []
}
```

### POST /api/admin/accounts/{name}/sessions/revoke

Revokes all active sessions for the target account.

Required permission:

- `accounts.manage`

Behavior:

- Expires all active sessions for the account
- Intended for immediate admin lockout or incident response

Response:

- `200 OK`

### POST /api/admin/accounts/notifications

Sends push notifications to specific accounts or all accounts.

Required permission:

- `notifications.send`

Targeting modes:

- `account_id`
- `account_ids`
- `broadcast_to_all`

Request body:

```json
{
  "account_ids": [
    "550e8400-e29b-41d4-a716-446655440000",
    "7a1bd1c9-9d7d-4e77-b25c-c48a7c6b8956"
  ],
  "broadcast_to_all": false,
  "topic": "admin.notice",
  "title": "Maintenance notice",
  "subtitle": "DysonNetwork",
  "body": "We will perform maintenance at 02:00 UTC.",
  "action_uri": "solian://announcements/maintenance",
  "push_type": "alert",
  "is_silent": false,
  "is_savable": true,
  "meta": {
    "category": "maintenance"
  }
}
```

Response shape:

```json
{
  "requested": 2,
  "resolved": 2,
  "sent": 2,
  "skipped": 0,
  "broadcast_to_all": false
}
```

Notes:

- For broadcast mode, all non-deleted accounts are targeted.
- The endpoint reuses the Ring push delivery path.

### POST /api/admin/accounts/emails

Sends raw HTML emails to specific accounts or all accounts.

Required permission:

- `emails.send`

Targeting modes:

- `account_id`
- `account_ids`
- `broadcast_to_all`

Request body:

```json
{
  "account_ids": [
    "550e8400-e29b-41d4-a716-446655440000"
  ],
  "broadcast_to_all": false,
  "subject": "Important account notice",
  "html_body": "<html><body><h1>Hello</h1><p>This is a custom admin email.</p></body></html>"
}
```

Response shape:

```json
{
  "requested": 1,
  "resolved": 1,
  "sent": 1,
  "skipped": 0,
  "broadcast_to_all": false
}
```

Notes:

- The caller is responsible for providing the full HTML body.
- Delivery uses the account's verified email contacts.
- If an account has no verified email contact, it is counted in `skipped`.

### POST /api/admin/accounts/{name}/suspend

Creates a suspension-oriented punishment directly from the admin account surface.

Required permission:

- `punishments.create`

Supported punishment types:

- `block_login`
- `disable_account`

Request body:

```json
{
  "reason": "Chargeback abuse",
  "expired_at": "2026-08-01T00:00:00Z",
  "type": "disable_account",
  "revoke_sessions": true,
  "social_credit_reduction": 15,
  "publisher_rating_reduction": 5,
  "publisher_names": ["solsynth-blog"]
}
```

Notes:

- If `revoke_sessions` is `true`, the account is logged out immediately.
- This endpoint reuses the same punishment creation flow as the regular punishment route.

Response:

- `SnAccountPunishment`

### Existing punishment routes under the same controller

These still exist and are useful for full moderation tooling:

- `GET /api/admin/accounts/punishments/created`
- `POST /api/admin/accounts/{name}/punishments`
- `PATCH /api/admin/accounts/{name}/punishments/{punishmentId}`
- `DELETE /api/admin/accounts/{name}/punishments/{punishmentId}`
- `DELETE /api/admin/accounts/{name}`

See also:

- `docs/PUNISHMENT_API.md`

## Passport Admin Routes

Base route:

```text
/api/admin/accounts
```

These endpoints live in `DysonNetwork.Passport/Account/AccountAdminController.cs`.

### GET /api/admin/accounts

Lists hydrated accounts for admin tooling with Passport-side metadata.

Query parameters:

- `query` optional account filter
- `take` default `50`, max `200`
- `offset` default `0`
- `order_by` forwarded to the shared account listing, typically `created_at_desc`

Required permission:

- `accounts.view`

Response headers:

- `X-Total` total matching account count

Response shape:

```json
[
  {
    "account": {
      "id": "550e8400-e29b-41d4-a716-446655440000",
      "name": "alice",
      "nick": "Alice"
    },
    "status": {
      "account_id": "550e8400-e29b-41d4-a716-446655440000",
      "label": "Online",
      "is_online": true
    },
    "badge_count": 3,
    "active_activity_count": 1
  }
]
```

Notes:

- `status` comes from `AccountEventService`.
- `active_activity_count` counts live presence activities such as rich presence providers.

### GET /api/admin/accounts/{identifier}

Returns Passport-side detail for a single account.

`{identifier}` accepts either:

- account name
- account id as a GUID

Required permission:

- `accounts.view`

Response fields:

- `account`
- `status`
- `activities`
- `badge_count`

Example response:

```json
{
  "account": {
    "id": "550e8400-e29b-41d4-a716-446655440000",
    "name": "alice",
    "nick": "Alice"
  },
  "status": {
    "account_id": "550e8400-e29b-41d4-a716-446655440000",
    "label": "Playing Game",
    "is_online": true,
    "type": 0
  },
  "activities": [
    {
      "provider": "steam",
      "title": "Counter-Strike 2"
    }
  ],
  "badge_count": 3
}
```

## Presence Scan Admin Routes

These Passport admin routes were already present and remain part of the same controller:

- `POST /api/admin/accounts/{name}/credits`
- `POST /api/admin/accounts/presences/steam/scan`
- `POST /api/admin/accounts/presences/steam/scan/{identifier}`
- `POST /api/admin/accounts/presences/steam/scan-by-steam-id/{steamId}`
- `POST /api/admin/accounts/presences/steam/scan/stages/{stage}`

They are useful for support tooling, but they are not part of the core account identity/punishment flow.

## Account Identifier Rules

Admin detail routes accept either:

- account slug-like name such as `alice`
- account GUID such as `550e8400-e29b-41d4-a716-446655440000`

The controller resolves GUIDs directly first, then falls back to name lookup.

## Service Boundary Guidance

Use Padlock when you need to:

- inspect contacts
- inspect auth factors
- revoke sessions
- suspend or punish an account
- delete an account

Use Passport when you need to:

- inspect profile-hydrated account data
- inspect current online status
- inspect live activities
- inspect badge counts

## Related Docs

- `docs/PUNISHMENT_API.md`
- `docs/SESSION_MANAGEMENT_API.md`
- `docs/STATUS_AND_CHAT_ONLINE_API.md`
- `docs/PRESENCE_ACTIVITY_API.md`
