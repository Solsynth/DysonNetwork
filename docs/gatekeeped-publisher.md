# GatekeepedPublisher Feature

Allows publishers to gatekeep their posts behind a follow system with approval workflow.

## Overview

Publishers can enable two separate feature flags:

| Flag | Description |
|------|-------------|
| `followRequiresApproval` | Users must submit follow requests that publishers/managers must approve |
| `postsRequireFollow` | Only users who have been approved as followers can see the publisher's posts |

Both flags are gated behind Stellar program: requires `PerkLevel >= 2`.

## Feature Flags

### Requirements

Enabling `followRequiresApproval` or `postsRequireFollow` requires:
- Publisher must have an associated account
- The account's `PerkLevel` must be >= 2

### Enabling/Disabling

Use the existing feature flag API:

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
    "message": "You are subscribed to this publisher"
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
    "message": "Follow request is pending approval"
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
    "message": "You are following this publisher"
}
```

**Status Values:**
- `none` - No subscription or follow request
- `pending` - Follow request pending approval
- `following` - Approved and following
- `subscribed` - Subscribed (no approval required)
- `rejected` - Follow request was rejected

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

When `postsRequireFollow` is enabled:

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
20260329135038_AddPublisherFollowRequests.cs
```

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

### Filtering

Post visibility filtering is applied in:
- `TimelineService.ListEvents()` / `ListEventsForAnyone()`
- `PostService.FilterUsersByPostVisibility()`
- `PostQueryExtensions.FilterWithVisibility()`

## Testing Checklist

- [ ] Enable `followRequiresApproval` flag
- [ ] Submit follow request as regular user
- [ ] Verify manager sees pending request
- [ ] Approve request and verify subscriber subscription created
- [ ] Reject request and verify rejection reason shown
- [ ] Enable `postsRequireFollow` flag
- [ ] Verify non-followers cannot see posts
- [ ] Verify approved followers can see posts
- [ ] Verify pending/rejected users cannot see posts
- [ ] Test unfollow/cancel request flow
- [ ] Test cleanup job removes expired requests
- [ ] Verify push notifications are sent
