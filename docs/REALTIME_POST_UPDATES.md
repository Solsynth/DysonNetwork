# Real-Time Post Updates via WebSocket

This document describes how to implement real-time post updates in DysonNetwork clients using WebSocket connections.

## Overview

DysonNetwork supports real-time updates for post operations. When a post is created, updated, or deleted, connected clients receive WebSocket packets containing the post data. This enables live updates without polling.

## WebSocket Connection

### Establishing Connection

Connect to the WebSocket endpoint at:

```
wss://api.dysonn.network/ws
```

Include your authentication token in the connection headers or query parameters as required by your authentication flow.

### Packet Format

All real-time post updates use the following WebSocket packet structure:

```json
{
  "type": "post.created" | "post.updated" | "post.deleted",
  "data": "<base64-encoded-json>"
}
```

The `data` field contains a Base64-encoded JSON string representing the `SnPost` object.

## Event Types

### 1. Post Created (`post.created`)

Triggered when a new post is published.

**When you receive this:**
- Add the new post to your feed/timeline
- Update post counters
- Show notification if appropriate

**Example handler:**
```typescript
function handlePostCreated(post: SnPost) {
  // Add to timeline
  timeline.unshift(post);
  
  // Show notification if not from current user
  if (post.publisherId !== currentUserId) {
    showNotification('New post', post.title || 'Untitled');
  }
}
```

### 2. Post Updated (`post.updated`)

Triggered when an existing post is modified.

**When you receive this:**
- Update the post in your local state
- Refresh the UI if the post is currently visible

**Example handler:**
```typescript
function handlePostUpdated(post: SnPost) {
  const index = timeline.findIndex(p => p.id === post.id);
  if (index !== -1) {
    timeline[index] = { ...timeline[index], ...post };
  }
}
```

### 3. Post Deleted (`post.deleted`)

Triggered when a post is removed.

**When you receive this:**
- Remove the post from your local state
- Show "post deleted" placeholder if viewing the post

**Example handler:**
```typescript
function handlePostDeleted(post: SnPost) {
  timeline = timeline.filter(p => p.id !== post.id);
  
  if (currentViewingPostId === post.id) {
    showPostDeletedMessage();
  }
}
```

## Post Data Structure

The `data` field contains a complete `SnPost` object with the following key fields:

```typescript
interface SnPost {
  id: string;                    // UUID
  slug: string;                  // URL-friendly identifier
  title?: string;               // Post title
  content?: string;             // Post content (HTML or Markdown)
  contentType: 'html' | 'markdown' | 'plain';
  description?: string;         // Short description
  
  // Publisher info
  publisherId?: string;
  publisher?: SnPublisher;
  
  // Reply/Forward info
  repliedPostId?: string;
  repliedPost?: SnPost;
  forwardedPostId?: string;
  forwardedPost?: SnPost;
  
  // Visibility
  visibility: 'public' | 'unlisted' | 'friends' | 'private';
  
  // Engagement
  upvotes: number;
  downvotes: number;
  repliesCount: number;
  reactionsCount: Record<string, number>;
  reactionsMade?: Record<string, boolean>;
  
  // Metadata
  attachments: SnAttachment[];
  tags: SnPostTag[];
  categories: SnPostCategory[];
  
  // Timestamps
  createdAt: string;            // ISO 8601
  editedAt?: string;
  publishedAt?: string;
}
```

## Visibility & Filtering

The server automatically filters updates based on post visibility:

| Visibility | Who Receives Updates |
|------------|---------------------|
| **Public** | All connected users |
| **Unlisted** | All connected users |
| **Friends** | Publisher members + friends only |
| **Private** | Publisher members only |

**Important:** Clients don't need to implement visibility filtering - the server handles this. However, clients should respect visibility when displaying posts.

## Implementation Guide

### TypeScript/JavaScript Example

