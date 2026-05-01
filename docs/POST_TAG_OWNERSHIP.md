# Post Tag Ownership

Adds ownership, protection, and event lifecycle to post tags. Tags can be owned by publishers, protected to restrict usage, and marked as time-limited event tags.

> **Note:** All API responses use snake_case field names (e.g., `owner_publisher_id` instead of `ownerPublisherId`).

## Base URLs

```
/api/posts/tags        — Publisher endpoints
/api/admin/posts/tags  — Admin endpoints
```

---

## Data Model

### SnPostTag

| Field | Type | Description |
|-------|------|-------------|
| `id` | UUID | Primary key |
| `slug` | string(128) | Unique normalized identifier (lowercase, trimmed) |
| `name` | string(256)? | Display name |
| `description` | string(4096)? | Tag description/metadata |
| `owner_publisher_id` | UUID? | Publisher that owns this tag |
| `is_protected` | bool | If true, only the owner can use this tag on new posts |
| `is_event` | bool | If true, this tag is a time-limited event tag |
| `event_ends_at` | Instant? | When the event tag expires (new usages rejected after this) |
| `created_at` | Instant | Creation timestamp |
| `updated_at` | Instant | Last update timestamp |

### Ownership Rules

1. **Unclaimed tags:** Tags with no owner are available to everyone
2. **First claim:** A publisher can manually claim an unowned tag via `POST /{slug}/claim`
3. **Admin assignment:** Admins can assign ownership to any publisher via `POST /{slug}/assign`
4. **Protected tags:** When a tag is protected, only the owning publisher can use it on new posts
5. **Event tags:** When an event tag expires, existing posts keep the tag but new posts cannot use it

### Protected Tag Quota

Each publisher can protect a limited number of tags based on their level:

```
quota = 3 + 3 × perkLevel
```

| Perk Level | Protected Tag Quota |
|------------|-------------------|
| 0 | 3 |
| 1 | 6 |
| 2 | 9 |
| 3 | 12 |

---

## Publisher Endpoints

### Get Tag

```
GET /api/posts/tags/{slug}
```

