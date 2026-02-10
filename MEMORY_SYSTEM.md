# MiChan Memory System Documentation

## Overview

MiChan now has a sophisticated memory system with **semantic search capabilities** using pgvector. The system stores conversations, interactions, and context in a separate vector database, enabling MiChan to recall relevant past interactions.

## Architecture

### Two-Layer Memory System

```
┌─────────────────────────────────────────────────────────────┐
│                     MiChanMemoryService                     │
│                      (Service Layer)                        │
├─────────────────────────────────────────────────────────────┤
│  ┌──────────────┐         ┌──────────────────────────────┐ │
│  │ In-Memory    │         │ AgentVectorService           │ │
│  │ Cache        │────────▶│ (Persistent Vector Store)    │ │
│  │ (Hot Data)   │         │                              │ │
│  └──────────────┘         │ • Semantic search            │ │
│                           │ • Vector similarity          │ │
│                           │ • Cross-session persistence  │ │
│                           └──────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│              AgentVector Database (PostgreSQL)              │
│                                                             │
│  Table: agent_memories                                      │
│  • id (UUID)                                                │
│  • agent_id (string) - identifies the bot                  │
│  • memory_type (string) - "thought", "autonomous", etc.    │
│  • context_id (string) - conversation/thread identifier    │
│  • content (text) - searchable content                     │
│  • embedding (vector(1536)) - semantic vector              │
│  • metadata (JSON) - additional data                       │
│  • importance (float) - relevance score                    │
│  • created_at (timestamp)                                  │
└─────────────────────────────────────────────────────────────┘
```

## Configuration

### Basic Setup

```json
{
  "ConnectionStrings": {
    "App": "Host=localhost;Port=5432;Database=dyson_insight;...",
    "AgentVector": "Host=localhost;Port=5432;Database=agent_vector;..."
  },
  "Embeddings": {
    "Provider": "openrouter",
    "Model": "qwen/qwen3-embedding-8b",
    "ApiKey": "sk-or-v1-your-key",
    "Endpoint": "https://openrouter.ai/api/v1"
  },
  "MiChan": {
    "Memory": {
      "MaxContextLength": 100,
      "PersistToDatabase": true
    }
  }
}
```

### Embedding Providers

The embedding service is **independent** of the chat provider. Supported providers:

1. **OpenRouter** (Recommended)
   - Model: `qwen/qwen3-embedding-8b` (1536 dimensions)
   - Works with any chat provider (DeepSeek, Aliyun, BigModel, etc.)

2. **Ollama** (Self-hosted)
   - Model: `nomic-embed-text`
   - No API costs, runs locally

3. **Aliyun DashScope**
   - Model: `text-embedding-v3`
   - Chinese-optimized embeddings

4. **OpenAI**
   - Model: `text-embedding-3-small`
   - Most accurate, but expensive

## Memory Types

### 1. Thought Memory

Stored during user conversations via `ThoughtController`:

```csharp
// Automatically stored after each MiChan response
await miChanMemoryService.StoreInteractionAsync(
    type: "thought",
    contextId: $"thought_{accountId}_{sequence.Id}",
    context: new Dictionary<string, object>
    {
        ["message"] = userMessage,
        ["response"] = aiResponse,
        ["timestamp"] = DateTime.UtcNow,
        ["is_superuser"] = isSuperuser
    }
);
```

**Use Case**: Remembering previous conversations with users

### 2. Autonomous Memory

Stored during MiChan's autonomous activities:

```csharp
// Stored when MiChan replies to posts
await _memoryService.StoreInteractionAsync(
    type: "autonomous",
    contextId: $"post_{post.Id}",
    context: new Dictionary<string, object>
    {
        ["action"] = "reply",
        ["post_id"] = post.Id.ToString(),
        ["content"] = replyContent
    }
);
```

**Use Case**: Tracking what MiChan has done autonomously

### 3. Context-Specific Memory

Any custom memory type you define:

```csharp
await _memoryService.StoreInteractionAsync(
    type: "user_preference",
    contextId: userId,
    context: new Dictionary<string, object>
    {
        ["preference"] = "likes_dark_mode",
        ["value"] = true
    }
);
```

## API Reference

### MiChanMemoryService

#### Store Interaction
```csharp
Task<MiChanInteraction> StoreInteractionAsync(
    string type,                    // Memory type identifier
    string contextId,               // Conversation/thread ID
    Dictionary<string, object> context,  // Data to store
    Dictionary<string, object>? memory = null,
    CancellationToken cancellationToken = default
)
```

#### Search Similar Memories
```csharp
Task<List<MiChanInteraction>> SearchSimilarInteractionsAsync(
    string query,                   // Natural language query
    int limit = 5,                  // Max results
    double? minSimilarity = 0.7,    // Minimum similarity (0-1)
    CancellationToken cancellationToken = default
)
```

**Example**: Searching for "user asked about pricing" returns relevant past conversations about pricing.

#### Get Recent Context
```csharp
Task<List<MiChanInteraction>> GetRecentInteractionsAsync(
    string contextId,               // Specific conversation
    int count = 10,                 // Number of recent items
    CancellationToken cancellationToken = default
)
```

**Example**: Get last 10 messages in the current conversation.

#### Hybrid Context Retrieval
```csharp
Task<List<MiChanInteraction>> GetRelevantContextAsync(
    string contextId,               // Current conversation
    string? currentQuery = null,    // For semantic search
    int semanticCount = 5,          // Similar memories
    int recentCount = 5,            // Recent memories
    CancellationToken cancellationToken = default
)
```

**Example**: Combines recent conversation + semantically similar past conversations.

