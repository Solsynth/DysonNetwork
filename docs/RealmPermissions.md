# Realm Permissions System

## Overview

The Realm Permissions System provides fine-grained access control for realm members. It allows realm owners to configure what actions different roles and individual users can perform within a realm.

## Permission Types

### Role-Based Permissions
Permissions are assigned to roles (Owner, Moderator, Normal) and apply to all members with that role.

### User-Specific Permissions
Individual users can have permission overrides that take precedence over role-based permissions.

## Available Permissions

| Permission | Description | Default (Normal) | Default (Moderator) | Default (Owner) |
|------------|-------------|-------------------|---------------------|-----------------|
| `chat.send` | Send messages in realm chat rooms | ✅ | ✅ | ✅ |
| `post.create` | Create posts in the realm feed | ✅ | ✅ | ✅ |
| `post.comment` | Reply to posts in the realm | ✅ | ✅ | ✅ |
| `media.upload` | Upload attachments | ✅ | ✅ | ✅ |
| `post.moderate` | Remove posts from realm | ❌ | ✅ | ✅ |
| `chat.moderate` | Delete messages, timeout users | ❌ | ✅ | ✅ |
| `members.manage` | Invite, remove, change roles | ❌ | ✅ | ✅ |
| `realm.manage` | Edit info, labels, settings | ❌ | ❌ | ✅ |

## Default Role for New Joiners

When a new member joins a realm, they receive the **Normal** role with the following permissions:
- ✅ Can chat
- ✅ Can post
- ✅ Can comment
- ✅ Can upload media
- ❌ Cannot moderate posts
- ❌ Cannot moderate chat
- ❌ Cannot manage members
- ❌ Cannot manage realm

## API Endpoints

### Role Permissions

#### Get Role Permissions
```
GET /api/realms/{slug}/permissions/roles
```

**Response:**
```json
{
  "roles": [
    {
      "roleLevel": 0,
      "canChat": true,
      "canPost": true,
      "canComment": true,
      "canUploadMedia": true,
      "canModeratePosts": false,
      "canModerateChat": false,
      "canManageMembers": false,
      "canManageRealm": false
    },
    {
      "roleLevel": 50,
      "canChat": true,
      "canPost": true,
      "canComment": true,
      "canUploadMedia": true,
      "canModeratePosts": true,
      "canModerateChat": true,
      "canManageMembers": true,
      "canManageRealm": false
    },
    {
      "roleLevel": 100,
      "canChat": true,
      "canPost": true,
      "canComment": true,
      "canUploadMedia": true,
      "canModeratePosts": true,
      "canModerateChat": true,
      "canManageMembers": true,
      "canManageRealm": true
    }
  ]
}
```

#### Update Role Permissions
```
POST /api/realms/{slug}/permissions/roles
```

**Request Body:**
```json
{
  "roleLevel": 0,
  "canChat": true,
  "canPost": true,
  "canComment": true,
  "canUploadMedia": true,
  "canModeratePosts": false,
  "canModerateChat": false,
  "canManageMembers": false,
  "canManageRealm": false
}
```

**Response:** Updated role permission object.

### User Permissions

#### Get User Permissions
```
GET /api/realms/{slug}/permissions/users/{accountId}
```

**Response:**
```json
{
  "accountId": "user-guid",
  "canChat": null,
  "canPost": null,
  "canComment": null,
  "canUploadMedia": null,
  "canModeratePosts": null,
  "canModerateChat": null,
  "canManageMembers": null,
  "canManageRealm": null
}
```

**Note:** `null` values indicate that the role-based permission should be used.

#### Update User Permissions
```
POST /api/realms/{slug}/permissions/users
```

**Request Body:**
```json
{
  "accountId": "user-guid",
  "canChat": true,
  "canPost": false,
  "canComment": null,
  "canUploadMedia": null,
  "canModeratePosts": null,
  "canModerateChat": null,
  "canManageMembers": null,
  "canManageRealm": null
}
```

**Response:** Updated user permission object.

### Post Moderation

#### Remove Post from Realm
```
POST /api/realms/{slug}/posts/{postId}/moderate
```

**Request Body:**
```json
{
  "reason": "Post violates realm rules"
}
```

**Response:**
```json
{
  "success": true,
  "message": "Post removed from realm",
  "moderationLog": {
    "id": "log-guid",
    "realmId": "realm-guid",
    "postId": "post-guid",
    "moderatorAccountId": "moderator-guid",
    "reason": "Post violates realm rules",
    "moderatedAt": "2024-01-01T00:00:00Z"
  }
}
```

#### Get Moderation Logs
```
GET /api/realms/{slug}/posts/moderation-logs
```

**Query Parameters:**
- `offset` (optional): Number of logs to skip (default: 0)
- `take` (optional): Number of logs to return (default: 20)

**Response:**
```json
{
  "logs": [
    {
      "id": "log-guid",
      "realmId": "realm-guid",
      "postId": "post-guid",
      "moderatorAccountId": "moderator-guid",
      "reason": "Post violates realm rules",
      "moderatedAt": "2024-01-01T00:00:00Z",
      "post": {
        "id": "post-guid",
        "title": "Post Title",
        "content": "Post content..."
      },
      "moderator": {
        "id": "moderator-guid",
        "name": "Moderator Name"
      }
    }
  ],
  "total": 50
}
```

