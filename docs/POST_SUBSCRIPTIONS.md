# Post Subscription Notifications

This document describes the post-related subscription features in `DysonNetwork.Sphere`:

- collection subscriptions that receive new post notifications
- explicit per-post subscriptions that receive follow-up update notifications

All API responses use `snake_case`.

## Overview

There are now two different subscription paths around posts:

1. `SnPostCategorySubscription` can target a category, tag, or collection.
2. `SnPostSubscription` targets one specific post.

They serve different purposes:

- category/tag/collection subscriptions notify when a newly published post matches the subscribed target
- post subscriptions notify when an already-known post later receives reactions, boosts, or meaningful edits

## Collection Subscription Notifications

Collection subscriptions reuse the existing `post_category_subscriptions` table.

### Data Model

`SnPostCategorySubscription` now supports:

```csharp
public Guid? CollectionId { get; set; }
public SnPostCollection? Collection { get; set; }
```

This means a subscription row can point to exactly one of:

- `category_id`
- `tag_id`
- `collection_id`

### Behavior

When a post is published through the existing publisher subscription flow, `PublisherSubscriptionService` also checks the collections that contain that post.

Users subscribed to any of those collections receive the same new-post notification flow as publisher, tag, and category subscribers.

### Collection Subscription Endpoints

Collection subscriptions are managed under the collection API surface:

```http
POST /api/publishers/{publisherName}/collections/{slug}/subscribe
POST /api/publishers/{publisherName}/collections/{slug}/unsubscribe
GET /api/publishers/{publisherName}/collections/{slug}/subscription
```

The current user's category, tag, and collection subscriptions can be listed through the existing subscription listing endpoint.

## Explicit Post Subscriptions

Explicit post subscriptions are separate from bookmarks.

- bookmarks are private saved-post records
- post subscriptions are notification preferences for one post

### Data Model

`SnPostSubscription` is stored in `post_subscriptions`.

```csharp
public class SnPostSubscription : ModelBase
{
    public Guid Id { get; set; }
    public Guid PostId { get; set; }
    public Guid AccountId { get; set; }
    public bool NotifyReactions { get; set; } = true;
    public bool NotifyForwards { get; set; } = true;
    public bool NotifyEdits { get; set; } = true;
}
```

### Rules

1. Post subscriptions are explicit and per-user.
2. A user can have at most one active subscription per post.
3. Re-subscribing updates the existing subscription instead of creating another row.
4. The user must be able to view the post in order to subscribe to it or fetch its subscription status.
5. The acting user does not receive their own subscription-triggered notification.

## Post Subscription API

Base URL:

```http
/api/posts
```

### Subscribe To A Post

```http
POST /api/posts/{id}/subscribe
```

### Request Body

All fields are optional. If omitted on first subscribe, they default to `true`.

```json
{
  "reactions": true,
  "forwards": true,
  "edits": true
}
```

### Behavior

- requires authentication
- returns `404` if the post is not visible to the current user
- creates a new subscription when none exists
- updates the existing subscription flags when one already exists

### Response Shape

```json
{
  "id": "subscription-id",
  "post_id": "post-id",
  "account_id": "account-id",
  "notify_reactions": true,
  "notify_forwards": true,
  "notify_edits": true,
  "created_at": "2026-05-22T00:00:00Z",
  "updated_at": "2026-05-22T00:00:00Z"
}
```

### Unsubscribe From A Post

```http
POST /api/posts/{id}/unsubscribe
```

### Behavior

- requires authentication
- removes the current user's subscription for the target post
- returns `204 No Content` whether or not a subscription existed

### Get Subscription Status

```http
GET /api/posts/{id}/subscription
```

### Behavior

- requires authentication
- returns `404` if the post is not visible
- returns `404` if the current user has no subscription record for that post

### List Current User Post Subscriptions

```http
GET /api/posts/subscriptions?offset=0&take=20
```

### Query Parameters

- `offset`: pagination offset
- `take`: page size

### Response Headers

- `X-Total`: total subscription count for the current user

### Response Shape

```json
[
  {
    "subscription": {
      "id": "subscription-id",
      "post_id": "post-id",
      "account_id": "account-id",
      "notify_reactions": true,
      "notify_forwards": false,
      "notify_edits": true
    },
    "post": {
      "id": "post-id",
      "title": "Subscribed post",
      "publisher": {
        "id": "publisher-id",
        "name": "publisher-name"
      }
    }
  }
]
```

## Notification Triggers

Post subscriptions currently support three event types.

### Reaction Notifications

Topic:

```text
posts.subscriptions.reactions
```

Triggered when:

- a reaction is added to the subscribed post

Not triggered when:

- a reaction is removed

Additional notification meta includes:

- `notification_type = "post_subscription"`
- `subscription_event_type = "reaction"`
- `reaction`
- `reaction_attitude`
- `actor_id`
- `actor_name`

### Forward Notifications

Topic:

```text
posts.subscriptions.forwards
```

Triggered when:

- a local Sphere boost is created for the subscribed post

Not triggered when:

- remote ActivityPub boosts arrive
- an existing boost is removed

Additional notification meta includes:

- `notification_type = "post_subscription"`
- `subscription_event_type = "forward"`
- `actor_publisher_id`
- `actor_publisher_name`
- `actor_publisher_nick`

### Edit Notifications

Topic:

```text
posts.subscriptions.edits
```

Triggered when:

- a post was already published before the update
- the post remains published after the update
- the update changes meaningful content

Meaningful edits currently include changes to:

- `title`
- `description`
- `content`
- `visibility`
- `drafted_at`
- `published_at`
- attachment membership

Not triggered when:

- the post is being published for the first time
- the post is draft-only before or after the update
- only unrelated subscription systems are involved

Additional notification meta includes:

- `notification_type = "post_subscription"`
- `subscription_event_type = "edit"`

## Notes

- Post subscriptions do not replace bookmarks.
- Collection subscriptions and post subscriptions can both exist for the same user.
- A user may first discover a post via publisher, category, tag, or collection subscription, then subscribe to that individual post for later updates.

## Related Files

- `DysonNetwork.Shared/Models/Post.cs`
- `DysonNetwork.Sphere/AppDatabase.cs`
- `DysonNetwork.Sphere/Post/PostSubscriptionController.cs`
- `DysonNetwork.Sphere/Post/PostService.cs`
- `DysonNetwork.Sphere/Post/PostActionController.cs`
- `DysonNetwork.Sphere/Publisher/PublisherSubscriptionService.cs`
- `DysonNetwork.Sphere/Migrations/20260521161833_AddPostCollectionSubscription.cs`
- `DysonNetwork.Sphere/Migrations/20260521162749_AddPostSubscriptions.cs`