**Response:** `200 OK`
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "slug": "photography",
  "name": "Photography",
  "description": "Posts about photography techniques and gear",
  "owner_publisher_id": "3fa85f64-5717-4562-b3fc-2c963f66afa7",
  "is_protected": false,
  "is_event": false,
  "event_ends_at": null,
  "created_at": "2026-04-01T00:00:00Z",
  "updated_at": "2026-04-07T00:00:00Z"
}
```

---

### Create Tag

Creates a new tag. The authenticated publisher becomes the owner.

```
POST /api/posts/tags?pub={publisherName}
```

**Request Body:**
```json
{
  "slug": "photography",
  "name": "Photography",
  "description": "Posts about photography techniques and gear"
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `slug` | Yes | Unique tag identifier (max 128 chars) |
| `name` | No | Display name (max 256 chars) |
| `description` | No | Tag description (max 4096 chars) |

**Query Parameters:**

| Param | Description |
|-------|-------------|
| `pub` | Publisher name. If omitted, uses default posting publisher or individual publisher |

**Response:** `201 Created`
```json
{
  "id": "...",
  "slug": "photography",
  "name": "Photography",
  "description": "Posts about photography techniques and gear",
  "owner_publisher_id": "3fa85f64-...",
  "is_protected": false,
  "is_event": false,
  "event_ends_at": null,
  "created_at": "...",
  "updated_at": "..."
}
```

**Error Responses:**

| Status | Condition |
|--------|-----------|
| `400 Bad Request` | Tag with this slug already exists |
| `400 Bad Request` | Cannot resolve publisher |
| `401 Unauthorized` | Not authenticated |

---

### Update Tag

Updates tag metadata. Only the owning publisher's manager/owner can edit.

```
PATCH /api/posts/tags/{slug}?pub={publisherName}
```

**Request Body:**
```json
{
  "name": "Updated Name",
  "description": "Updated description"
}
```

Both fields are optional. Only provided fields are updated.

**Response:** `200 OK`

**Error Responses:**

| Status | Condition |
|--------|-----------|
| `403 Forbidden` | Tag has no owner (admin only) or you are not a manager of the owning publisher |
| `404 Not Found` | Tag not found |

---

### Claim Tag

Manually claim ownership of an unowned tag. The authenticated publisher becomes the owner.

```
POST /api/posts/tags/{slug}/claim?pub={publisherName}
```

**Response:** `200 OK`

**Error Responses:**

| Status | Condition |
|--------|-----------|
| `400 Bad Request` | Tag is already owned by a publisher |
| `400 Bad Request` | Cannot resolve publisher |
| `404 Not Found` | Tag not found |

---

### Get Protected Tag Quota

Check how many protected tags a publisher has used and their remaining quota.

```
GET /api/posts/tags/{slug}/quota?pub={publisherName}
```

**Response:** `200 OK`
```json
{
  "total": 6,
  "used": 2,
  "remaining": 4,
  "level": 15,
  "perk_level": 1,
  "records": [
    {
      "id": "...",
      "slug": "exclusive-content",
      "name": "Exclusive Content"
    },
    {
      "id": "...",
      "slug": "members-only",
      "name": "Members Only"
    }
  ]
}
```

---

## Admin Endpoints

All admin endpoints require either `IsSuperuser` or the `posts.tags.admin` permission.

### Assign Tag Ownership

Assigns a tag to a publisher. Overwrites any existing owner.

```
POST /api/admin/posts/tags/{slug}/assign
```

**Request Body:**
```json
{
  "publisher_id": "3fa85f64-5717-4562-b3fc-2c963f66afa7"
}
```

**Response:** `200 OK`

**Error Responses:**

| Status | Condition |
|--------|-----------|
| `400 Bad Request` | Publisher not found |
| `403 Forbidden` | Admin permission required |
| `404 Not Found` | Tag not found |

---

### Toggle Protected Status

Enable or disable protection on a tag. The tag must have an owner. Only the owner publisher can protect (admin bypasses this by first assigning ownership).

```
PATCH /api/admin/posts/tags/{slug}/protect
```

**Request Body:**
```json
{
  "is_protected": true
}
```

**Response:** `200 OK`

**Error Responses:**

| Status | Condition |
|--------|-----------|
| `400 Bad Request` | Tag has no owner, or owner publisher not found |
| `400 Bad Request` | Protected tag quota exceeded for the owning publisher |
| `403 Forbidden` | Admin permission required |

---

### Set Event Tag

Mark a tag as an event tag with an expiration time. After expiry, new posts cannot use this tag.

```
PATCH /api/admin/posts/tags/{slug}/event
```

**Request Body:**
```json
{
  "is_event": true,
  "ends_at": "2026-06-01T00:00:00Z"
}
```

To remove event status:
```json
{
  "is_event": false,
  "ends_at": null
}
```

**Response:** `200 OK`

**Error Responses:**

| Status | Condition |
|--------|-----------|
| `400 Bad Request` | Event tags must have an end time, or end time is in the past |
| `403 Forbidden` | Admin permission required |

---

### Admin Update Tag

Admin can update any tag's metadata regardless of ownership.

```
PATCH /api/admin/posts/tags/{slug}
```

**Request Body:**
```json
{
  "name": "Updated Name",
  "description": "Updated description"
}
```

**Response:** `200 OK`

---

## Behavior

### Tag Usage Validation During Post Creation/Update

When a post is created or updated with tags, the system validates:

1. **Event tag expiry:** If the tag is an event tag and `event_ends_at` is in the past, the request is rejected with `400 Bad Request`
2. **Protected tag access:** If the tag is protected and the post's publisher is not the tag owner, the request is rejected with `400 Bad Request`

Unclaimed tags (no owner) can be used by anyone.

### Tag Resolution Flow

```
1. Normalize slugs (trim, lowercase)
2. Look up existing tags by slug
3. Create missing tags (unowned)
4. For each tag:
   a. If event tag and expired → reject
   b. If protected and owner differs from post publisher → reject
5. Attach tags to post
```

### Database

#### Table: `post_tags` (new columns)

```sql
ALTER TABLE post_tags ADD COLUMN description VARCHAR(4096);
ALTER TABLE post_tags ADD COLUMN owner_publisher_id UUID REFERENCES publishers(id) ON DELETE SET NULL;
ALTER TABLE post_tags ADD COLUMN is_protected BOOLEAN NOT NULL DEFAULT false;
ALTER TABLE post_tags ADD COLUMN is_event BOOLEAN NOT NULL DEFAULT false;
ALTER TABLE post_tags ADD COLUMN event_ends_at TIMESTAMPTZ;

CREATE INDEX ix_post_tags_owner_publisher_id ON post_tags(owner_publisher_id);
```

#### Migration

```bash
dotnet ef migrations add AddTagOwnership --project DysonNetwork.Sphere --output-dir Migrations
dotnet ef database update --project DysonNetwork.Sphere
```
