# Realm Admin API

Admin-facing realm moderation APIs in `DysonNetwork.Passport`.

All routes below are local development routes. In production, the gateway rewrites them to:

- `/passport/admin/realms/...`

All JSON fields are serialized as `snake_case`.

## Overview

Controller: `DysonNetwork.Passport/Realm/RealmAdminController.cs`

Base route:

```text
/api/admin/realms
```

Use them for:

- listing realms across the system with moderation filters
- reading realm detail with member / label / boost counts
- updating realm metadata without owner membership checks
- setting or clearing realm verification marks
- listing, role-adjusting, and removing members as admin
- deleting realms as admin

## Permissions

| Permission | Purpose |
| --- | --- |
| `realms.moderate` | List/detail realms, verification, admin member listing |
| `realms.update` | Update realm metadata as admin |
| `realms.members.manage` | Change member roles and kick members |
| `realms.delete` | Delete a realm through the admin API |

## Listing And Detail

### GET /api/admin/realms

Required permission: `realms.moderate`

Query parameters:

- `query` optional text search across `slug`, `name`, and `description`
- `is_public` optional boolean
- `is_community` optional boolean
- `verified` optional boolean (has verification mark)
- `account_id` optional owner account filter
- `offset` default `0`
- `take` default `50`, max `200`

Response headers:

- `X-Total` total matching count

Notes:

- ordered by `updated_at` descending
- boost points are refreshed for the returned page

### GET /api/admin/realms/{slug}

Required permission: `realms.moderate`

Response shape:

```json
{
  "realm": { "...": "..." },
  "member_count": 42,
  "pending_invite_count": 2,
  "label_count": 3,
  "active_boost_contribution_count": 8
}
```

## Profile And Verification

### PATCH /api/admin/realms/{slug}

Required permission: `realms.update`

```json
{
  "slug": "new-slug",
  "name": "Display Name",
  "description": "About this realm",
  "is_community": true,
  "is_public": true,
  "account_id": "550e8400-e29b-41d4-a716-446655440000"
}
```

All fields optional. Bypasses normal membership-role checks.

### POST /api/admin/realms/{slug}/verification

Required permission: `realms.moderate`

```json
{
  "type": "organization",
  "title": "Official Realm",
  "description": "Optional",
  "verified_by": "admin"
}
```

### DELETE /api/admin/realms/{slug}/verification

Required permission: `realms.moderate`

## Members

### GET /api/admin/realms/{slug}/members

Required permission: `realms.moderate`

Query parameters:

- `role` optional role integer (`0` normal, `50` moderator, `100` owner)
- `pending_only` default `false` — when true, returns pending invites
- `offset` / `take`

Response includes hydrated `account` when available, and `X-Total`.

### PATCH /api/admin/realms/{slug}/members/{memberId}/role

Required permission: `realms.members.manage`

```json
{
  "role": 50
}
```

`memberId` is the member account id. Admin may set any role, including owner (`100`).

### DELETE /api/admin/realms/{slug}/members/{memberId}

Required permission: `realms.members.manage`

Sets `leave_at` on the membership. Returns `204`.

## Delete

### DELETE /api/admin/realms/{slug}

Required permission: `realms.delete`

Removes the realm and marks related memberships deleted/left. Returns `204`.

## Action Logging

| Action type | Operations |
| --- | --- |
| `realms.update` | Admin profile update |
| `realms.moderate` | Verification, member role changes |
| `realms.kick` | Admin member removal |
| `realms.delete` | Admin delete |

## Related Docs

- [REALM_PERMISSION.md](./REALM_PERMISSION.md)
- [REALM_MEMBER_FILTERING.md](./REALM_MEMBER_FILTERING.md)
- [SPHERE_ADMIN_API.md](./SPHERE_ADMIN_API.md)
- [PERMISSIONS.md](./PERMISSIONS.md)
