# Post API Reference

This document covers the reply-related post APIs and the reply count fields exposed on `SnPost`.

## Reply Count Fields

Posts now expose two reply count fields with different meanings:

- `replies_count`: direct replies only
- `thread_replies_count`: all descendant replies in the thread, including direct replies

Use `replies_count` when loading the next page of comments from the direct replies endpoint.
Use `thread_replies_count` for feed or timeline UI that wants the full discussion size.

## List Direct Replies

Returns only the direct children of the target post.

```http
GET /api/posts/{id}/replies?offset=0&take=20
```

### Query Parameters

- `offset`: pagination offset for direct replies
- `take`: page size for direct replies

### Response Headers

- `X-Total`: total number of direct replies to the target post

### Response Shape

```json
[
  {
    "id": "reply-id",
    "replied_post_id": "parent-post-id",
    "replies_count": 2,
    "thread_replies_count": 2
  }
]
```

Notes:

- `replies_count` on each returned reply is still that reply's direct child count
- `thread_replies_count` on each returned reply is the full descendant count below that reply
- this endpoint does not nest children in the response body

## List Threaded Replies

Returns a flattened list of all visible replies (direct and descendants), ordered by hierarchy. The response is paginated at the top level only.

```http
GET /api/posts/{id}/replies/threaded?offset=0&take=20
```

### Query Parameters

- `offset`: pagination offset for top-level direct replies
- `take`: page size for top-level direct replies

### Response Headers

- `X-Total`: total number of direct replies to the target post

### Response Shape

Each item is a `ThreadedReplyNode`:

```json
[
  {
    "post": {
      "id": "reply-a",
      "replied_post_id": "parent-post-id",
      "replies_count": 1,
      "thread_replies_count": 3
    },
    "depth": 0,
    "parentId": null
  },
  {
    "post": {
      "id": "reply-b",
      "replied_post_id": "reply-a",
      "replies_count": 1,
      "thread_replies_count": 2
    },
    "depth": 1,
    "parentId": "reply-a"
  },
  {
    "post": {
      "id": "reply-c",
      "replied_post_id": "reply-b",
      "replies_count": 0,
      "thread_replies_count": 0
    },
    "depth": 2,
    "parentId": "reply-b"
  }
]
```

### Fields

- `post`: the full post object
- `depth`: nesting level relative to the root replies (0 = direct reply to the target post)
- `parentId`: the `id` of the parent post this reply responds to

### Notes

- pagination applies only to the top-level direct replies of the target post
- descendants are returned in depth-first order
- `depth` and `parentId` allow the client to reconstruct the tree structure on the frontend
- visibility filtering still applies, so hidden replies are omitted

## Client Guidance

- Use `/api/posts/{id}/replies` when implementing classic paged comments UI
- Use `/api/posts/{id}/replies/threaded` when rendering a nested thread view
- Do not replace `replies_count` with `thread_replies_count` in comment pagination logic
