# Plan: Add Role-Based Detailed Realm Management

## Context
The current realm management system has a basic role system with only three levels: Owner (100), Moderator (50), and Normal (0). The user wants to add more granular permissions to control:
- Who can chat in the realm
- Who can post in the realm
- Set default role for new joiners
- Realm moderators can moderate others' posts (remove from realm → make private, remove realm link, send notify)

## Current State
- **Realm Models**: `SnRealm`, `SnRealmMember`, `SnRealmLabel` in `DysonNetwork.Shared/Models/Realm.cs`
- **Role System**: Simple integer-based roles (Owner=100, Moderator=50, Normal=0)
- **Permission System**: Existing ABAC permission system in `DysonNetwork.Padlock/Permission/` with `SnPermissionNode` model
- **Realm Service**: `DysonNetwork.Passport/Realm/RealmService.cs` with basic member management
- **Realm Controller**: `DysonNetwork.Passport/Realm/RealmController.cs` with CRUD operations
- **Chat Service**: `DysonNetwork.Messager/Chat/ChatService.cs` - messages linked to rooms, rooms have `RealmId`
- **Post Service**: `DysonNetwork.Sphere/Post/PostService.cs` - posts have `RealmId` field
- **Post Model**: `SnPost` has `RealmId` field and `Visibility` property
- **Chat Rooms**: `SnChatRoom` has `RealmId` field

## Requirements
1. **Granular Permissions**: Realm members can have different permissions for:
   - Chat: Send messages in realm chat rooms
   - Post: Create posts in the realm feed
   - Comment: Reply to posts in the realm
   - Media: Upload attachments
   - Moderate Posts: Remove posts from realm
   - Moderate Chat: Delete messages, timeout users
   - Manage Members: Invite, remove, change roles
   - Manage Realm: Edit info, labels, settings

2. **Default Role**: New joiners get a default role that can do everything except managing:
   - Can chat: Yes
   - Can post: Yes
   - Can comment: Yes
   - Can upload media: Yes
   - Can moderate posts: No
   - Can moderate chat: No
   - Can manage members: No
   - Can manage realm: No

