# Post Interactions API Reference

This document covers APIs for post interactions: forwards (quotes), boosts, reactions, public user reaction history, and private bookmarks.

## List Forwards

Returns posts that forward (quote) the target post.

```http
GET /api/posts/{id}/forwards?offset=0&take=20
```

### Query Parameters

- `offset`: pagination offset
- `take`: page size (default: 20)

### Response Headers

- `X-Total`: total number of forwards

### Response Shape

```json
[
  {
    "id": "forward-post-id",
    "forwarded_post_id": "original-post-id",
    "title": "Forward Post Title",
    "content": "Optional quote text",
    "published_at": "2026-03-27T10:00:00Z",
    "created_at": "2026-03-27T09:00:00Z",
    "categories": [...],
    "tags": [...]
  }
]
```

### Notes

- Only posts where `ForwardedPostId == {id}` are returned
- Visibility filtering applies (users see only posts they have access to)
- Ordered by `PublishedAt ?? CreatedAt` descending (newest first)
- Each forward includes the original post via `ForwardedPost` navigation

---

## List Boosts

Returns boosts (reblogs) of the target post.

```http
GET /api/posts/{id}/boosts?offset=0&take=20
```

### Query Parameters

- `offset`: pagination offset
- `take`: page size (default: 20)

### Response Headers

- `X-Total`: total number of boosts

### Response Shape

```json
[
  {
    "id": "boost-id",
    "post_id": "original-post-id",
    "actor_id": "fediverse-actor-id",
    "actor": {
      "id": "actor-id",
      "username": "username",
      "display_name": "Display Name",
      "avatar_url": "https://..."
    },
    "boosted_at": "2026-03-27T10:00:00Z",
    "activity_pub_uri": "https://fediverse.example/actors/id",
    "web_url": "https://fediverse.example/users/name/statuses/123"
  }
]
```

### Notes

- Ordered by `BoostedAt` descending (newest first)
- Includes the actor who performed the boost

---

## List Reactions

Returns reactions (emoji responses) on the target post.

```http
GET /api/posts/{id}/reactions?offset=0&take=20&symbol=👍&order=created
```

### Query Parameters

- `offset`: pagination offset
- `take`: page size (default: 20)
- `symbol`: filter by emoji symbol (optional)
- `order`: sort order (optional)
  - `symbol` (default): order by symbol, then by creation date desc
  - `created`: order by creation date descending

### Response Headers

- `X-Total`: total number of reactions (filtered by symbol if provided)

### Response Shape

```json
[
  {
    "id": "reaction-id",
    "symbol": "👍",
    "attitude": "Positive",
    "post_id": "post-id",
    "account_id": "account-id",
    "account": {
      "id": "account-id",
      "username": "username",
      "display_name": "Display Name"
    },
    "actor_id": "actor-id",
    "actor": {
      "id": "actor-id",
      "username": "fediverse_username",
      "display_name": "Fediverse Name"
    },
    "fediverse_uri": "https://mastodon.social/users/name",
    "is_local": true,
    "created_at": "2026-03-27T10:00:00Z"
  }
]
```

### Notes

- Default ordering groups reactions by symbol
- Use `order=created` to get chronological feed of reactions
- Actor information is included for Fediverse reactions
- Account information is included for local reactions

---

## List User Reactions

Returns the visible local-post reactions made by a specific local user, resolved by username.

```http
GET /api/posts/reactions/users/{name}?offset=0&take=20&order=created
```

### Query Parameters

- `offset`: pagination offset
- `take`: page size (default: 20)
- `order`: sort order (optional)
  - `created` (default): newest reactions first

### Response Headers

- `X-Total`: total number of visible reactions for the user

### Response Shape

```json
[
  {
    "reaction": {
      "id": "reaction-id",
      "symbol": "heart",
      "attitude": "Positive",
      "post_id": "post-id",
      "account_id": "account-id",
      "account": {
        "id": "account-id",
        "name": "littlesheep"
      },
      "is_local": true,
      "created_at": "2026-05-10T12:00:00Z"
    },
    "post": {
      "id": "post-id",
      "title": "Post title",
      "content": "Post content",
      "publisher": {
        "id": "publisher-id",
        "name": "publisher-name"
      },
      "reactions_count": {
        "heart": 3
      }
    }
  }
]
```

### Notes

- The `{name}` route value is a local username, not publisher name and not account ID
- This endpoint only returns reactions from local accounts
- This endpoint only returns reactions on local posts
- Post visibility filtering still applies for the current viewer
- Hidden, private, draft, or gatekept-inaccessible posts are omitted from the result
- Each item includes both the reaction and the resolved post payload for rewind-style clients

---

## Bookmarks

Bookmarks are private saved-post records for the authenticated user.

### Save Bookmark

```http
POST /api/posts/{id}/bookmark
```

### Behavior

- Requires authentication
- Saves the target post for the current user
- Works for both local and remote posts, as long as the post is currently visible to the user
- If the bookmark already exists, the existing bookmark record is returned

### Response Shape

```json
{
  "id": "bookmark-id",
  "post_id": "post-id",
  "account_id": "account-id",
  "created_at": "2026-05-10T12:00:00Z",
  "updated_at": "2026-05-10T12:00:00Z"
}
```

### Remove Bookmark

```http
DELETE /api/posts/{id}/bookmark
```

### Behavior

- Requires authentication
- Removes the bookmark owned by the current user for the target post
- Returns `404` if no bookmark exists for that user and post

### Response

- `204 No Content`

### List Bookmarks

Returns the current user's saved posts as normal enriched `SnPost` payloads.

```http
GET /api/posts/bookmarks?offset=0&take=20&order=created
```

### Query Parameters

- `offset`: pagination offset
- `take`: page size (default: 20)
- `order`: sort order (optional)
  - `created` (default): newest bookmarks first

### Response Headers

- `X-Total`: total number of visible bookmarks for the current user

### Response Shape

```json
[
  {
    "id": "post-id",
    "title": "Saved post",
    "content": "Saved content",
    "fediverse_uri": "https://remote.example/@user/123",
    "is_bookmarked": true,
    "publisher": {
      "id": "publisher-id",
      "name": "publisher-name"
    }
  }
]
```

### Notes

- Bookmarks are private and only available to the current authenticated user
- Listing returns post payloads, not bookmark wrapper objects
- `is_bookmarked` is populated on returned posts and on other normal post payloads for the authenticated user
- Visibility filtering still applies when listing bookmarks
- A bookmarked post may disappear from the listing if it later becomes inaccessible to the viewer

---

## Related Files

- `DysonNetwork.Sphere/Post/PostController.cs` - Forwards and Reactions endpoints
- `DysonNetwork.Sphere/Post/PostActionController.cs` - Boosts and bookmark mutation endpoints
- `DysonNetwork.Shared/Models/Post.cs` - `SnPostReaction` model
- `DysonNetwork.Shared/Models/Post.cs` - `SnPostBookmark` model
- `DysonNetwork.Shared/Models/Boost.cs` - `SnBoost` model