### AgentVectorService

Low-level vector store operations:

```csharp
// Store with full control
await _vectorService.StoreMemoryAsync(
    agentId: "michan",
    memoryType: "custom",
    content: "searchable text",
    contextId: "conversation-123",
    title: "Optional title",
    metadata: new Dictionary<string, object> { ... },
    importance: 0.8
);

// Vector similarity search
var similar = await _vectorService.SearchSimilarMemoriesAsync(
    query: "user likes Python",
    agentId: "michan",
    memoryType: "thought",
    limit: 5
);

// Get by context
var contextMemories = await _vectorService.GetMemoriesByContextAsync(
    contextId: "conversation-123"
);
```

## Memory Retrieval Flow

### During User Conversation (ThoughtController)

```
1. User sends message
2. Retrieve relevant memories BEFORE generating response:
   ├─ Search for semantically similar past interactions
   └─ Get recent context from current conversation
3. Add memories to prompt context
4. Generate response with memory awareness
5. Store the new interaction
```

### During Autonomous Behavior (MiChanAutonomousBehavior)

```
1. Check social feed for posts
2. For each interesting post:
   ├─ Retrieve memories about the author
   ├─ Retrieve similar past interactions
   └─ Add to decision prompt
3. Decide whether to reply/react
4. If replying, include memory context in response generation
5. Store the autonomous action
```

## Best Practices

### 1. Memory Types Organization

```csharp
// Good: Specific, descriptive types
"user_question"
"user_preference"
"autonomous_reply"
"autonomous_repost"
"error_occurred"

// Bad: Generic, vague types
"interaction"
"memory"
"event"
```

### 2. Context ID Strategy

```csharp
// For user conversations
$"thought_{accountId}_{sequenceId}"

// For social posts
$"post_{postId}"

// For user-specific memories
$"user_{accountId}"

// For global knowledge
"global"
```

### 3. Content Optimization

Store **searchable, meaningful content**:

```csharp
// Good: Clear, searchable text
content: "User asked about API pricing for enterprise plans"

// Bad: Vague or overly technical
content: "msg_12345_received"
```

### 4. Metadata Usage

Use metadata for structured data that shouldn't be searched:

```csharp
metadata: new Dictionary<string, object>
{
    ["user_id"] = userId,           // For filtering
    ["confidence"] = 0.95,          // For ranking
    ["source"] = "api",             // For tracking
    ["tags"] = new[] { "urgent" }   // For categorization
}
```

### 5. Importance Scoring

```csharp
// High importance: User preferences, critical info
importance: 0.9

// Medium importance: Regular conversations
importance: 0.5

// Low importance: Routine acknowledgments
importance: 0.2
```

## Advanced Usage

### Custom Memory Queries

```csharp
// Find all memories about a specific topic
var memories = await _vectorService.SearchSimilarMemoriesAsync(
    query: "machine learning Python tutorial",
    agentId: "michan",
    memoryType: "thought",
    limit: 10,
    minRelevanceScore: 0.8  // Only highly relevant
);

// Get all interactions with a user
var userMemories = await _vectorService.GetRecentMemoriesAsync(
    agentId: "michan",
    memoryType: "thought",
    limit: 50
);

// Archive old, low-importance memories
await _vectorService.CleanupOldMemoriesAsync(
    maxAge: TimeSpan.FromDays(30),
    importanceThreshold: 0.3
);
```

### Memory-Aware Response Generation

```csharp
// In your service/controller
var relevantMemories = await _memoryService.GetRelevantContextAsync(
    contextId: currentConversationId,
    currentQuery: userMessage,
    semanticCount: 5,   // Find 5 similar past conversations
    recentCount: 10     // Include last 10 messages
);

// Build prompt with memory
var prompt = $@"
You are MiChan. You have these relevant memories:
{FormatMemories(relevantMemories)}

User: {userMessage}
You:
";
```

## Troubleshooting

### Issue: "Embedding service not available"

**Cause**: Embedding provider not configured

**Solution**:
```json
"Embeddings": {
    "Provider": "openrouter",
    "ApiKey": "your-key-here"
}
```

### Issue: "No similar memories found"

**Cause**: 
1. Vector database is empty
2. Content is too generic
3. Similarity threshold too high

**Solution**:
- Lower `minSimilarity` threshold
- Check if memories are being stored
- Use more specific search queries

### Issue: "Out of memory"

**Cause**: In-memory cache growing too large

**Solution**:
```json
"MiChan": {
    "Memory": {
        "MaxContextLength": 50  // Reduce from default 100
    }
}
```

### Issue: Database connection errors

**Cause**: AgentVector connection string missing

**Solution**:
```json
"ConnectionStrings": {
    "AgentVector": "Host=localhost;Port=5432;Database=agent_vector;..."
}
```

## Migration Guide

### From Old MiChanInteractions Table

If you have data in the old table:

```bash
# 1. Migrate data to vector store
dotnet run --project DysonNetwork.Insight migrate-michan-data

# 2. Apply EF migration to drop old table
dotnet ef database update --project DysonNetwork.Insight
```

## Performance Tips

1. **Batch Operations**: Store multiple memories in parallel
2. **Indexing**: The vector store automatically creates HNSW indexes
3. **Cleanup**: Run periodic cleanup of old, low-importance memories
4. **Caching**: Recent memories are cached in-memory for fast access

## Future Enhancements

- [ ] Multi-agent memory isolation
- [ ] Memory summarization for long contexts
- [ ] Automatic memory importance scoring
- [ ] Cross-session memory consolidation
- [ ] Memory decay (forgetting old, unused memories)
