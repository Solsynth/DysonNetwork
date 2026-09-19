# Post Thread API

This document covers the endpoints that return the context around a post:
`/thread` for the conversation (ancestors going up the reply chain, descendants coming down) and `/chain` for the post chain the post is part of.

## Get Thread

Returns the full thread context for a given post. Walks up the reply chain to find ancestors, and down to find all descendant replies.

```http
GET /api/posts/{id}/thread?ancestors=true&ancestorLimit=50&take=20
```

### Query Parameters

| Param | Default | Description |
|-------|---------|-------------|
| `ancestors` | `true` | Whether to include the ancestor chain. Set to `false` when paginating descendants (ancestors already loaded). |
| `ancestorLimit` | `50` | Max depth to walk up the reply chain via recursive CTE. |
| `take` | `20` | Max descendants to return. |

### Response Shape

```json
{
  "ancestors": [
    {
      "post": { "id": "root-post", "replied_post_id": null },
      "depth": 0,
      "parent_id": null
    },
    {
      "post": { "id": "mid-post", "replied_post_id": "root-post" },
      "depth": 1,
      "parent_id": "root-post"
    }
  ],
  "current": {
    "post": { "id": "target-post", "replied_post_id": "mid-post" },
    "depth": 2,
    "parent_id": "mid-post"
  },
  "descendants": [
    {
      "post": { "id": "reply-a", "replied_post_id": "target-post" },
      "depth": 0,
      "parent_id": "target-post"
    },
    {
      "post": { "id": "reply-b", "replied_post_id": "reply-a" },
      "depth": 1,
      "parent_id": "reply-a"
    }
  ],
  "has_more": true
}
```

### Fields

- `ancestors`: ordered root-first (depth 0 = thread root). `null` when `ancestors=false`.
- `current`: the requested post, with `depth` relative to the ancestor chain.
- `descendants`: flattened tree in depth-first order. `depth` is relative to the current post (0 = direct reply).
- `has_more`: `true` if there are more descendants beyond `take`. Use a child post as a new anchor to fetch the next page.

### Ancestor Chain

Ancestors are fetched in a single recursive CTE query, regardless of chain depth. The query walks up `replied_post_id` from the target post to the root (or `ancestorLimit`). Visibility filtering is applied after fetching.

### Descendant Tree

Descendants are fetched via BFS (same algorithm as `/replies/threaded`). Each BFS level is a batch query. The result is flattened into a depth-first list, capped at `take`.

## Client Pagination Flow

The thread API is designed for incremental loading:

1. **First load** — user opens a post midway through a thread:

```http
GET /api/posts/{id}/thread?ancestors=true&take=20
```

This returns the full ancestor chain + current post + first 20 descendants.

2. **Load more descendants** — user scrolls down and `has_more` is `true`. Pick the last child post as a new anchor:

```http
GET /api/posts/{lastChildId}/thread?ancestors=false&take=20
```

Skipping ancestors avoids re-fetching data already on the client.

3. **Repeat** until `has_more` is `false`.

## Notes

- Visibility filtering applies to all posts (ancestors, current, descendants). Posts the user cannot see are omitted.
- Gatekept publisher checks apply — subscriber-only posts require an active subscription.
- View count is incremented for the current post on each call.
- `depth` and `parent_id` allow the client to reconstruct the tree structure without additional logic.

## Get Chain

Returns the chain a post belongs to, head first, in publication order. Chaining
is flat (every chained post stores the chain head in `chained_post_id`), so a
chained post read on its own only carries its head id — this endpoint resolves
the rest of the chain around it.

```http
GET /api/posts/{id}/chain
```

The anchor may be any member of the chain, including the head itself.

### Response Shape

```json
[
  { "id": "chain-head", "chained_post_id": null },
  { "id": "chain-member-1", "chained_post_id": "chain-head" },
  { "id": "chain-member-2", "chained_post_id": "chain-head" }
]
```

### Notes

- Members are the chain head plus its `chained_posts`, so visibility, ordering
  (`published_at` ascending) and gating match the head's own detail read.
- Members carry empty `chained_posts` / `chained_count`; the list itself is the
  chain.
- View counts are not affected — the anchor's detail read already counts one.
- `404` when the anchor or the chain head is not visible to the caller.
