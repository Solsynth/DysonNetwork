# Post Interactions API Reference

This document covers APIs for listing post interactions: forwards (quotes), boosts, and reactions.

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

## Related Files

- `DysonNetwork.Sphere/Post/PostController.cs` - Forwards and Reactions endpoints
- `DysonNetwork.Sphere/Post/PostActionController.cs` - Boosts endpoint
- `DysonNetwork.Shared/Models/Post.cs` - `SnPostReaction` model
- `DysonNetwork.Shared/Models/Boost.cs` - `SnBoost` model
