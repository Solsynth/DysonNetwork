# Plan: Add Blog Post Type

## Context

We need a new `PostType.Blog` — a post that links to an external website's blog post. The content is just a URL; we don't store the blog content. The client renders it in an in-app WebView. The post owner retains interactive features (reactions, replies, etc.).

Key security constraint: publishers maintain a verified domains list, and users can only create blog posts from those domains. Superusers bypass this check. A separate permission node (`posts.create.blog`) controls access.

## Approach

### 1. Add `Blog` to `PostType` enum
**File:** `DysonNetwork.Shared/Models/Post.cs` (line 17)

```csharp
public enum PostType
{
    Moment,
    Article,
    Blog,
}
```

Also update the generated proto file `DysonNetwork.Shared/Proto/Post.cs` (line 256) — add `DY_BLOG = 3` to `DyPostType` enum. Update `PostServiceGrpc.cs` (line 249) switch expression to map `DyBlog => PostType.Blog`. Update `ActivityRenderer.cs` (line 108) to map Blog to `"Article"` type in AP. Update `ActivityHandlerService.cs` (line 424) reverse mapping.

### 2. Create `SnPublisherVerifiedDomain` model (Sphere-local)
**New file:** `DysonNetwork.Sphere/Models/PublisherVerifiedDomain.cs`

A separate table in the Sphere project (not Shared) with metadata:

```csharp
public enum DomainVerificationStatus
{
    Pending,      // Added, awaiting .well-known check
    Verified,     // .well-known file confirmed
    Failed,       // Verification attempt failed
    Revoked,      // Manually revoked
}

[Index(nameof(PublisherId), nameof(Domain), IsUnique = true)]
public class SnPublisherVerifiedDomain : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PublisherId { get; set; }
    public SnPublisher Publisher { get; set; } = null!;

    [MaxLength(512)]
    public string Domain { get; set; } = string.Empty;

    public DomainVerificationStatus Status { get; set; } = DomainVerificationStatus.Pending;
    public Instant? VerifiedAt { get; set; }
    public Instant? LastCheckedAt { get; set; }
    public int FailedAttempts { get; set; } = 0;
    [MaxLength(4096)] public string? LastError { get; set; }
}
```

Register `DbSet<SnPublisherVerifiedDomain>` in `DysonNetwork.Sphere/AppDatabase.cs`.

### 3. Create domain verification service
**New file:** `DysonNetwork.Sphere/Publisher/DomainVerificationService.cs`

Implements `.well-known` verification:
- Fetch `https://{domain}/.well-known/dyson-domains.txt`
- File should contain the publisher name (one per line), confirming the domain belongs to that publisher
- Uses `IHttpClientFactory` (same pattern as `ActivityPubDiscoveryService`)
- Exposes `Task<bool> VerifyDomainAsync(Guid publisherId, string domain)` and `Task VerifyPendingDomainsAsync()` for batch re-check
- Called on domain add + periodic background re-check

### 4. Add DB migration

Run EF CLI after all model changes are in place:

```bash
dotnet ef migrations add AddBlogPostType --project DysonNetwork.Sphere
```

This auto-generates the migration for the new `publisher_verified_domains` table and the `PostType` enum value change. Review the generated migration before applying.

### 5. Seed new permission node
**File:** `DysonNetwork.Padlock/AppDatabase.cs` (line 70)

Add `"posts.create.blog"` to the default permission group seed:

```csharp
"posts.create",
"posts.create.blog",  // <-- new
"posts.react",
```

### 6. Add domain verification + blog post creation logic
**File:** `DysonNetwork.Sphere/Post/PostActionController.cs`

In `CreatePost` (around line 175), after resolving the publisher, add blog-specific logic:

