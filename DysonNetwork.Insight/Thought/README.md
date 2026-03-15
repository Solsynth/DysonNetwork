# DysonNetwork Insight Thought API

The Thought API provides conversational AI capabilities for users of the Solar Network. It allows users to engage in chat-like conversations with an AI assistant powered by semantic kernel and connected to various tools.

This service is handled by the Insight, when using with the Gateway, the `/api` should be replaced with `/insight`

## Features

- Streaming chat responses using Server-Sent Events (SSE)
- Conversation context management with sequences
- Caching for improved performance
- Authentication required for all operations
- MiChan unified-thread support with prompt compaction
- Paginated sequence-history reads

## Endpoints

### POST /api/thought

Initiates or continues a chat conversation.

#### Parameters
- `UserMessage` (string, required): The message from the user
- `SequenceId` (Guid, optional): ID of existing conversation sequence

#### Bot-specific sequence behavior

- **SnChan**: If `SequenceId` is omitted, a new conversation sequence is created
- **MiChan**: If `SequenceId` is omitted, the canonical MiChan sequence is used or created automatically
- **MiChan**: If a non-canonical `SequenceId` is provided, the request is rejected

#### Response
- Content-Type: `text/event-stream`
- Streaming response with assistant messages
- Status: 401 if not authenticated
- Status: 403 if sequence doesn't belong to user

#### Example Usage
```bash
curl -X POST "http://localhost:5000/api/thought" \
  -H "Content-Type: application/json" \
  -d '{
    "UserMessage": "Hello, how can I help with the Solar Network?",
    "SequenceId": null
  }'
```

### GET /api/thought/sequences

Lists all thinking sequences for the authenticated user.

#### Parameters
- `offset` (int, default 0): Number of sequences to skip for pagination
- `take` (int, default 20): Maximum number of sequences to return

#### Response
- `200 OK`: Array of `SnThinkingSequence`
- `401 Unauthorized`: If not authenticated
- Headers:
  - `X-Total`: Total number of sequences before pagination

#### Example Usage
```bash
curl -X GET "http://localhost:5000/api/thought/sequences?take=10"
```

### GET /api/thought/sequences/{sequenceId}

Retrieves thoughts (messages) in a specific conversation sequence.

#### Parameters
- `sequenceId` (Guid, path): ID of the sequence to retrieve
- `offset` (int, default 0): Number of visible thoughts to skip
- `take` (int, default 50, max 200): Maximum number of visible thoughts to return

#### Response
- `200 OK`: Array of `SnThinkingThought` ordered by creation date descending
- `401 Unauthorized`: If not authenticated
- `404 Not Found`: If sequence doesn't exist or doesn't belong to user
- Headers:
  - `X-Has-More`: `true` if more visible thoughts can be fetched
  - `X-Offset`: Effective offset used for this page
  - `X-Take`: Effective take used for this page

#### Example Usage
```bash
curl -X GET "http://localhost:5000/api/thought/sequences/12345678-1234-1234-1234-123456789abc?offset=0&take=50"
```

## Data Models

### StreamThinkingRequest
```csharp
{
  string UserMessage, // Required
  Guid? SequenceId    // Optional
}
```

### SnThinkingSequence
```csharp
{
  Guid Id,
  string? Topic,
  Guid AccountId
}
```

### MiChan User Profile
```csharp
{
  Guid AccountId,
  string? ProfileSummary,
  string? ImpressionSummary,
  string? RelationshipSummary,
  List<string> Tags,
  int Favorability,
  int TrustLevel,
  int IntimacyLevel,
  int InteractionCount
}
```

### SnThinkingThought
```csharp
{
  Guid Id,
  string? Content,
  List<SnCloudFileReferenceObject> Files,
  ThinkingThoughtRole Role,
  Guid SequenceId,
  SnThinkingSequence Sequence
}
```

### ThinkingThoughtRole (enum)
- `Assistant`
- `User`

## Caching

The API uses Redis-based caching for conversation thoughts:
- Thoughts are cached for 10 minutes with group-based invalidation
- Cache is invalidated when new thoughts are added to a sequence
- Improves performance for accessing conversation history

## MiChan Notes

- MiChan uses a single canonical conversation sequence per account
- Older MiChan history can be compacted into internal summary thoughts for prompt assembly
- Internal compaction thoughts are excluded from normal history responses
- Proactive MiChan messages started through the conversation plugin append to the canonical MiChan sequence
- MiChan also keeps a structured per-user profile to track impressions and relationship state

## Authentication

All endpoints require authentication through the current user session. Sequence access is validated against the authenticated user's account ID.

## Error Responses

- `401 Unauthorized`: Authentication required
- `403 Forbidden`: Access denied (sequence ownership)
- `404 Not Found`: Resource not found

## Streaming Details

The POST endpoint returns a stream of assistant responses using Server-Sent Events format. Clients should handle the streaming response and display messages incrementally.

### Streaming Message Format

The streaming response sends several types of JSON messages:

- **Text messages**: `{"type": "text", "data": "..." }`
- **Function calls**: `{"type": "function_call", "data": {...} }` (when AI uses tools)
- **Topic updates**: `{"type": "topic", "data": "..." }` (sent at end if topic was generated)
- **Thought completion**: `{"type": "thought", "data": {...} }` (sent at end with saved thought details)

All streaming chunks during generation use the SSE event format:
```
data: {"type": "...", "data": ...}

```

Final messages (topic and thought) use custom event types:
```
topic: {"type": "topic", "data": "..."}

thought: {"type": "thought", "data": {...}}
```

Clients should parse these JSON messages and handle different types appropriately, such as displaying text in real-time and processing tool calls.

## Implementation Notes

- Built with ASP.NET Core and Semantic Kernel
- Uses PostgreSQL via Entity Framework Core
- Integrated with Ollama for AI completion
- Caching via Redis
