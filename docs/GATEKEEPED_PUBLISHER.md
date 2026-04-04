# GatekeepedPublisher Feature

Allows publishers to gatekeep their posts behind a follow system with approval workflow.

## Overview

Publishers can enable two separate settings stored as properties on `SnPublisher`:

| Property | Description |
|----------|-------------|
| `IsModerateSubscription` | Users must submit follow requests that publishers/managers must approve |
| `IsGatekept` | Only users who have been approved as followers can see the publisher's posts |

Internally, `IsModerateSubscription` maps to the `ModerateSubscription` database column and `IsGatekept` maps to `GatekeptFollows`. Both are nullable booleans where `null` means disabled.

Both features are gated behind Stellar program: requires `PerkLevel >= 2` for publishers with an associated account.

## Model Properties

### SnPublisher

```csharp
public bool? GatekeptFollows { get; set; }
public bool? ModerateSubscription { get; set; }

public bool IsGatekept => GatekeptFollows ?? false;
public bool IsModerateSubscription => ModerateSubscription ?? false;
```

### Feature Flags (Legacy)

For backward compatibility with the API, these flag names are still used:

| Flag Name (API) | Maps To |
|-----------------|---------|
| `followRequiresApproval` | `ModerateSubscription` / `IsModerateSubscription` |
| `postsRequireFollow` | `GatekeptFollows` / `IsGatekept` |

### Requirements

Enabling `followRequiresApproval` or `postsRequireFollow` requires:
- Publisher must have an associated account
- The account's `PerkLevel` must be >= 2

### Enabling/Disabling

Use the feature flag API (internally sets publisher properties):

```http
POST /api/publishers/{name}/features
Content-Type: application/json

{
    "flag": "followRequiresApproval"
}
```

```http
DELETE /api/publishers/{name}/features?flag=followRequiresApproval
```

### Available Flags

| Flag | Description | PerkLevel Required |
|------|-------------|-------------------|
| `followRequiresApproval` | Require approval for follow requests | >= 2 |
| `postsRequireFollow` | Only show posts to approved followers | >= 2 |

### System-Only Flags

The following flags cannot be enabled manually via API:
- `develop` - Created and managed by external services

Attempting to enable these flags will return:
```json
{
    "error": "Flag 'develop' is a system flag and cannot be enabled manually"
}
```

## API Endpoints

### Check Subscription Status

Get the current subscription and follow request status in one call.

```http
GET /api/publishers/{name}/subscription
```

**Response (no follow required):**
```json
{
    "subscription": { ... },
    "followRequest": null,
    "requiresApproval": false,
    "status": "subscribed",
    "message": "You are subscribed to this publisher",
    "isPending": false,
    "isActive": true
}
```

**Response (no follow required, ended):**
```json
{
    "subscription": {
        "id": "uuid",
        "endedAt": "2026-04-05T00:00:00Z",
        "endReason": "UserLeft",
        "isActive": false,
        ...
    },
    "followRequest": null,
    "requiresApproval": false,
    "status": "ended",
    "message": "Your subscription has ended",
    "isPending": false,
    "isActive": false
}
```

**Response (follow required, pending):**
```json
{
    "subscription": null,
    "followRequest": {
        "id": "uuid",
        "state": "Pending",
        ...
    },
    "requiresApproval": true,
    "status": "pending",
    "message": "Follow request is pending approval",
    "isPending": true,
    "isActive": false
}
```

**Response (follow required, accepted):**
```json
{
    "subscription": { ... },
    "followRequest": {
        "id": "uuid",
        "state": "Accepted",
        ...
    },
    "requiresApproval": true,
    "status": "following",
    "message": "You are following this publisher",
    "isPending": false,
    "isActive": true
}
```

**Status Values:**
- `none` - No subscription or follow request
- `pending` - Follow request pending approval
- `following` - Approved and following
- `subscribed` - Subscribed (no approval required)
- `rejected` - Follow request was rejected
- `ended` - Subscription has ended (user left or was removed)

**Boolean Flags:**
- `isPending` - True when follow request is pending
- `isActive` - True when subscription is active (not ended)

### Subscribe / Follow

Subscribe or submit follow request. Prevents duplicate requests.

```http
POST /api/publishers/{name}/subscribe
```

Returns 400 if:
- Follow request already pending
- Already following/subscribed
- Follow request was rejected

### Unsubscribe

Cancel subscription or follow request.

```http
POST /api/publishers/{name}/unsubscribe
```

### List Pending Follow Requests (Managers+)

Get all pending follow requests for a publisher.

```http
GET /api/publishers/{name}/subscription/requests
```

**Response:**
```json
[
    {
        "id": "uuid",
        "accountId": "uuid",
        "state": "Pending",
        "createdAt": "2026-03-29T00:00:00Z",
        "account": {
                "id": "uuid",
                "name": "UserName",
                "avatar": { ... }
            }
        }
    ]
}
```

### Approve Follow Request

Approve a user's follow request.

```http
POST /api/publishers/{name}/subscription/requests/{requestId}/approve
```

The user will receive a push notification and in-app notification.

### Reject Follow Request

Reject a user's follow request with optional reason.

