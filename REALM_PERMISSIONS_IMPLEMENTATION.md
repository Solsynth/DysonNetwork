# Realm Permissions Implementation Summary

## Overview
This implementation adds role-based detailed realm management with granular permissions for chat and post actions.

## Changes Made

### 1. New Models (DysonNetwork.Shared/Models/Realm.cs)
- **SnRealmRolePermission**: Stores permission flags per role level
- **SnRealmUserPermission**: Stores permission overrides per user
- **SnRealmPostModerationLog**: Stores history of post moderation actions

### 2. Passport Project (Realm Management)

#### Database (DysonNetwork.Passport/AppDatabase.cs)
- Added DbSets for `RealmRolePermissions`, `RealmUserPermissions`, `RealmPostModerationLogs`

#### Service (DysonNetwork.Passport/Realm/RealmService.cs)
- Added permission checking methods:
  - `HasPermission()`: Check if user has specific permission
  - `GetRolePermission()`: Get permission for a role
  - `GetDefaultPermission()`: Get default permission for a role
- Added role permission management:
  - `GetRolePermissions()`: Get all role permissions for a realm
  - `UpdateRolePermission()`: Update role permission
- Added user permission management:
  - `GetUserPermission()`: Get user-specific permission
  - `UpdateUserPermission()`: Update user permission
- Added post moderation:
  - `ModeratePost()`: Create moderation log
  - `IsPostModerated()`: Check if post is already moderated
  - `GetModerationLogs()`: Get moderation history

#### Controller (DysonNetwork.Passport/Realm/RealmController.cs)
- Added endpoints:
  - `GET /api/realms/{slug}/permissions/roles`: Get role permissions
  - `POST /api/realms/{slug}/permissions/roles`: Update role permissions
  - `GET /api/realms/{slug}/permissions/users/{accountId}`: Get user permissions
  - `POST /api/realms/{slug}/permissions/users`: Update user permissions
  - `GET /api/realms/{slug}/posts/moderation-logs`: Get moderation logs

### 3. Sphere Project (Post Permissions)

#### Database (DysonNetwork.Sphere/AppDatabase.cs)
- Added DbSets for `RealmRolePermissions`, `RealmUserPermissions`, `RealmPostModerationLogs`, `RealmMembers`

#### Service (DysonNetwork.Sphere/Realm/RealmPermissionService.cs)
- Created new service for post-related permission checking
- Same permission logic as Passport project

#### Post Service (DysonNetwork.Sphere/Post/PostService.cs)
- Added `RemovePostFromRealmAsync()`: Remove post from realm with notification

#### Controller (DysonNetwork.Sphere/Post/PostActionController.cs)
- Added endpoint:
  - `POST /api/posts/{id}/realm/moderate`: Remove post from realm

### 4. Messager Project (Chat Permissions)

#### Database (DysonNetwork.Messager/AppDatabase.cs)
- Added DbSets for `RealmRolePermissions`, `RealmUserPermissions`, `RealmPostModerationLogs`, `RealmMembers`

#### Service (DysonNetwork.Messager/Realm/RealmPermissionService.cs)
- Created new service for chat-related permission checking

#### Chat Controller (DysonNetwork.Messager/Chat/ChatController.cs)
- Added realm permission check for sending messages

#### Chat Room Controller (DysonNetwork.Messager/Chat/ChatRoomController.cs)
- Added endpoints:
  - `DELETE /api/chat/rooms/{roomId}/messages/{messageId}`: Delete message (moderator)
  - `POST /api/chat/rooms/{roomId}/members/{accountId}/timeout`: Timeout user

### 5. Documentation (docs/RealmPermissions.md)
- Created comprehensive documentation for the permission system

## Permission Types

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

## Key Features

1. **Role-Based Permissions**: Configure permissions for each role (Normal, Moderator, Owner)
2. **User-Specific Overrides**: Override permissions for individual users
3. **Post Moderation**: Remove posts from realm with reason and notification
4. **Chat Moderation**: Delete messages and timeout users
5. **Moderation History**: Track all moderation actions with reasons

## Database Migration Required

After deploying these changes, you need to:
1. Create the new tables in each project's database
2. Run Entity Framework migrations

## Notes

- The permission system is implemented locally in each project to avoid cross-project gRPC calls
- Default permissions match the current behavior (backward compatible)
- Post moderation creates a history to prevent re-linking moderated posts
