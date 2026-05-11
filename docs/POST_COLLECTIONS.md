# Post Collections

Manual post collections let a publisher curate an ordered list of posts and expose stable previous/next navigation within that list.

Collections are publisher-scoped resources.
Posts can appear in collections owned by other publishers, but when a post is returned from the post APIs, only collections owned by the same publisher as the post are exposed in `publisher_collections`.

> **Note:** All API responses use snake_case field names.

## Data Model

### SnPostCollection

| Field | Type | Description |
|-------|------|-------------|
| `id` | UUID | Collection ID |
| `slug` | string(128) | Normalized collection identifier, unique per publisher |
| `name` | string(256)? | Display name |
| `description` | string(4096)? | Collection description |
| `publisher_id` | UUID | Owning publisher |
| `publisher` | object | Owning publisher object |
| `background` | object? | Background image (SnCloudFileReferenceObject) |
| `icon` | object? | Icon image (SnCloudFileReferenceObject) |
| `created_at` | Instant | Creation timestamp |
| `updated_at` | Instant | Last update timestamp |

### SnPostCollectionItem

Collection membership is stored as ordered items.

| Field | Type | Description |
|-------|------|-------------|
| `id` | UUID | Collection item ID |
| `collection_id` | UUID | Parent collection |
| `post_id` | UUID | Included post |
| `order` | int | Zero-based manual order |
| `created_at` | Instant | Creation timestamp |
| `updated_at` | Instant | Last update timestamp |

## Rules

1. Collections are manual only.
2. Collection slugs are normalized to lowercase and trimmed.
3. Slugs are unique per publisher.
4. Collection management requires publisher `Editor` role or higher.
5. Listing and navigation endpoints still respect post visibility and gatekept publisher rules.
6. Post payloads only expose `publisher_collections` where `collection.publisher_id == post.publisher_id`.

## Base URL

```
/api/publishers/{publisherName}/collections
```

## Collection Endpoints

### List Collections

```http
GET /api/publishers/{publisherName}/collections
```

Returns all collections owned by the publisher.

**Response:** `200 OK`

```json
[
  {
    "id": "8f7b3f8e-6758-4f10-a4d4-cbe8ce7c5278",
    "slug": "featured-articles",
    "name": "Featured Articles",
    "description": "Long-form editorial posts",
    "publisher_id": "f37d7331-7305-4f2d-8e85-d5dfb04f2bd9",
    "publisher": {
      "id": "f37d7331-7305-4f2d-8e85-d5dfb04f2bd9",
      "name": "solsynth"
    },
    "background": null,
    "icon": {
      "id": "file-uuid",
      "name": "icon.png",
      "url": "https://cdn.example.com/files/icon.png"
    },
    "created_at": "2026-05-10T12:00:00Z",
    "updated_at": "2026-05-10T12:00:00Z"
  }
]
```

### Create Collection

```http
POST /api/publishers/{publisherName}/collections
```

**Request Body:**

