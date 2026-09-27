# Post Subscription Notifications

This document describes the post-related notification features in `DysonNetwork.Sphere`:

- collection subscriptions that receive new post notifications
- post watches, which follow a post a user already interacted with

All API responses use `snake_case`.

## Overview

There are two different notification paths around posts:

1. `SnPostCategorySubscription` can target a category, tag, or collection.
2. Post watches follow one specific post, without a subscription record.

They serve different purposes:

- category/tag/collection subscriptions notify when a newly published post matches the subscribed target
- post watches notify when an already-known post later receives reactions, replies, chained posts, boosts, or meaningful edits, and exist for any account that bookmarked, reacted to, or replied to that post

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

## Post Watches

Watching a post is implicit — there is no subscribe endpoint and no per-post subscription
record. Bookmarking, reacting to, or replying to a post makes the acting account a watcher of
that post, and the watched set is derived from rows that already exist:

| watch source | derived from |
|---|---|
| `bookmark` | `post_bookmarks` |
| `reaction` | `post_reactions` |
| `reply` | posts whose `replied_post_id` points at the post |

Un-bookmarking, removing the reaction, or deleting the reply removes that source, and an
account with no remaining source stops receiving the post's update notifications. The watch
source rows are the subscription state, so no extra bookkeeping can drift out of sync.

### Data Model

Only the per-source filters are stored, one row per account and source, in
`post_watch_preferences`.

```csharp
public enum PostWatchSource
{
    Bookmark,
    Reaction,
    Reply,
}

public enum PostWatchEvent
{
    Reactions,
    Replies,
    Chains,
    Forwards,
    Edits,
}

public class SnPostWatchPreference : ModelBase
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public PostWatchSource Source { get; set; }
    public bool NotifyReactions { get; set; }
    public bool NotifyReplies { get; set; }
    public bool NotifyChains { get; set; }
    public bool NotifyForwards { get; set; }
    public bool NotifyEdits { get; set; }
}
```

A row exists only after the user changes a source's filters. When no row exists, the defaults
apply:

| source | reactions | replies | chains | forwards | edits |
|---|---|---|---|---|---|
| `bookmark` | on | on | on | on | on |
| `reaction` | off | off | on | off | on |
| `reply` | off | off | on | off | on |

Reacting to or replying to a post therefore subscribes only to new chained posts and edits of
that post by default, while bookmarking subscribes to everything.

### Rules

1. A watch always has at least one source; the acting account never receives its own
   notification.
2. At most one filter row per account and source (unique index on
   `account_id`, `source`, `deleted_at`).
3. Filters are per account and per source, not per post: one `reaction` setting covers every
   post the account reacted to.
4. Updating a subset of the flags leaves the other flags untouched.
5. Accounts the post publisher blocked or muted are skipped.
6. A chain is flat: a new chained post notifies watchers of the chain head and of every member
   of that chain, de-duplicated.
7. Watch notifications go to accounts, not publisher members: a reply to a post notifies that
   post's watchers, while the post's publisher members keep receiving the existing
   `post.replies` notification.

## Watch Preference API

Base URL:

```http
/api/posts/watch
```

### Get Preferences

```http
GET /api/posts/watch
```

Requires authentication. Always returns all three sources with the effective values, defaults
included.

### Update Preferences

```http
PUT /api/posts/watch
```

Requires authentication and the `post.subscriptions.manage` permission. Only the sources
present in the body are written; omitted flags keep their current value (or the default when
the source has no row yet).

Request body:

```json
{
  "bookmark": { "reactions": false, "chains": false },
  "reaction": { "replies": true },
  "reply": {}
}
```

Response shape:

```json
[
  {
    "source": "bookmark",
    "notify_reactions": false,
    "notify_replies": true,
    "notify_chains": false,
    "notify_forwards": true,
    "notify_edits": true
  },
  {
    "source": "reaction",
    "notify_reactions": false,
    "notify_replies": true,
    "notify_chains": true,
    "notify_forwards": false,
    "notify_edits": true
  },
  {
    "source": "reply",
    "notify_reactions": false,
    "notify_replies": false,
    "notify_chains": true,
    "notify_forwards": false,
    "notify_edits": true
  }
]
```

## Notification Triggers

Watch notifications support five event types. Every one of them carries
`notification_type = "post_watch"` and `watch_event_type`, and is delivered to the accounts
whose watch source allows that event.

### Reaction Notifications

Topic:

```text
posts.watch.reactions
```

Triggered when a reaction is added to a watched post. The acting account is excluded. Meta also
includes `reaction`, `reaction_attitude`, `actor_id`, `actor_name`.

### Reply Notifications

Topic:

```text
posts.watch.replies
```

Triggered when a reply is published to a watched post. Meta also includes
`actor_publisher_id`, `actor_publisher_name`, `actor_publisher_nick`.

### Chain Notifications

Topic:

```text
posts.watch.chains
```

Triggered when a post is published with a `chained_post_id`, whether set explicitly or assigned
by the automatic chaining window. The notification opens the newly chained post. Meta also
includes `chain_head_id` and the acting publisher fields.

### Forward Notifications

Topic:

```text
posts.watch.forwards
```

Triggered when a local Sphere boost is created for a watched post. Remote ActivityPub boosts and
boost removal do not trigger it.

### Edit Notifications

Topic:

```text
posts.watch.edits
```

Triggered when a post was already published before the update, remains published after it, and
the update changes meaningful content: `title`, `description`, `content`, `visibility`,
`drafted_at`, `published_at`, or attachment membership. The first publication of a post is not
an edit, and draft-only posts never trigger it.

## Notes

- Post watches replace the former explicit `post_subscriptions` table and its
  `/api/posts/{id}/subscribe`, `/api/posts/{id}/unsubscribe`, `/api/posts/{id}/subscription`
  and `/api/posts/subscriptions` endpoints; `post_subscriptions` is dropped by the
  `ReplacePostSubscriptionsWithWatchPreferences` migration.
- The former `posts.subscriptions.*` topics no longer fire. Notification preferences are stored
  per topic, so any leftover preference rows for those topics are simply never consulted.
- Collection subscriptions and post watches are independent and can both apply to the same user.

## Related Files

- `DysonNetwork.Sphere/Models/PostWatch.cs`
- `DysonNetwork.Sphere/Post/PostWatchService.cs`
- `DysonNetwork.Sphere/Post/PostWatchController.cs`
- `DysonNetwork.Sphere/Post/PostService.cs`
- `DysonNetwork.Sphere/Post/PostActionController.cs`
- `DysonNetwork.Sphere/AppDatabase.cs`
- `DysonNetwork.Sphere/Migrations/20260521161833_AddPostCollectionSubscription.cs`
- `DysonNetwork.Sphere/Publisher/PublisherSubscriptionService.cs`
