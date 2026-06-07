# Post Listing Error Responses

## Overview

When listing posts with the `pub=` query parameter, the API now returns detailed error responses instead of an empty list when access is restricted. This provides clear feedback to clients about why posts cannot be viewed.

## Endpoint

```
GET /api/posts?pub={publisher_name}
```

## Error Scenarios

### 1. Publisher is Gatekept (Subscription Required)

When a publisher has enabled `gatekept_follows`, their posts are only available to subscribers.

**Response:** `403 Forbidden`

```json
{
  "code": "PUBLISHER_GATEKEPT",
  "message": "publisher_name's posts are only available to subscribers.",
  "status": 403,
  "detail": "Subscribe to owner_name's publisher to access their posts.",
  "meta": {
    "publisher": "publisher_name",
    "is_gatekept": true,
    "requires_subscription": true
  }
}
```

**Conditions:**
- Publisher has `gatekept_follows = true`
- Current user is not an active subscriber
- Current user is not a member of the publisher

### 2. Blocked by Publisher

When the publisher's account has blocked the current user.

**Response:** `403 Forbidden`

```json
{
  "code": "BLOCKED_BY_PUBLISHER",
  "message": "You cannot view this publisher's posts because they have blocked you.",
  "status": 403,
  "meta": {
    "publisher": "publisher_name",
    "is_blocked": true,
    "blocked_by_publisher": true
  }
}
```

**Conditions:**
- Publisher's account has blocked the current user
- Current user is authenticated

### 3. User Blocked Publisher

When the current user has blocked the publisher's account.

**Response:** `403 Forbidden`

```json
{
  "code": "PUBLISHER_BLOCKED",
  "message": "You have blocked this publisher. Unblock them to view their posts.",
  "status": 403,
  "meta": {
    "publisher": "publisher_name",
    "is_blocked": true,
    "blocked_by_publisher": false
  }
}
```

**Conditions:**
- Current user has blocked the publisher's account
- Current user is authenticated

### 4. Authentication Required (Gatekept Publisher)

When an unauthenticated user attempts to view a gatekept publisher's posts.

**Response:** `401 Unauthorized`

```json
{
  "code": "AUTHENTICATION_REQUIRED",
  "message": "Authentication is required to view this publisher's posts.",
  "status": 401,
  "detail": "This publisher requires subscribers to be authenticated.",
  "meta": {
    "publisher": "publisher_name",
    "is_gatekept": true,
    "requires_authentication": true
  }
}
```

**Conditions:**
- Publisher has `gatekept_follows = true`
- No authentication provided (anonymous user)

## Error Response Structure

All error responses follow the `ApiError` schema:

| Field | Type | Description |
|-------|------|-------------|
| `code` | `string` | Application-specific error code |
| `message` | `string` | Human-readable error message |
| `status` | `int` | HTTP status code |
| `detail` | `string?` | Additional context or instructions |
| `meta` | `object?` | Structured metadata for programmatic handling |

## Client Implementation

### JavaScript/TypeScript Example

```typescript
async function fetchPosts(publisherName: string) {
  const response = await fetch(`/api/posts?pub=${publisherName}`);
  
  if (!response.ok) {
    const error = await response.json();
    
    switch (error.code) {
      case 'PUBLISHER_GATEKEPT':
        // Show subscription prompt
        showSubscriptionDialog(error.meta.publisher);
        break;
        
      case 'BLOCKED_BY_PUBLISHER':
        // Show "blocked by user" message
        showBlockedMessage(error.meta.publisher, true);
        break;
        
      case 'PUBLISHER_BLOCKED':
        // Show option to unblock
        showUnblockOption(error.meta.publisher);
        break;
        
      case 'AUTHENTICATION_REQUIRED':
        // Redirect to login
        redirectToLogin();
        break;
        
      default:
        // Handle other errors
        showGenericError(error.message);
    }
    return;
  }
  
  // Success - process posts
  const posts = await response.json();
  return posts;
}
```

### Swift Example

```swift
func fetchPosts(publisherName: String) async throws -> [Post] {
    let url = URL(string: "/api/posts?pub=\(publisherName)")!
    let (data, response) = try await URLSession.shared.data(from: url)
    
    guard let httpResponse = response as? HTTPURLResponse else {
        throw NetworkError.invalidResponse
    }
    
    if httpResponse.statusCode != 200 {
        let error = try JSONDecoder().decode(ApiError.self, from: data)
        
        switch error.code {
        case "PUBLISHER_GATEKEPT":
            throw PostError.gatekept(publisher: error.meta?["publisher"] as? String ?? "")
        case "BLOCKED_BY_PUBLISHER":
            throw PostError.blockedByPublisher
        case "PUBLISHER_BLOCKED":
            throw PostError.publisherBlocked
        case "AUTHENTICATION_REQUIRED":
            throw PostError.authenticationRequired
        default:
            throw PostError.unknown(error.message)
        }
    }
    
    return try JSONDecoder().decode([Post].self, from: data)
}
```

## Behavior Notes

1. **Normal Empty Results**: If the publisher exists and has no posts, or all posts are filtered by other criteria (date, category, etc.), the API returns `200 OK` with an empty array and `X-Total: 0` header.

2. **Error Priority**: The error checks occur in this order:
   - Gatekept publisher (subscription required)
   - Blocked by publisher
   - User blocked publisher
   - Authentication required (for gatekept publishers)

3. **Publisher Not Found**: If the publisher name doesn't exist, the API returns `404 Not Found` (unchanged behavior).

4. **Other Query Parameters**: The error responses apply regardless of other query parameters (`type`, `categories`, `tags`, etc.). If access is restricted, no posts will be returned.

## Related Documentation

- [BLOCK_SYSTEM.md](BLOCK_SYSTEM.md) - Block system details
- [GATEKEEPED_PUBLISHER.md](GATEKEEPED_PUBLISHER.md) - Gatekept publisher feature
- [API_POSTS.md](API_POSTS.md) - Posts API overview
