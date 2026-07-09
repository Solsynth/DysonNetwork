# Account IsPublic Flag

The `is_public` flag controls whether an account's contacts and connections are visible to other users through the public API.

## Overview

Account contacts (email, phone, address) and connections (third-party platforms like Steam, Discord) now include a boolean `is_public` field. When set to `true`, the resource is visible to other users who can view the account's profile. When `false` (default), only the account owner and authorized services can see it.

This follows the same model as an account's existing `is_primary` and `is_verified` fields on contacts, giving users fine-grained privacy control over each contact method and linked platform.

## Proto Definition

Both messages in `account.proto` carry the field:

| Message | Field | Type | Number | Default |
|---------|-------|------|--------|---------|
| `DyAccountContact` | `is_public` | `bool` | 9 | `false` |
| `DyAccountConnection` | `is_public` | `bool` | 11 | `false` |

## Model Definition

### SnAccountConnection

Added to `DysonNetwork.Shared/Models/Account.cs`:

```csharp
public class SnAccountConnection : ModelBase
{
    // ... existing fields ...
    public bool IsPublic { get; set; } = false;
    // ...
}
```

### SnAccountContact

The `IsPublic` property already existed on the model but was not being serialized to/from proto. This has been fixed. The proto field is now wired through `ToProtoValue()` and `FromProtoValue()`.

## API Behavior

### Contacts

**Proto serialization** (`SnAccountContact.ToProtoValue`):

- `IsPublic = true` → included in proto response as `is_public: true`
- `IsPublic = false` → included in proto response as `is_public: false`

**Proto deserialization** (`SnAccountContact.FromProtoValue`):

- Restores `IsPublic` from incoming proto `is_public` field

### Connections

**gRPC service mapping** (`AccountServiceGrpc.ToProtoConnection`):

```csharp
var proto = new DyAccountConnection
{
    // ... existing fields ...
    IsPublic = connection.IsPublic,
    // ...
};
```

**Remote service mapping** (`RemoteAccountConnectionService.FromProtoValue`):

```csharp
var connection = new SnAccountConnection
{
    // ... existing fields ...
    IsPublic = proto.IsPublic,
    // ...
};
```

### Response Example (Connections)

```json
[
  {
    "id": "a1b2c3d4-...",
    "provider": "steam",
    "provided_identifier": "76561198012345678",
    "meta": {
      "avatar": "https://avatars.steamstatic.com/abc123.jpg"
    },
    "last_used_at": "2025-06-20T12:00:00Z",
    "is_public": true,
    "account_id": "f47ac10b-58cc-...",
    "created_at": "2025-01-15T08:30:00Z",
    "updated_at": "2025-06-20T12:00:00Z"
  }
]
```

## Database

### Migration

`DysonNetwork.Padlock/Migrations/20260709065927_AddIsPublicToAccountConnection.cs`

```csharp
migrationBuilder.AddColumn<bool>(
    name: "is_public",
    table: "account_connections",
    type: "boolean",
    nullable: false,
    defaultValue: false);
```

The `is_public` column is added to `account_connections` with a default of `false`. A migration for contacts is not needed if the column didn't previously exist — verify with your schema.

## Permission Model

| Resource | Visibility when `is_public = true` | Visibility when `is_public = false` |
|----------|-----------------------------------|-------------------------------------|
| Contact | Any authenticated user with `account.contacts` scope | Owner only (and admin with `account.contacts.manage`) |
| Connection | Any authenticated user with `account.connections` scope | Owner only (and admin with `account.connections.manage`) |

Callers filtering public profiles should suppress resources where `is_public` is `false` unless the requesting user owns the account or has admin manage permissions.

## Files Changed

| File | Change |
|------|--------|
| `Spec/proto/account.proto` | Added `is_public` to `DyAccountContact` (9) and `DyAccountConnection` (11) |
| `DysonNetwork.Shared/Proto/Account.cs` | Regenerated with `buf generate` — adds `IsPublic` property on both proto classes |
| `DysonNetwork.Shared/Models/Account.cs` | Added `IsPublic` property on `SnAccountConnection`; fixed `ToProtoValue`/`FromProtoValue` on `SnAccountContact` to serialize `IsPublic` |
| `DysonNetwork.Padlock/Account/AccountServiceGrpc.cs` | Maps `IsPublic` from model to proto in `ToProtoConnection` |
| `DysonNetwork.Shared/Registry/RemoteAccountConnectionService.cs` | Maps `IsPublic` from proto to model in `FromProtoValue` |
| `DysonNetwork.Padlock/Migrations/20260709065927_AddIsPublicToAccountConnection.cs` | Adds `is_public` column to `account_connections` |