```typescript
class RealtimePostManager {
  private ws: WebSocket;
  
  constructor(authToken: string) {
    this.ws = new WebSocket(`wss://api.dysonn.network/ws?token=${authToken}`);
    this.ws.onmessage = this.handleMessage.bind(this);
  }
  
  private handleMessage(event: MessageEvent) {
    const packet = JSON.parse(event.data);
    
    if (!['post.created', 'post.updated', 'post.deleted'].includes(packet.type)) {
      return; // Not a post update
    }
    
    // Decode Base64 data
    const postData = JSON.parse(atob(packet.data));
    
    switch (packet.type) {
      case 'post.created':
        this.onPostCreated(postData);
        break;
      case 'post.updated':
        this.onPostUpdated(postData);
        break;
      case 'post.deleted':
        this.onPostDeleted(postData);
        break;
    }
  }
  
  private onPostCreated(post: SnPost) {
    console.log('New post:', post.title);
    // Update your UI state
  }
  
  private onPostUpdated(post: SnPost) {
    console.log('Post updated:', post.id);
    // Update your UI state
  }
  
  private onPostDeleted(post: SnPost) {
    console.log('Post deleted:', post.id);
    // Update your UI state
  }
}
```

### C# Example

```csharp
public class RealtimePostClient
{
    private readonly ClientWebSocket _ws;
    
    public async Task ConnectAsync(string authToken)
    {
        _ws = new ClientWebSocket();
        _ws.Options.SetRequestHeader("Authorization", $"Bearer {authToken}");
        await _ws.ConnectAsync(new Uri("wss://api.dysonn.network/ws"), CancellationToken.None);
        
        _ = ReceiveLoopAsync();
    }
    
    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[4096];
        
        while (_ws.State == WebSocketState.Open)
        {
            var result = await _ws.ReceiveAsync(buffer, CancellationToken.None);
            var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
            
            var packet = JsonSerializer.Deserialize<WebSocketPacket>(message);
            
            if (packet?.Type is "post.created" or "post.updated" or "post.deleted")
            {
                var postData = Convert.FromBase64String(packet.Data);
                var post = JsonSerializer.Deserialize<SnPost>(postData);
                
                HandlePostUpdate(packet.Type, post);
            }
        }
    }
    
    private void HandlePostUpdate(string type, SnPost post)
    {
        switch (type)
        {
            case "post.created":
                OnPostCreated?.Invoke(post);
                break;
            case "post.updated":
                OnPostUpdated?.Invoke(post);
                break;
            case "post.deleted":
                OnPostDeleted?.Invoke(post);
                break;
        }
    }
    
    public event Action<SnPost>? OnPostCreated;
    public event Action<SnPost>? OnPostUpdated;
    public event Action<SnPost>? OnPostDeleted;
}
```

## Best Practices

1. **Background Processing**: Handle WebSocket messages in a background thread/task to avoid blocking the UI

2. **State Management**: Use a reactive state management system (Redux, MobX, Vuex, etc.) to automatically update UI when post data changes

3. **Optimistic Updates**: Don't wait for the update - apply changes optimistically and reconcile if needed

4. **Error Handling**: Always handle WebSocket disconnections and implement reconnection logic

5. **Visibility Respect**: Even though the server filters by visibility, double-check visibility before displaying posts in sensitive contexts

6. **Deduplication**: If you receive the same post update multiple times (rare but possible), implement deduplication by post ID

7. **Battery Efficiency**: Consider throttling updates or pausing the WebSocket connection when the app is in the background

## Troubleshooting

### Not receiving updates?
- Verify WebSocket connection is active
- Check authentication token is valid
- Ensure you're subscribed to the correct feed/channel

### Receiving updates you shouldn't?
- This is handled server-side - contact support if visibility filtering isn't working

### Duplicate updates?
- Implement deduplication using post ID and timestamp
- Check if the update is newer than your current data before applying

## Migration from Polling

If you're currently using polling for post updates:

1. Keep polling as a fallback for users without WebSocket support
2. Use WebSocket for real-time updates when available
3. Implement exponential backoff for polling when WebSocket is connected

```typescript
// Hybrid approach
if (websocket.isConnected()) {
  // Disable or slow down polling
  pollInterval = 60000; // 1 minute backup poll
} else {
  // Use normal polling
  pollInterval = 5000;  // 5 second poll
}
```

## Related Documentation

- [Presence Activity API](./PRESENCE_ACTIVITY_API.md)
- [WebSocket Authentication](./WEB_LOCAL_CREDENTIAL_SHARING.md)
- [Post API Reference](../api/posts.md)
