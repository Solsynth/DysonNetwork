# Post Thread API

This document covers the thread endpoint that returns the full conversation context around a post — ancestors (reply chain going up) and descendants (replies going down).

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