```http
POST /api/publishers/{name}/subscription/requests/{requestId}/reject
Content-Type: application/json

{
    "reason": "Optional rejection reason"
}
```

The user will receive a push notification and in-app notification explaining the rejection.

## Data Model

### SnPublisherFollowRequest

| Field | Type | Description |
|-------|------|-------------|
| `Id` | Guid | Primary key |
| `PublisherId` | Guid | Publisher being followed |
| `AccountId` | Guid | User making the request |
| `State` | FollowRequestState | Pending, Accepted, or Rejected |
| `ReviewedAt` | Instant? | When request was reviewed |
| `ReviewedByAccountId` | Guid? | Who reviewed the request |
| `RejectReason` | string? | Reason for rejection |
| `CreatedAt` | Instant | Request creation time |
| `UpdatedAt` | Instant | Last update time |
| `DeletedAt` | Instant? | Soft delete timestamp |

### FollowRequestState Enum

```csharp
public enum FollowRequestState
{
    Pending,   // Awaiting review
    Accepted,  // Approved, user can see posts
    Rejected   // Denied
}
```

## Post Visibility

When `IsGatekept` (via `postsRequireFollow` flag) is enabled:

1. **Private/Friends posts** - Still restricted to publisher members (existing behavior)
2. **Public/Unlisted posts** - Restricted to:
   - Publisher members
   - Users with `Accepted` follow requests
   - The post author themselves

This means:
- Anonymous users and non-followers see nothing
- Users with `Pending` requests see nothing
- Users with `Rejected` requests see nothing
- Only `Accepted` followers can see the posts

## Notifications

Three notification types are sent:

| Event | Recipient | Content |
|-------|-----------|---------|
| Follow request received | Publisher + Managers | "{User} wants to follow {Publisher}" |
| Request approved | Requester | "Your follow request was approved" |
| Request rejected | Requester | "Your follow request was rejected" + reason |

### Localization Keys

English (`en.json`):
- `followRequestReceivedTitle` / `followRequestReceivedBody`
- `followRequestApprovedTitle` / `followRequestApprovedBody`
- `followRequestRejectedTitle` / `followRequestRejectedBody`

Chinese (`zh-hans.json`):
- Same keys with Chinese translations

## Cleanup Job

Expired follow requests are automatically cleaned up by `PublisherFollowRequestCleanupJob`:

- **Schedule**: Daily at 6:00 AM
- **Behavior**: Deletes requests in `Pending` state older than 7 days
- **Job Name**: `PublisherFollowRequestCleanup`

## Database

### Migration

```
20260404151939_AddPublisherGatekeepFields.cs
```

Adds columns to `publishers` table:
- `gatekept_follows` (boolean, nullable) - Maps to `IsGatekept`
- `moderate_subscription` (boolean, nullable) - Maps to `IsModerateSubscription`

Migrates existing feature flag data to the new columns and removes the feature flag records.

### Legacy Table: publisher_follow_requests

Created by migration `20260329135038_AddPublisherFollowRequests.cs`:

Creates table `publisher_follow_requests` with:
- Foreign key to `publishers` table (CASCADE delete)
- Index on `publisher_id` for efficient queries

## Implementation Details

### Services

- **PublisherService** - Core follow request logic
  - `CreateFollowRequest()` - Submit new request
  - `ApproveFollowRequest()` - Approve and notify user
  - `RejectFollowRequest()` - Reject with reason and notify
  - `GetFollowRequest()` - Get user's request
  - `GetPendingFollowRequests()` - List pending for publisher
  - `CleanupExpiredFollowRequests()` - Delete expired requests
  - `HasAcceptedFollowRequest()` - Check if user is follower
  - `HasFollowRequiresApprovalFlag()` - Reads from `publisher.IsModerateSubscription`
  - `HasPostsRequireFollowFlag()` - Reads from `publisher.IsGatekept`

### Property Access

Instead of querying `PublisherFeatures` table, the services now read directly from `SnPublisher`:

```csharp
// Check if follow requires approval
publisher.IsModerateSubscription  // true = moderate subscription enabled

// Check if posts are gatekept  
publisher.IsGatekept  // true = posts require follow
```

### Filtering

Post visibility filtering is applied in:
- `TimelineService.ListEvents()` / `ListEventsForAnyone()`
- `PostService.FilterUsersByPostVisibility()`
- `PostQueryExtensions.FilterWithVisibility()` - Uses `gatekeptPublisherIds` and `followerPublisherIds`

## Testing Checklist

- [ ] Enable `followRequiresApproval` (sets `ModerateSubscription = true`)
- [ ] Submit follow request as regular user
- [ ] Verify manager sees pending request
- [ ] Approve request and verify subscription created
- [ ] Reject request and verify rejection reason shown
- [ ] Enable `postsRequireFollow` (sets `GatekeptFollows = true`)
- [ ] Verify non-followers cannot see posts
- [ ] Verify approved followers can see posts
- [ ] Verify pending/rejected users cannot see posts
- [ ] Test unfollow/cancel request flow
- [ ] Test cleanup job removes expired requests
- [ ] Verify push notifications are sent
- [ ] Verify `IsModerateSubscription` and `IsGatekept` properties work correctly