```json
{
  "slug": "featured-articles",
  "name": "Featured Articles",
  "description": "Long-form editorial posts"
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `slug` | Yes | Collection slug, max 128 chars |
| `name` | No | Display name, max 256 chars |
| `description` | No | Description, max 4096 chars |
| `background_id` | No | Cloud file ID for the background image |
| `icon_id` | No | Cloud file ID for the icon image |

**Response:** `201 Created`

**Error Responses:**

| Status | Condition |
|--------|-----------|
| `400 Bad Request` | Collection slug already exists for the publisher |
| `401 Unauthorized` | Not authenticated |
| `403 Forbidden` | Not an editor of the publisher |
| `404 Not Found` | Publisher not found |

### Get Collection

```http
GET /api/publishers/{publisherName}/collections/{slug}
```

Returns the collection metadata.

**Response:** `200 OK`

### Update Collection

```http
PATCH /api/publishers/{publisherName}/collections/{slug}
```

**Request Body:**

```json
{
  "name": "Updated Name",
  "description": "Updated description"
}
```

All fields are optional. Note that `name`, `description`, `background_id`, and `icon_id` are replaced with whatever is sent (including `null` to clear them).

**Request Body:**

```json
{
  "name": "Updated Name",
  "description": "Updated description",
  "background_id": null,
  "icon_id": "new-icon-file-id"
}
```

**Response:** `200 OK`

### Delete Collection

```http
DELETE /api/publishers/{publisherName}/collections/{slug}
```

Deletes the collection and all collection items.

**Response:** `204 No Content`

## Collection Post Membership

### List Collection Posts

```http
GET /api/publishers/{publisherName}/collections/{slug}/posts?offset=0&take=20
```

Returns visible posts in the collection, ordered by manual `order` ascending.

### Query Parameters

| Param | Description |
|-------|-------------|
| `offset` | Pagination offset |
| `take` | Page size |

### Response Headers

| Header | Description |
|--------|-------------|
| `X-Total` | Total number of visible posts in the collection |

**Response:** `200 OK`

### Add Post To Collection

```http
POST /api/publishers/{publisherName}/collections/{slug}/posts
```

**Request Body:**

```json
{
  "post_id": "f68871e8-1608-43dc-9ccf-3ef0cc63f3a0",
  "order": 2
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `post_id` | Yes | Post to include |
| `order` | No | Target zero-based index. If omitted, appends to the end |

When `order` is provided, existing items at or after that position are shifted down.

**Response:** `204 No Content`

**Error Responses:**

| Status | Condition |
|--------|-----------|
| `400 Bad Request` | Post not found |
| `400 Bad Request` | Post is already in the collection |
| `401 Unauthorized` | Not authenticated |
| `403 Forbidden` | Not an editor of the publisher |

### Batch Add Posts To Collection

```http
POST /api/publishers/{publisherName}/collections/{slug}/posts/batch
```

**Request Body:**

```json
{
  "post_ids": [
    "f68871e8-1608-43dc-9ccf-3ef0cc63f3a0",
    "a7b93d22-43dc-1608-9ccf-3ef0cc63f3a1"
  ]
}
```

Silently skips posts already present in the collection. Validates that all remaining posts exist (returns 400 with missing IDs if not). New posts are appended sequentially after the current max order.

**Response:** `204 No Content`

**Error Responses:**

| Status | Condition |
|--------|-----------|
| `400 Bad Request` | One or more posts not found (IDs listed in message) |
| `401 Unauthorized` | Not authenticated |
| `403 Forbidden` | Not an editor of the publisher |
| `404 Not Found` | Collection not found |

### Batch Remove Posts From Collection

```http
POST /api/publishers/{publisherName}/collections/{slug}/posts/batch/remove
```

**Request Body:**

```json
{
  "post_ids": [
    "f68871e8-1608-43dc-9ccf-3ef0cc63f3a0",
    "a7b93d22-43dc-1608-9ccf-3ef0cc63f3a1"
  ]
}
```

Removes all matching posts in a single operation and compacts the remaining items' order.

**Response:** `204 No Content`

**Error Responses:**

| Status | Condition |
|--------|-----------|
| `401 Unauthorized` | Not authenticated |
| `403 Forbidden` | Not an editor of the publisher |
| `404 Not Found` | Collection not found |

### Remove Post From Collection

```http
DELETE /api/publishers/{publisherName}/collections/{slug}/posts/{postId}
```

Removes a post from the collection. Remaining items after the removed entry are compacted.

**Response:** `204 No Content`

### Reorder Collection Posts

```http
PUT /api/publishers/{publisherName}/collections/{slug}/posts/reorder
```

**Request Body:**

```json
{
  "post_ids": [
    "post-a",
    "post-b",
    "post-c"
  ]
}
```

The request must include every post currently in the collection exactly once.

**Response:** `204 No Content`

**Error Responses:**

| Status | Condition |
|--------|-----------|
| `400 Bad Request` | Payload does not contain exactly the full current membership |

## Collection Navigation

These endpoints browse within the collection's manual order, not by publish time.

### Previous Post In Collection

```http
GET /api/publishers/{publisherName}/collections/{slug}/posts/{postId}/prev
```

Returns the previous visible post in collection order.

### Next Post In Collection

```http
GET /api/publishers/{publisherName}/collections/{slug}/posts/{postId}/next
```

Returns the next visible post in collection order.

**Response:** `200 OK`

**Error Responses:**

| Status | Condition |
|--------|-----------|
| `404 Not Found` | Collection not found |
| `404 Not Found` | Current post is not visible in the collection |
| `404 Not Found` | No previous or next visible post exists |

## Post Response Integration

Post responses now expose `publisher_collections`.

Example shape:

```json
{
  "id": "f68871e8-1608-43dc-9ccf-3ef0cc63f3a0",
  "publisher_id": "f37d7331-7305-4f2d-8e85-d5dfb04f2bd9",
  "publisher_collections": [
    {
      "id": "8f7b3f8e-6758-4f10-a4d4-cbe8ce7c5278",
      "slug": "featured-articles",
      "name": "Featured Articles",
      "description": "Long-form editorial posts",
      "publisher_id": "f37d7331-7305-4f2d-8e85-d5dfb04f2bd9"
    }
  ]
}
```

This field is populated on the major post read endpoints, including:

1. `GET /api/posts`
2. `GET /api/posts/{publisherName}/{slug}`
3. `GET /api/posts/{id}`
4. `GET /api/posts/{id}/prev`
5. `GET /api/posts/{id}/next`
6. reply and forward listing endpoints

### Creating Posts With Collections

When creating a post via `POST /api/posts`, you can optionally include a `collection_ids` array in the request body to add the post to one or more collections at creation time. The collections must belong to the same publisher as the post. Duplicate assignments are silently skipped.

```json
{
  "content": "Hello world",
  "collection_ids": ["8f7b3f8e-6758-4f10-a4d4-cbe8ce7c5278"]
}
```

## Migration Notes

The schema now uses `post_collection_items` instead of the old implicit `post_collection_links` table.

Additional migrations:
- `AddPostCollectionBackgroundIcon` adds `background` (jsonb) and `icon` (jsonb) columns to the `post_collections` table.

During migration:

1. existing memberships are copied into `post_collection_items`
2. imported order is assigned by `posts_id` within each collection
3. the old `post_collection_links` table is removed
