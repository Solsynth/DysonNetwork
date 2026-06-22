# Blog Post Type

A blog post links to an external website's blog article. The platform does not store the blog content — the client renders it in an in-app WebView. The post owner retains all interactive features: reactions, replies, forwards, bookmarks, and awards.

Blog posts are gated behind:

1. a dedicated permission node (`posts.create.blog`)
2. a verified domain owned by the publisher (unless the user is a superuser)

## Data Model

### PostType.Blog

Added as the third value in the `PostType` enum:

| Value | Name |
|-------|------|
| 0 | Moment |
| 1 | Article |
| 2 | Blog |

In the gRPC proto: `DY_BLOG = 3`.

### SnPublisherVerifiedDomain

Sphere-local model. Lives in the `publisher_verified_domains` table.

| Field | Type | Description |
|-------|------|-------------|
| `id` | UUID | Domain record ID |
| `publisher_id` | UUID | Owning publisher |
| `domain` | string(512) | Lowercase host (e.g. `blog.example.com`) |
| `status` | int | `0` = Pending, `1` = Verified, `2` = Failed, `3` = Revoked |
| `verified_at` | Instant? | When verification last succeeded |
| `last_checked_at` | Instant? | When the last `.well-known` check ran |
| `failed_attempts` | int | Consecutive failure count |
| `last_error` | string(4096)? | Last failure reason |
| `created_at` | Instant | Creation timestamp |
| `updated_at` | Instant | Last update timestamp |

Unique constraint on `(publisher_id, domain)`.

### DomainVerificationStatus

```csharp
public enum DomainVerificationStatus
{
    Pending,   // 0 — awaiting .well-known check
    Verified,  // 1 — .well-known file confirmed
    Failed,    // 2 — verification attempt failed
    Revoked,   // 3 — manually revoked
}
```

## Domain Verification

When a domain is added or rechecked, the service fetches:

```
https://{domain}/.well-known/dyson-domains.txt
```

The file must contain one publisher name per line. If the publisher's name appears (case-insensitive match), the domain is marked `Verified`. Otherwise it is marked `Failed`.

Example `.well-known/dyson-domains.txt`:

```
solsynth
other-publisher
```

The service uses `IHttpClientFactory` with a 10-second timeout. Failures (network errors, timeouts, non-2xx responses) increment `failed_attempts` and record `last_error`.

## Permission

Blog creation requires the `posts.create.blog` permission node, checked in addition to the base `posts.create` node.

This permission is seeded into the `default` permission group alongside the existing post permissions.

Superusers bypass both the permission check and the domain verification check.

## Creating a Blog Post

```http
POST /api/posts?pub={publisherName}
```

**Request Body:**

```json
{
  "content": "https://blog.example.com/my-article",
  "type": 2,
  "title": "My Blog Post",
  "description": "A short summary"
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `content` | Yes | Must be a valid absolute URL |
| `type` | Yes | Must be `2` (Blog) |
| `title` | No | Post title, max 1024 chars |
| `description` | No | Post description, max 4096 chars |
| `visibility` | No | Defaults to `Public` |
| `tags` | No | Tag slugs, max 16 |
| `categories` | No | Category slugs, max 8 |

**Behavior:**

1. Validates `content` is a valid absolute URL
2. Checks `posts.create.blog` permission (skipped for superusers)
3. Verifies the URL's host is in the publisher's verified domains (skipped for superusers)
4. Auto-sets `embed_view` to `{ "uri": "<url>", "renderer": 0 }` (WebView)
5. Content indexing is skipped — blog posts are not added to the semantic search index

**Response:** `200 OK` — returns the created post

**Error Responses:**

| Status | Condition |
|--------|-----------|
| `400 Bad Request` | Content is not a valid URL |
| `401 Unauthorized` | Not authenticated |
| `403 Forbidden` | Missing `posts.create.blog` permission |
| `403 Forbidden` | Domain not verified for the publisher |

## Updating a Blog Post

```http
PATCH /api/posts/{id}
```

The same domain verification and permission checks apply when:

- changing `type` to Blog
- changing `content` (the URL) on an existing Blog post

The `embed_view` is auto-updated when the URL changes. Client-provided `embed_view` is ignored for Blog posts.

## Listing Blog Posts

Blog posts appear in all standard post listing endpoints. Filter by type:

```http
GET /api/posts?type=2
GET /api/posts?pub={name}&type=2
```

Blog posts receive the same timeline ranking boost as Articles.

In ActivityPub, Blog posts are rendered as `type: "Article"` (same as Article posts).

## Response Shape

Blog posts return the same shape as other posts. Key differences:

```json
{
  "id": "...",
  "type": 2,
  "content": "https://blog.example.com/my-article",
  "title": "My Blog Post",
  "description": "A short summary",
  "embed_view": {
    "uri": "https://blog.example.com/my-article",
    "renderer": 0
  },
  "reactions_count": {},
  "replies_count": 0
}
```

The client should:

1. Render the post card with title + description
2. Open the blog URL in an in-app WebView when tapped
3. Use `embed_view.uri` as the WebView source
4. Display reactions, replies, and other interactions normally

## Verified Domain Management

Base path: `/api/publishers/{publisherName}/domains`

### List Domains

```http
GET /api/publishers/{publisherName}/domains
```

**Role required:** Editor

**Response:** `200 OK`

```json
[
  {
    "id": "a1b2c3d4-...",
    "publisher_id": "f37d7331-...",
    "domain": "blog.example.com",
    "status": 1,
    "verified_at": "2026-06-22T12:00:00Z",
    "last_checked_at": "2026-06-22T12:00:00Z",
    "failed_attempts": 0,
    "last_error": null,
    "created_at": "2026-06-22T11:00:00Z",
    "updated_at": "2026-06-22T12:00:00Z"
  }
]
```

### Add Domain

```http
POST /api/publishers/{publisherName}/domains
```

**Role required:** Manager

**Request Body:**

```json
{
  "domain": "blog.example.com"
}
```

Triggers an immediate `.well-known` verification in the background. The response returns the record with `status: 0` (Pending). Poll via the list endpoint or use the recheck endpoint to get the updated status.

**Response:** `200 OK` — returns the domain record

**Error Responses:**

| Status | Condition |
|--------|-----------|
| `400 Bad Request` | Empty domain |
| `401 Unauthorized` | Not authenticated |
| `403 Forbidden` | Not a manager of the publisher |
| `409 Conflict` | Domain already exists for this publisher |
| `404 Not Found` | Publisher not found |

### Remove Domain

```http
DELETE /api/publishers/{publisherName}/domains/{domainId}
```

**Role required:** Manager

**Response:** `204 No Content`

### Recheck Domain

```http
POST /api/publishers/{publisherName}/domains/{domainId}/recheck
```

**Role required:** Manager

Resets status to Pending and re-runs the `.well-known` check synchronously. Returns the updated record.

**Response:** `200 OK` — returns the domain record with updated status

## Migration

Run from the Sphere project:

```bash
dotnet ef migrations add AddBlogPostType --project DysonNetwork.Sphere
```

Creates the `publisher_verified_domains` table. No changes to the `posts` table — `PostType` is stored as an integer column.