3. **Post Moderation**: Realm moderators can:
   - Remove posts from realm (make private, remove realm link, but don't delete)
   - Include reason for removal
   - Create a history in DB to prevent the same post from being edited to link the realm again
   - Send notification to post author
   - No appeal process

4. **Chat Moderation**: Realm moderators can:
   - Delete messages in realm chat
   - Timeout users in realm chat
   - Separate permissions from post moderation

## Approach: Hybrid System

### 1. Extend Role System with Permission Flags
Keep the existing role hierarchy (Owner, Moderator, Normal) but add permission flags to each role.

### 2. Add Realm Permission Models
Create new models for realm-specific permissions:
- `SnRealmRolePermission`: Permission flags per role
- `SnRealmUserPermission`: Permission overrides per user
- `SnRealmPostModerationLog`: History of post moderation actions

### 3. Add Post Moderation Feature
Create a new method in `PostService` to remove posts from realm (soft delete) with moderation logging.

## Files to Modify

### 1. `DysonNetwork.Shared/Models/Realm.cs`
- Add `SnRealmRolePermission` model
- Add `SnRealmUserPermission` model
- Add `SnRealmPostModerationLog` model
- Add `DefaultRole` field to `SnRealm`
- Add permission flags to `RealmMemberRole`

### 2. `DysonNetwork.Passport/Realm/RealmService.cs`
- Add permission checking methods (per-role and per-user)
- Add role management methods
- Add post moderation methods
- Add chat moderation methods

### 3. `DysonNetwork.Passport/Realm/RealmController.cs`
- Add endpoints for managing role permissions
- Add endpoints for managing user permissions
- Add endpoints for role management
- Add endpoints for post moderation
- Add endpoints for chat moderation

### 4. `DysonNetwork.Passport/AppDatabase.cs`
- Add new DbSets for realm permissions and moderation logs

### 5. `DysonNetwork.Sphere/Post/PostService.cs`
- Add `RemovePostFromRealmAsync` method
- Add `GetRealmPostModerationLogsAsync` method

### 6. `DysonNetwork.Sphere/Post/PostActionController.cs`
- Add endpoint for removing posts from realm

### 7. `DysonNetwork.Messager/Chat/ChatController.cs`
- Update chat permission checks to use new permission system
- Add endpoints for deleting messages and timing out users

### 8. `DysonNetwork.Shared/Registry/RemoteRealmService.cs`
- Add new gRPC methods for permission checking

### 9. `DysonNetwork.Passport/Realm/RealmServiceGrpc.cs`
- Implement new gRPC methods

### 10. `docs/RealmPermissions.md`
- Create documentation for the new permission system

## Implementation Steps

### Step 1: Add Realm Permission Models
```csharp
public class SnRealmRolePermission : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RealmId { get; set; }
    public SnRealm Realm { get; set; } = null!;
    
    // Role this permission set applies to
    public int RoleLevel { get; set; } = RealmMemberRole.Normal;
    
    // Permission flags
    public bool CanChat { get; set; } = true;
    public bool CanPost { get; set; } = true;
    public bool CanComment { get; set; } = true;
    public bool CanUploadMedia { get; set; } = true;
    public bool CanModeratePosts { get; set; } = false;
    public bool CanModerateChat { get; set; } = false;
    public bool CanManageMembers { get; set; } = false;
    public bool CanManageRealm { get; set; } = false;
}

public class SnRealmUserPermission : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RealmId { get; set; }
    public SnRealm Realm { get; set; } = null!;
    public Guid AccountId { get; set; }
    
    // Permission overrides (null = use role default)
    public bool? CanChat { get; set; }
    public bool? CanPost { get; set; }
    public bool? CanComment { get; set; }
    public bool? CanUploadMedia { get; set; }
    public bool? CanModeratePosts { get; set; }
    public bool? CanModerateChat { get; set; }
    public bool? CanManageMembers { get; set; }
    public bool? CanManageRealm { get; set; }
}

public class SnRealmPostModerationLog : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RealmId { get; set; }
    public SnRealm Realm { get; set; } = null!;
    public Guid PostId { get; set; }
    public Guid ModeratorAccountId { get; set; }
    public string? Reason { get; set; }
    public Instant ModeratedAt { get; set; } = SystemClock.Instance.GetCurrentInstant();
}
```

### Step 2: Update SnRealm Model
- Add `DefaultRole` field to `SnRealm`
- Add `RolePermissions` collection
- Add `UserPermissions` collection
- Add `PostModerationLogs` collection

### Step 3: Add Permission Checking Methods
```csharp
// In RealmService.cs
public async Task<bool> HasPermission(Guid realmId, Guid accountId, string permission)
{
    // Check user-specific override first
    var userPermission = await db.RealmUserPermissions
        .FirstOrDefaultAsync(p => p.RealmId == realmId && p.AccountId == accountId);
    
    if (userPermission != null)
    {
        return permission switch
        {
            "chat.send" => userPermission.CanChat ?? await GetRolePermission(realmId, accountId, permission),
            "post.create" => userPermission.CanPost ?? await GetRolePermission(realmId, accountId, permission),
            "post.comment" => userPermission.CanComment ?? await GetRolePermission(realmId, accountId, permission),
            "media.upload" => userPermission.CanUploadMedia ?? await GetRolePermission(realmId, accountId, permission),
            "post.moderate" => userPermission.CanModeratePosts ?? await GetRolePermission(realmId, accountId, permission),
            "chat.moderate" => userPermission.CanModerateChat ?? await GetRolePermission(realmId, accountId, permission),
            "members.manage" => userPermission.CanManageMembers ?? await GetRolePermission(realmId, accountId, permission),
            "realm.manage" => userPermission.CanManageRealm ?? await GetRolePermission(realmId, accountId, permission),
            _ => false
        };
    }
    
    return await GetRolePermission(realmId, accountId, permission);
}

private async Task<bool> GetRolePermission(Guid realmId, Guid accountId, string permission)
{
    var member = await GetActiveMember(realmId, accountId);
    if (member == null) return false;
    
    var rolePermission = await db.RealmRolePermissions
        .FirstOrDefaultAsync(p => p.RealmId == realmId && p.RoleLevel == member.Role);
    
    return permission switch
    {
        "chat.send" => rolePermission?.CanChat ?? GetDefaultPermission(member.Role, permission),
        "post.create" => rolePermission?.CanPost ?? GetDefaultPermission(member.Role, permission),
        "post.comment" => rolePermission?.CanComment ?? GetDefaultPermission(member.Role, permission),
        "media.upload" => rolePermission?.CanUploadMedia ?? GetDefaultPermission(member.Role, permission),
        "post.moderate" => rolePermission?.CanModeratePosts ?? GetDefaultPermission(member.Role, permission),
        "chat.moderate" => rolePermission?.CanModerateChat ?? GetDefaultPermission(member.Role, permission),
        "members.manage" => rolePermission?.CanManageMembers ?? GetDefaultPermission(member.Role, permission),
        "realm.manage" => rolePermission?.CanManageRealm ?? GetDefaultPermission(member.Role, permission),
        _ => false
    };
}

private bool GetDefaultPermission(int role, string permission)
{
    // Default permissions for each role
    return role switch
    {
        RealmMemberRole.Owner => true,
        RealmMemberRole.Moderator => permission switch
        {
            "chat.send" => true,
            "post.create" => true,
            "post.comment" => true,
            "media.upload" => true,
            "post.moderate" => true,
            "chat.moderate" => true,
            "members.manage" => true,
            "realm.manage" => false,
            _ => false
        },
        RealmMemberRole.Normal => permission switch
        {
            "chat.send" => true,
            "post.create" => true,
            "post.comment" => true,
            "media.upload" => true,
            "post.moderate" => false,
            "chat.moderate" => false,
            "members.manage" => false,
            "realm.manage" => false,
            _ => false
        },
        _ => false
    };
}
```

### Step 4: Add Post Moderation Method
```csharp
// In PostService.cs
public async Task<SnPost> RemovePostFromRealmAsync(SnPost post, Guid moderatorAccountId, string? reason)
{
    // Check if post is already moderated
    var existingLog = await db.RealmPostModerationLogs
        .FirstOrDefaultAsync(l => l.PostId == post.Id && l.RealmId == post.RealmId);
    
    if (existingLog != null)
        throw new InvalidOperationException("This post has already been removed from the realm.");
    
    // Create moderation log
    var log = new SnRealmPostModerationLog
    {
        RealmId = post.RealmId!.Value,
        PostId = post.Id,
        ModeratorAccountId = moderatorAccountId,
        Reason = reason
    };
    db.RealmPostModerationLogs.Add(log);
    
    // Make post private
    post.Visibility = PostVisibility.Private;
    
    // Remove realm link
    post.RealmId = null;
    
    // Update post
    db.Posts.Update(post);
    await db.SaveChangesAsync();
    
    // Send notification to post author
    await SendPostRemovedNotification(post, moderatorAccountId, reason);
    
    return post;
}
```

### Step 5: Add API Endpoints
```csharp
// In RealmController.cs
[HttpPost("{slug}/permissions/role")]
public async Task<ActionResult> UpdateRolePermissions(string slug, [FromBody] RealmRolePermissionRequest request)
{
    // Update role permissions
}

[HttpPost("{slug}/permissions/user")]
public async Task<ActionResult> UpdateUserPermissions(string slug, [FromBody] RealmUserPermissionRequest request)
{
    // Update user permissions
}

[HttpPost("{slug}/posts/{postId}/moderate")]
public async Task<ActionResult> ModeratePost(string slug, Guid postId, [FromBody] ModeratePostRequest request)
{
    // Remove post from realm
}

[HttpGet("{slug}/posts/moderation-logs")]
public async Task<ActionResult> GetModerationLogs(string slug)
{
    // Get moderation logs
}
```

### Step 6: Update Chat Permission Checks
```csharp
// In ChatController.cs
if (room.RealmId.HasValue)
{
    if (!await rs.HasPermission(room.RealmId.Value, accountId, "chat.send"))
        return StatusCode(403, "You do not have permission to send messages in this realm.");
}
```

## Verification

1. **Unit Tests**: Test permission checking methods
2. **Integration Tests**: Test API endpoints
3. **Manual Testing**:
   - Create realm with custom permissions
   - Join realm and verify permissions
   - Moderate posts and verify notifications
   - Test default role for new joiners
   - Test chat moderation (delete messages, timeout users)
   - Verify moderation logs are created

## Backward Compatibility
- Existing realms will use default permissions (Owner=100, Moderator=50, Normal=0)
- New permission system is opt-in
- Old role system still works for UI/display
- Default permissions match current behavior