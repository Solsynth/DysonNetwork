# Punishment API

Manages account punishments/t sanctions in the Padlock system.

## Punishment Types

| Type | Description |
| --- | --- |
| `permission_modification` | Blocks specific permissions via `blocked_permissions` list |
| `block_login` | Prevents account from logging in |
| `disable_account` | Disables the account entirely |
| `strike` | A strike/warning penalty |

## Permission Modification

When `type` is set to `permission_modification`, the system blocks the permissions listed in `blocked_permissions`.

**How it works:**
- The `PermissionService` filters out blocked permissions at runtime
- Supports exact permission keys (e.g., `chat.send`) and wildcards (e.g., `chat.*`)
- Checks active punishments (where `expired_at` is null or in the future)
- Returns `null`/default when a permission is blocked

**Example:**
```json
{
    "reason": "Violated terms of service",
    "type": "permission_modification",
    "blocked_permissions": ["chat.send", "drive.*"]
}
```
This blocks `chat.send` permission and all permissions starting with `drive.`.

## Data Model

```csharp
public class SnAccountPunishment
{
    public Guid Id { get; set; }
    public string Reason { get; set; }          // Max 8192 chars
    public Instant? ExpiredAt                  // Optional expiration time
    public PunishmentType Type { get; set; }
    public List<string>? BlockedPermissions    // Permissions to block (jsonb)
    public Guid AccountId { get; set; }
    public SnAccount Account { get; set; }
}
```

## API Endpoints

All endpoints require authorization via the `Authorize` attribute.

### Get Punishments

```
GET /api/admin/accounts/{name}/punishments
```

Returns all punishments for the specified account.

**Response:** `List<SnAccountPunishment>`

### Create Punishment

```
POST /api/admin/accounts/{name}/punishments
```

Requires permission: `punishments.create`

**Request Body:**
```json
{
    "reason": "string",
    "expired_at": "2024-01-01T00:00:00Z",  // optional
    "type": "block_login",
    "blocked_permissions": ["chat.send"]  // optional
}
```

**Response:** `List<SnAccountPunishment>`

### Update Punishment

```
PATCH /api/admin/accounts/{name}/punishments/{punishmentId}
```

Requires permission: `punishments.update`

All fields are optional - only provided fields are updated.

**Request Body:**
```json
{
    "reason": "string",
    "expired_at": "2024-01-01T00:00:00Z",
    "type": "strike",
    "blocked_permissions": ["drive.upload"]
}
```

**Response:** `SnAccountPunishment`

### Delete Punishment

```
DELETE /api/admin/accounts/{name}/punishments/{punishmentId}
```

Requires permission: `punishments.delete`

**Response:** `200 OK`

### Admin Delete Account

```
DELETE /api/admin/accounts/{name}
```

Requires permission: `accounts.deletion`

**Response:** `200 OK`

## Permissions Required

| Endpoint | Permission |
| --- | --- |
| Create Punishment | `punishments.create` |
| Update Punishment | `punishments.update` |
| Delete Punishment | `punishments.delete` |
| Delete Account | `accounts.deletion` |