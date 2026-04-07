# Notification Preferences

Allows users to control notification delivery on a per-topic basis with three preference levels.

## Overview

Users can configure how they want to receive notifications for each topic:
- **Normal** - Full delivery (store + push + real-time)
- **Silent** - Store only (no push or real-time delivery)
- **Reject** - Drop entirely (don't store or deliver)

## Preference Levels

| Level | Value | Behavior |
|-------|-------|----------|
| Normal | `0` | Standard notification flow: store in DB, deliver via push providers, WebSocket, and SOP streams |
| Silent | `1` | Store in database only, skip push/websocket/sop delivery |
| Reject | `2` | Discard notification entirely before storage or delivery |

### Flow Diagram

```
SendNotification(topic)
       │
       ▼
Check Preference(topic)
       │
   ┌───┴───┐
   │       │
Reject   ┌─┴────────┐
   │       │         │
   │    Silent    Normal
   │       │         │
   │       ▼         ▼
   │    Store    Store +
   │    only     Enqueue
   │              │
   │              ▼
   └────────► Drop
       │         │
       ▼         ▼
    (none)   DeliverPush
```

## Data Model

### SnNotificationPreference

```csharp
public enum NotificationPreferenceLevel
{
    Normal = 0,
    Silent = 1,
    Reject = 2
}

[Index(nameof(AccountId), nameof(Topic), nameof(DeletedAt), IsUnique = true)]
public class SnNotificationPreference : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    [MaxLength(1024)] public string Topic { get; set; } = null!;
    public NotificationPreferenceLevel Preference { get; set; } = NotificationPreferenceLevel.Normal;
}
```

### Database Table

Table: `notification_preferences`

| Column | Type | Constraints |
|--------|------|-------------|
| id | uuid | PK |
| account_id | uuid | NOT NULL |
| topic | varchar(1024) | NOT NULL |
| preference | integer | NOT NULL, default 0 |
| created_at | timestamptz | NOT NULL |
| updated_at | timestamptz | NOT NULL |
| deleted_at | timestamptz | NULLABLE |

Unique index on `(account_id, topic)` where `deleted_at IS NULL`.

## API Endpoints

All endpoints require authentication via `Authorization` header.

### List All Preferences

```
GET /api/notifications/preferences
```

Returns all custom preferences set by the user. Topics with default (Normal) preference are not stored.

**Response:**
```json
[
  {
    "id": "550e8400-e29b-41d4-a716-446655440000",
    "accountId": "...",
    "topic": "posts.mentions.new",
    "preference": 1,
    "createdAt": "2026-04-08T00:00:00Z",
    "updatedAt": "2026-04-08T00:00:00Z"
  }
]
```

### Get Preference for Topic

```
GET /api/notifications/preferences/{topic}
```

Returns the preference for a specific topic. Returns `Normal` (0) if no custom preference is set.

**Response:**
```json
{
  "preference": 1
}
```

### Set Preference

```
PUT /api/notifications/preferences/{topic}
```

Sets or updates the preference for a topic.

**Request Body:**
```json
{
  "preference": 1
}
```

Accepted values for `preference`:
- `0` - Normal
- `1` - Silent
- `2` - Reject

### Delete Preference

```
DELETE /api/notifications/preferences/{topic}
```

Removes the custom preference, resetting the topic to default (Normal) behavior.

## Examples

### Set topic to Silent

```bash
curl -X PUT "/api/notifications/preferences/posts.mentions.new" \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"preference": 1}'
```

### Reject all post notifications

```bash
curl -X PUT "/api/notifications/preferences/posts.reactions.new" \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"preference": 2}'
```

### Check preference

```bash
curl "/api/notifications/preferences/posts.mentions.new" \
  -H "Authorization: Bearer <token>"
```

### Reset to default

```bash
curl -X DELETE "/api/notifications/preferences/posts.mentions.new" \
  -H "Authorization: Bearer <token>"
```

## Common Topics

| Topic | Description |
|-------|-------------|
| `posts.mentions.new` | Post mentions |
| `post.replies` | Post replies |
| `posts.reactions.new` | New reactions |
| `posts.awards.new` | Post awards |
| `subscriptions.discontinued_in_app` | Subscription discontinued |
| `subscriptions.begun` | Subscription started |
| `gifts.claimed` | Gift claimed |
| `wallets.transactions` | Wallet transactions |
| `auth.verification` | Auth verification |
| `invites.realms` | Realm invites |
| `livestream.started` | Livestream started |