```csharp
if (request.Type == PostType.Blog)
{
    // 1. Check permission — already covered by [AskPermission("posts.create.blog")]

    // 2. Validate content is a URL
    if (string.IsNullOrWhiteSpace(request.Content) || !Uri.TryCreate(request.Content, UriKind.Absolute, out var blogUri))
        return BadRequest("Blog post content must be a valid URL.");

    // 3. Domain verification (skip for superusers)
    if (!currentUser.IsSuperuser)
    {
        var host = blogUri.Host.ToLowerInvariant();
        var isDomainVerified = await db.PublisherVerifiedDomains
            .AnyAsync(d => d.PublisherId == publisher.Id
                && d.Domain == host
                && d.Status == DomainVerificationStatus.Verified);
        if (!isDomainVerified)
            return StatusCode(403, "This domain is not verified for your publisher. Add it via the domains endpoint first.");
    }

    // 4. Set EmbedView so client knows to render in WebView
    post.EmbedView = new PostEmbedView
    {
        Uri = request.Content,
        Renderer = PostEmbedViewRenderer.WebView
    };
}
```

Same logic added to `UpdatePost` (~line 1034) for when type changes to Blog or URL changes.

### 7. Publisher verified domains management endpoint
**File:** `DysonNetwork.Sphere/Publisher/PublisherController.cs` (or wherever publisher management lives)

Endpoints:

```
POST   /api/publishers/{name}/domains              # Add domain (triggers .well-known verification)
Body: { "domain": "blog.example.com" }

GET    /api/publishers/{name}/domains              # List domains + their status
DELETE /api/publishers/{name}/domains/{id}         # Remove a verified domain
POST   /api/publishers/{name}/domains/{id}/recheck # Manually re-trigger verification
```

Only Owner/Manager can add/remove. On `POST`, `DomainVerificationService` immediately checks `.well-known/dyson-domains.txt` and updates status. Returns the domain record with `status: Pending` or `Verified`.

### 8. Skip content processing for Blog posts
**File:** `DysonNetwork.Sphere/Post/PostService.cs`

In `PostAsync` / `UpdatePostAsync`, skip content indexing/embedding generation for Blog posts since we don't store content — just the link.

### 9. Update ActionLog constants
**File:** `DysonNetwork.Shared/Models/ActionLog.cs`

```csharp
public const string PostCreateBlog = "posts.create.blog";
```

## Files to modify

1. `DysonNetwork.Shared/Models/Post.cs` — Add `Blog` to `PostType` enum
2. **New:** `DysonNetwork.Sphere/Models/PublisherVerifiedDomain.cs` — Verified domain model with status tracking
3. **New:** `DysonNetwork.Sphere/Publisher/DomainVerificationService.cs` — `.well-known` verification logic
4. `DysonNetwork.Shared/Proto/Post.cs` — Add `DY_BLOG` to proto enum
5. `DysonNetwork.Sphere/Post/PostActionController.cs` — Domain verification logic in CreatePost + UpdatePost
6. `DysonNetwork.Sphere/Post/PostService.cs` — Skip content indexing for Blog type
7. `DysonNetwork.Padlock/AppDatabase.cs` — Seed `posts.create.blog` permission
8. `DysonNetwork.Sphere/AppDatabase.cs` — Add `DbSet<SnPublisherVerifiedDomain>` + FK config
9. Publisher management controller — Add verified domains CRUD endpoints
10. New migration file — Create `publisher_verified_domains` table

## Reuse

- `DomainTrustService.MatchesDomainPattern()` — reuse for matching domains against verified list (`DysonNetwork.Passport/DomainTrust/DomainTrustService.cs:132`)
- `PostEmbedView` / `PostEmbedViewRenderer.WebView` — already exists, use as-is for client rendering signal
- `AskPermission` attribute pattern — same as `posts.create`
- `IsSuperuser` check — pattern used in many places already
- `IHttpClientFactory` — same pattern as `ActivityPubDiscoveryService` for `.well-known` fetch
- `SnDiscoveryPreference` — Sphere-local model pattern reference for new domain model

## Verification

1. Create a blog post with a URL from a verified domain → should succeed
2. Create a blog post with a URL from an unverified domain → should get 403
3. Superuser creates blog post from any domain → should succeed
4. Create a blog post without `posts.create.blog` permission → should be denied
5. Client receives `EmbedView` with the URL and WebView renderer
6. Reactions and replies work on blog posts (same as regular posts)
7. Add domain to publisher → `.well-known` check runs → status becomes Verified or Failed
8. Domain with `Pending` status → blog post creation blocked
9. Manually re-check domain → status updates
10. List publisher domains → shows status, verified_at, last_checked_at