### Chat Moderation

#### Delete Message
```
DELETE /api/chat/rooms/{roomId}/messages/{messageId}
```

**Request Body:**
```json
{
  "reason": "Message violates realm rules"
}
```

**Response:** 204 No Content

#### Timeout User
```
POST /api/chat/rooms/{roomId}/members/{accountId}/timeout
```

**Request Body:**
```json
{
  "durationMinutes": 60,
  "reason": "User violated realm rules"
}
```

**Response:**
```json
{
  "success": true,
  "message": "User timed out",
  "timeoutUntil": "2024-01-01T01:00:00Z"
}
```

## Permission Checking Logic

1. **User-Specific Override**: If a user has a specific permission override (non-null), use that value.
2. **Role-Based Permission**: If no user override exists, use the permission for the user's role.
3. **Default Permission**: If no role permission is configured, use the default permission for that role.

## Database Schema

### SnRealmRolePermission
```sql
CREATE TABLE realm_role_permissions (
    id UUID PRIMARY KEY,
    realm_id UUID NOT NULL REFERENCES realms(id),
    role_level INTEGER NOT NULL,
    can_chat BOOLEAN NOT NULL DEFAULT TRUE,
    can_post BOOLEAN NOT NULL DEFAULT TRUE,
    can_comment BOOLEAN NOT NULL DEFAULT TRUE,
    can_upload_media BOOLEAN NOT NULL DEFAULT TRUE,
    can_moderate_posts BOOLEAN NOT NULL DEFAULT FALSE,
    can_moderate_chat BOOLEAN NOT NULL DEFAULT FALSE,
    can_manage_members BOOLEAN NOT NULL DEFAULT FALSE,
    can_manage_realm BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
    deleted_at TIMESTAMP,
    UNIQUE(realm_id, role_level)
);
```

### SnRealmUserPermission
```sql
CREATE TABLE realm_user_permissions (
    id UUID PRIMARY KEY,
    realm_id UUID NOT NULL REFERENCES realms(id),
    account_id UUID NOT NULL,
    can_chat BOOLEAN,
    can_post BOOLEAN,
    can_comment BOOLEAN,
    can_upload_media BOOLEAN,
    can_moderate_posts BOOLEAN,
    can_moderate_chat BOOLEAN,
    can_manage_members BOOLEAN,
    can_manage_realm BOOLEAN,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
    deleted_at TIMESTAMP,
    UNIQUE(realm_id, account_id)
);
```

### SnRealmPostModerationLog
```sql
CREATE TABLE realm_post_moderation_logs (
    id UUID PRIMARY KEY,
    realm_id UUID NOT NULL REFERENCES realms(id),
    post_id UUID NOT NULL,
    moderator_account_id UUID NOT NULL,
    reason TEXT,
    moderated_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
    deleted_at TIMESTAMP
);
```

## Implementation Notes

### Service Architecture
The permission system is implemented as separate services in each project:
- `DysonNetwork.Sphere.Realm.RealmPermissionService` - For post-related permissions
- `DysonNetwork.Messager.Realm.RealmPermissionService` - For chat-related permissions

Each service checks permissions in the following order:
1. User-specific override (if exists)
2. Role-based permission (if configured)
3. Default permission (based on role)

### Database Tables
The permission tables are replicated in each project's database:
- `RealmRolePermissions`
- `RealmUserPermissions`
- `RealmPostModerationLogs`
- `RealmMembers`

This allows each project to check permissions locally without cross-project gRPC calls.

## Examples

### Example 1: Configure Role Permissions
```bash
# Set Normal role to not allow posting
curl -X POST https://api.example.com/api/realms/my-realm/permissions/roles \
  -H "Content-Type: application/json" \
  -d '{
    "roleLevel": 0,
    "canChat": true,
    "canPost": false,
    "canComment": true,
    "canUploadMedia": true,
    "canModeratePosts": false,
    "canModerateChat": false,
    "canManageMembers": false,
    "canManageRealm": false
  }'
```

### Example 2: Override User Permission
```bash
# Allow specific user to moderate posts even if they're Normal role
curl -X POST https://api.example.com/api/realms/my-realm/permissions/users \
  -H "Content-Type: application/json" \
  -d '{
    "accountId": "user-guid",
    "canModeratePosts": true
  }'
```

### Example 3: Remove Post from Realm
```bash
# Remove a post from the realm with a reason
curl -X POST https://api.example.com/api/realms/my-realm/posts/post-guid/moderate \
  -H "Content-Type: application/json" \
  -d '{
    "reason": "Post contains spam"
  }'
```

## Notes

1. **Backward Compatibility**: Existing realms will use default permissions based on the current role system.
2. **Performance**: Permissions are cached to reduce database queries.
3. **Audit Trail**: All post moderation actions are logged for accountability.
4. **No Appeal Process**: Once a post is removed from a realm, it cannot be re-linked to that realm.
