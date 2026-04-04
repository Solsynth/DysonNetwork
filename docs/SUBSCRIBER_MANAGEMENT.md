# Publisher Subscriber Management

Allows publishers to manage subscribers directly, with notification controls and subscription lifecycle tracking.

## Overview

Subscribers can be managed through two flows:

1. **User-initiated subscription** - Users subscribe/unsubscribe via normal API
2. **Manager-initiated subscription** - Publishers/managers add or remove subscribers directly

### Subscription Lifecycle

Each subscription tracks its state through the `EndedAt` and `EndReason` fields:

| State | `EndedAt` | `EndReason` | Description |
|-------|-----------|--------------|-------------|
| Active | `null` | `null` | User is subscribed |
| User Left | set | `UserLeft` | User unsubscribed voluntarily |
| Removed | set | `RemovedByPublisher` | Manager removed the user |

### Re-addition Rules

| Previous State | Can Be Re-added by Manager? |
|----------------|----------------------------|
| User Left | No - user chose to leave |
| Removed by Publisher | No - publisher chose to remove |
| Never subscribed | Yes - create new subscription |

## Data Model

### SnPublisherSubscription

```csharp
public class SnPublisherSubscription : ModelBase
{
    public Guid Id { get; set; }
    public Guid PublisherId { get; set; }
    public Guid AccountId { get; set; }
    public Instant? LastReadAt { get; set; }
    public bool Notify { get; set; } = true;

    public Instant? EndedAt { get; set; }
    public SubscriptionEndReason? EndReason { get; set; }
    public Guid? EndedByAccountId { get; set; }

    public bool IsActive => EndedAt == null;
}
```

### SubscriptionEndReason Enum

```csharp
public enum SubscriptionEndReason
{
    UserLeft,             // User voluntarily unsubscribed
    RemovedByPublisher    // Manager removed the subscriber
}
```

## API Endpoints

### List Subscribers

Get all active subscribers for a publisher.

```http
GET /api/publishers/{name}/subscribers?offset=0&take=20
```

**Authorization**: Manager+ role required

**Response:**
```json
[
    {
        "subscription": {
            "id": "uuid",
            "publisherId": "uuid",
            "accountId": "uuid",
            "lastReadAt": "2026-04-05T00:00:00Z",
            "notify": true,
            "endedAt": null,
            "endReason": null,
            "isActive": true,
            "createdAt": "...",
            "updatedAt": "..."
        },
        "account": {
            "id": "uuid",
            "name": "UserName",
            ...
        }
    }
]
```

**Headers:**
- `X-Total` - Total count of active subscribers

### Add Subscriber

Add a subscriber directly (manager action), bypassing approval flow.

```http
POST /api/publishers/{name}/subscribers/{accountId}
```

**Authorization**: Manager+ role required

**Response (success):**
```json
{
    "subscription": {
        "id": "uuid",
        "publisherId": "uuid",
        "accountId": "uuid",
        "notify": true,
        "isActive": true
    },
    "account": { ... }
}
```

**Response (error - cannot be re-added):**
```json
{
    "error": "Account was removed by publisher and cannot be re-added"
}
```

or

```json
{
    "error": "Account left voluntarily and cannot be re-added"
}
```

### Remove Subscriber

Remove a subscriber (manager action). The user will not be able to be re-added.

```http
DELETE /api/publishers/{name}/subscribers/{accountId}
```

**Authorization**: Manager+ role required

**Response:** `204 No Content`

**Errors:**
- `404 Not Found` - Subscriber not found or not active

### Update Notify Setting

Toggle notification preference for a subscriber.

```http
PATCH /api/publishers/{name}/subscribers/{accountId}/notify
Content-Type: application/json

{
    "notify": false
}
```

**Authorization**: The subscriber themselves OR Manager+ role

**Response:**
```json
{
    "id": "uuid",
    "publisherId": "uuid",
    "accountId": "uuid",
    "notify": false,
    "isActive": true
}
```

## Notify Toggle Behavior

When `notify` is set to `false`:
- User will NOT receive push notifications for new posts
- User can still view posts through normal timeline queries

When `notify` is set to `true` (default):
- User WILL receive push notifications for new posts

## Database

### Migration

```
20260404165540_AddPublisherSubscriberManagement.cs
```

Adds columns to `publisher_subscriptions` table:

| Column | Type | Description |
|--------|------|-------------|
| `notify` | boolean | Notify preference (default: true) |
| `ended_at` | timestamp | When subscription ended (null = active) |
| `end_reason` | integer | Reason for ending (null = active) |
| `ended_by_account_id` | uuid | Who ended the subscription |

## Service Methods

### PublisherSubscriptionService

- `GetSubscriptionAsync(accountId, publisherId)` - Gets active subscription
- `GetSubscriptionIncludingEndedAsync(accountId, publisherId)` - Gets any subscription
- `CreateSubscriptionAsync(accountId, publisherId)` - User subscribes
- `CancelSubscriptionAsync(accountId, publisherId)` - User unsubscribes (marks `UserLeft`)
- `AddSubscriberAsync(accountId, publisherId)` - Manager adds subscriber
- `RemoveSubscriberAsync(accountId, publisherId, removedBy)` - Manager removes subscriber
- `UpdateSubscriberNotifyAsync(accountId, publisherId, notify)` - Update notify setting

## Filtering Queries

All active subscriber queries filter by `EndedAt == null`:

```csharp
db.PublisherSubscriptions
    .Where(s => s.PublisherId == publisherId)
    .Where(s => s.EndedAt == null);
```

## Testing Checklist

- [ ] List subscribers shows only active subscriptions
- [ ] Manager can add a new subscriber
- [ ] Manager cannot re-add a user who left voluntarily
- [ ] Manager cannot re-add a user who was removed
- [ ] Manager can remove a subscriber
- [ ] Removed subscriber cannot be re-added
- [ ] User can update their own notify setting
- [ ] Manager can update any subscriber's notify setting
- [ ] Notify=false user doesn't receive post notifications
- [ ] Notify=true user receives post notifications
