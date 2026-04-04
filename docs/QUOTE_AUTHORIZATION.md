# Quote Authorization (FEP-044f)

This document describes the implementation of FEP-044f: Consent-respecting quote posts for the ActivityPub federation system.

## Overview

FEP-044f introduces a mechanism for post authors to control who can quote their posts, with a revocable authorization stamp system. This prevents unauthorized quoting while allowing automatic approval for trusted actors (followers, public).

## Implementation

### Database Models

#### SnQuoteAuthorization

```csharp
public class SnQuoteAuthorization : ModelBase
{
    public Guid Id { get; set; }
    public string? FediverseUri { get; set; }  // Publicly dereferenceable URI
    public Guid AuthorId { get; set; }           // The author who grants permission
    public string InteractingObjectUri { get; set; }   // The quote post URI
    public string InteractionTargetUri { get; set; }   // The quoted post URI
    public Guid? TargetPostId { get; set; }     // Local target post reference
    public Guid? QuotePostId { get; set; }      // Local quote post reference
    public bool IsValid { get; set; } = true;   // Revoked if false
    public Instant? RevokedAt { get; set; }      // Revocation timestamp
}
```

#### SnPost Changes

```csharp
public class SnPost
{
    // ... existing fields ...
    public Guid? ForwardedPostId { get; set; }    // Local forward/quote reference
    public SnPost? ForwardedPost { get; set; }
    public Guid? QuoteAuthorizationId { get; set; }  // Authorization stamp reference
    public SnQuoteAuthorization? QuoteAuthorization { get; set; }
}
```

### ActivityPub Object Format

#### Quote Post (Outgoing)

When creating a forward (quote) post, the system includes:

```json
{
    "@context": [
        "https://www.w3.org/ns/activitystreams",
        {
            "quote": "https://w3id.org/fep/044f#quote",
            "quoteAuthorization": "https://w3id.org/fep/044f#quoteAuthorization"
        }
    ],
    "type": "Note",
    "id": "https://example.com/posts/{id}",
    "quote": "https://other.instance/users/alice/posts/123",
    "quoteUrl": "https://other.instance/users/alice/posts/123",
    "quoteUri": "https://other.instance/users/alice/posts/123",
    "quoteAuthorization": "https://example.com/quote-authorizations/{authId}",
    "content": "Great post! <span class=\"quote-inline\"><br/>RE: https://other.instance/users/alice/posts/123</span>"
}
```

#### QuoteAuthorization Object

```json
{
    "@context": [
        "https://www.w3.org/ns/activitystreams",
        {
            "QuoteAuthorization": "https://w3id.org/fep/044f#QuoteAuthorization",
            "gts": "https://gotosocial.org/ns#",
            "interactingObject": "gts:interactingObject",
            "interactionTarget": "gts:interactionTarget"
        }
    ],
    "type": "QuoteAuthorization",
    "id": "https://example.com/quote-authorizations/{id}",
    "attributedTo": "https://example.com/users/bob",
    "interactingObject": "https://example.com/posts/456",
    "interactionTarget": "https://other.instance/users/alice/posts/123"
}
```

### API Endpoints

#### GET /quote-authorizations/{id}

Dereference a QuoteAuthorization object.

**Response:** Returns the QuoteAuthorization JSON-LD object

#### POST /quote-authorizations

Create a new QuoteAuthorization (internal use).

**Request:**
```json
{
    "interactingObjectUri": "https://example.com/posts/456",
    "interactionTargetUri": "https://other.instance/users/alice/posts/123"
}
```

#### DELETE /quote-authorizations/{id}

Revoke a QuoteAuthorization.

### Activity Types

#### QuoteRequest (Incoming)

When receiving a quote request from a remote actor:

1. Parse the `object` (quoted post URI)
2. Parse the `instrument` (quote post)
3. Check author's quote policy (automatic approval for followers/public)
4. Create QuoteAuthorization if approved
5. Send Accept activity with `result` pointing to authorization

```json
{
    "type": "QuoteRequest",
    "actor": "https://other.instance/users/charlie",
    "object": "https://example.com/users/bob/posts/123",
    "instrument": {
        "type": "Note",
        "id": "https://other.instance/users/charlie/posts/456",
        "content": "Sharing this!"
    }
}
```

#### Accept (QuoteResponse)

When receiving acceptance of a quote request:

1. Parse the `result` (authorization URI)
2. Store the authorization reference on the local quote post
3. Update post to include `quoteAuthorization` property

### Quote Policy

The system supports the following quote policies (via `interactionPolicy`):

- **Everyone**: Anyone can quote (automatic approval)
- **Followers**: Only followers can quote (automatic if follower)
- **Nobody**: No one can quote (reject all requests)

Current implementation: Auto-approves all quote requests (simplified).

### Revocation Flow

To revoke a previously approved quote:

1. Author deletes the QuoteAuthorization object
2. Delete activity is sent to quote post author
3. Quote post is marked as unapproved
4. Client should re-verify authorizations periodically

## Usage in Clients

### Creating a Quote Post

1. User selects "Forward/Quote" on a post
2. If post has `interactionPolicy.canQuote`:
   - Check if author is in `automaticApproval` → proceed
   - Otherwise, send QuoteRequest and wait for Accept
3. Create post with `quote` property pointing to original
4. If authorization received, add `quoteAuthorization` to post

### Displaying Quote Posts

1. Fetch the quote post normally
2. If `quoteAuthorization` exists:
   - Dereference the authorization
   - Verify `interactingObject` matches quote post
   - Verify `interactionTarget` matches quoted post
   - Verify `attributedTo` matches quoted post author
   - Verify authorization is valid (not revoked)
3. If self-quote or valid authorization → display quote
4. Otherwise → display without quote embed

## Compatibility

The implementation includes compatibility with other quote formats:

- `quote` - FEP-044f (canonical)
- `quoteUrl` - ActivityStreams
- `quoteUri` - Fedibird
- `_misskey_quote` - Misskey
