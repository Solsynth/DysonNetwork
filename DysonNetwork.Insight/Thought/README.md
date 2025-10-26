# DysonNetwork Insight Thought API

The Thought API provides conversational AI capabilities for users of the Solar Network. It allows users to engage in chat-like conversations with an AI assistant powered by semantic kernel and connected to various tools.

This service is handled by the Insight, when using with the Gateway, the `/api` should be replaced with `/insight`

## Features

- Streaming chat responses using Server-Sent Events (SSE)
- Conversation context management with sequences
- Caching for improved performance
- Authentication required for all operations

## Endpoints

### POST /api/thought

Initiates or continues a chat conversation.

#### Parameters
- `UserMessage` (string, required): The message from the user
- `SequenceId` (Guid, optional): ID of existing conversation sequence. If not provided, a new sequence is created.

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

Retrieves all thoughts (messages) in a specific conversation sequence.

#### Parameters
- `sequenceId` (Guid, path): ID of the sequence to retrieve

#### Response
- `200 OK`: Array of `SnThinkingThought` ordered by creation date
- `401 Unauthorized`: If not authenticated
- `404 Not Found`: If sequence doesn't exist or doesn't belong to user

#### Example Usage
```bash
curl -X GET "http://localhost:5000/api/thought/sequences/12345678-1234-1234-1234-123456789abc"
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
