# QuietPublic Post Visibility

Renames `SubscriberOnly` to `QuietPublic` and adjusts behavior so posts are hidden from general timelines but remain accessible on publisher pages and via direct links.

## Overview

`QuietPublic` is a post visibility level that behaves like `Public` but with reduced discoverability:

| Surface | Behavior |
|---------|----------|
| General timeline (unauthenticated) | Hidden |
| General timeline (authenticated) | Hidden for non-subscribers |
| Publisher page (`?pub=<name>`) | Visible to everyone |
| Direct post access (by ID or slug) | Visible to everyone |
| Replies/forwards listings | Visible (inherits parent context) |

This allows publishers to share content that doesn't clutter followers' feeds while still being accessible to anyone who visits the publisher's page or has a direct link.

## Enum Change

### Before
```csharp
public enum PostVisibility
{
    Public,
    Friends,
    Unlisted,
    Private,
    CloseFriendsOnly,
    SubscriberOnly,
}
```

### After
```csharp
public enum PostVisibility
{
    Public,
    Friends,
    Unlisted,
    Private,
    CloseFriendsOnly,
    QuietPublic,
}
```

The numeric value remains `5` (C#) / `6` (proto), so existing data is compatible without migration.

## FilterWithVisibility Changes

Added `showQuietPublic` parameter to `PostQueryExtensions.FilterWithVisibility()`:

```csharp
public static IQueryable<SnPost> FilterWithVisibility(
    this IQueryable<SnPost> source,
    DyAccount? currentUser,
    List<Guid> userFriends,
    List<SnPublisher> publishers,
    bool isListing = false,
    HashSet<Guid>? gatekeptPublisherIds = null,
    HashSet<Guid>? followerPublisherIds = null,
    HashSet<Guid>? blockedAccountIds = null,
    HashSet<Guid>? mutedAccountIds = null,
    HashSet<Guid>? closeFriendPublisherIds = null,
    bool showQuietPublic = false  // NEW
)
```

When `showQuietPublic` is `true`, `QuietPublic` posts bypass the subscriber check and are included in results.

### Visibility Logic

```csharp
.Where(e =>
    e.Visibility != PostVisibility.QuietPublic
    || showQuietPublic
    || publishersId.Contains(e.PublisherId!.Value)
    || (
        followerPublisherIds != null
        && e.PublisherId.HasValue
        && followerPublisherIds.Contains(e.PublisherId.Value)
    )
)
```

## Endpoint Behavior

### ListPosts (`GET /api/posts`)

When `pub=<name>` query parameter is set, `showQuietPublic: true` is passed to `FilterWithVisibility`:

```csharp
query = query.FilterWithVisibility(
    currentUser,
    userFriends,
    userPublishers,
    isListing: true,
    gatekeptPublisherIds,
    subscriberPublisherIds,
    blockedAccountIds,
    mutedAccountIds.ToHashSet(),
    closeFriendPublisherIds,
    showQuietPublic: pubName is not null
);
```

### Timeline (`GET /api/timeline`)

Timeline endpoints do not pass `showQuietPublic`, so `QuietPublic` posts are excluded from feeds for non-subscribers. Subscribers (via `followerPublisherIds`) can still see them.

### Direct Access (`GET /api/posts/{id}`)

Detail views call `FilterWithVisibility` without `isListing: true`, so `QuietPublic` posts are always accessible by ID or slug.

## Subscriber Access Checks

Post detail endpoints (`GetPost`, `GetPrevPost`, `GetNextPost`) retain subscriber access checks for `QuietPublic`:

```csharp
if (post.PublisherId.HasValue && (
    post.Publisher?.GatekeptFollows == true ||
    post.Visibility == PostVisibility.QuietPublic
))
{
    // Require subscriber access unless user is publisher member
}
```

This ensures that while `QuietPublic` posts are accessible via publisher page listings, direct access still respects subscription requirements when the publisher has gatekeeping enabled.

## Files Changed

### Shared
- `Models/Post.cs` — `SubscriberOnly` renamed to `QuietPublic` in `PostVisibility` enum
- `Proto/Post.cs` — `DyPostSubscriberOnly` renamed to `DyPostQuietPublic`

### Sphere
- `Post/PostService.cs`:
  - `FilterWithVisibility` — Added `showQuietPublic` parameter
  - `FilterUsersByPostVisibility` — Updated `SubscriberOnly` case to `QuietPublic`
  - Notification filtering — Updated visibility check
- `Post/PostController.cs`:
  - `ListPosts` — Passes `showQuietPublic: pubName is not null`
  - `GetPost` (by slug and by ID) — Updated visibility check
  - `GetPrevPost` / `GetNextPost` — Updated visibility check
- `Publisher/PublisherSubscriptionService.cs` — Updated `NotifySubscriberPost` visibility check
